using System;
using System.Collections;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class CatAnimPlayer : MonoBehaviour
{
    public const float DefaultBlendSeconds = 0.2f;
    public const float DefaultLoopSmoothSeconds = 0.15f;

    ResourceDatabase _database;
    CatAnimResolver _resolver;
    Transform[] _bones;
    Vector3[] _bindLocalPositions;
    Quaternion[] _bindLocalRotations;
    int _monsterDataId;
    int _animSet;
    string _currentLogicalName;

    readonly Dictionary<int, CatAnimRuntimeClip> _clipCache = new Dictionary<int, CatAnimRuntimeClip>();
    static readonly Dictionary<(int animId, int boneCount), CatAnimRuntimeClip> SharedClipCache =
        new Dictionary<(int animId, int boneCount), CatAnimRuntimeClip>();
    static readonly object SharedClipGate = new object();

    CatAnimRuntimeClip _clipA;
    CatAnimRuntimeClip _clipB;
    float _timeA;
    float _timeB;
    float _weightB;
    float _fadeDuration;
    float _fadeElapsed;
    bool _isCrossFading;
    bool _hasPose;
    float _loopSmoothSeconds = DefaultLoopSmoothSeconds;
    bool _isOneShot;
    Action _oneShotComplete;

    // Overlay layer (e.g. jump-land blended on top of locomotion).
    CatAnimRuntimeClip _overlayClip;
    float _overlayTime;
    float _overlayWeight;
    float _overlayFadeDuration;
    float _overlayFadeElapsed;
    bool _overlayFadingIn;
    bool _overlayFadingOut;
    bool _overlayOneShot;
    Action _overlayComplete;
    string _overlayLogicalName;

    public int MonsterDataId => _monsterDataId;
    public int AnimSet => _animSet;
    public string CurrentLogicalName => _currentLogicalName;
    public CatAnimRuntimeClip CurrentClip => _weightB >= 0.5f && _clipB != null ? _clipB : _clipA;
    public int CurrentAnimId
    {
        get
        {
            CatAnimRuntimeClip clip = CurrentClip;
            return clip != null ? clip.AnimId : 0;
        }
    }
    public bool Paused { get; set; }
    public float PlaybackSpeed { get; set; } = 1f;

    /// <summary>
    /// Near the end of a looping clip, blend toward the start pose over this many seconds
    /// so the wrap is continuous when end/start don't match exactly.
    /// </summary>
    public float LoopSmoothSeconds
    {
        get => _loopSmoothSeconds;
        set => _loopSmoothSeconds = Mathf.Max(0f, value);
    }

    public float PlaybackTime
    {
        get => _weightB >= 0.5f && _clipB != null ? _timeB : _timeA;
        set => SetTime(value);
    }
    public float Duration
    {
        get
        {
            CatAnimRuntimeClip clip = CurrentClip;
            return clip != null ? Mathf.Max(clip.Duration, 0.001f) : 0f;
        }
    }

    public void SetTime(float time)
    {
        if (!_hasPose || _clipA == null)
            return;

        float durationA = Mathf.Max(_clipA.Duration, 0.001f);
        _timeA = Mathf.Clamp(time, 0f, durationA);

        if (_clipB != null)
        {
            float durationB = Mathf.Max(_clipB.Duration, 0.001f);
            _timeB = Mathf.Clamp(time, 0f, durationB);
        }

        ApplyPose();
    }

    public void Initialize(ResourceDatabase database, Transform[] bones, int monsterDataId, int animSet = 0)
    {
        _database = database;
        _bones = bones;
        _monsterDataId = monsterDataId;
        _animSet = animSet;
        _resolver = new CatAnimResolver(database);
        _currentLogicalName = null;
        _clipA = null;
        _clipB = null;
        _weightB = 0f;
        _isCrossFading = false;
        _hasPose = false;
        ClearOverlay(invokeComplete: false);
        _clipCache.Clear();

        CacheBindPose();

        // Remove leftover legacy Animation if present from older builds.
        if (TryGetComponent(out Animation legacy))
            Destroy(legacy);
    }

    public void SetAnimSet(int animSet)
    {
        if (_animSet == animSet)
            return;

        _animSet = animSet;
        if (!string.IsNullOrEmpty(_currentLogicalName))
            Play(_currentLogicalName, DefaultBlendSeconds);
    }

    public void SetMonsterDataId(int monsterDataId)
    {
        _monsterDataId = monsterDataId;
    }

    public void InvalidateClipCache(int animId = 0)
    {
        if (animId <= 0)
        {
            _clipCache.Clear();
            return;
        }

        _clipCache.Remove(animId);
    }

    public bool Play(string logicalName, float blendSeconds = DefaultBlendSeconds)
    {
        if (_database?.Rdb == null || _bones == null || _bones.Length == 0 || _resolver == null)
            return false;

        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        string normalized = logicalName.Trim().ToLowerInvariant();
        if (string.Equals(_currentLogicalName, normalized, StringComparison.Ordinal) && !_isCrossFading)
            return true;

        _isOneShot = false;
        _oneShotComplete = null;

        if (!_resolver.TryResolve(_monsterDataId, _animSet, normalized, out int animId, out _))
        {
            Debug.LogWarning(
                $"CatAnimPlayer: No anim for '{normalized}' (MonsterData={_monsterDataId}, AnimSet={_animSet}).");
            return false;
        }

        if (!PlayAnimId(animId, blendSeconds))
            return false;

        _currentLogicalName = normalized;
        return true;
    }

    /// <summary>
    /// Resolve/play on the next frame so Instantiate + ApplyPose don't stack on the load frame.
    /// </summary>
    public void PlayDeferred(string logicalName, float blendSeconds = 0f)
    {
        if (!isActiveAndEnabled)
        {
            Play(logicalName, blendSeconds);
            return;
        }

        StartCoroutine(PlayDeferredRoutine(logicalName, blendSeconds));
    }

    IEnumerator PlayDeferredRoutine(string logicalName, float blendSeconds)
    {
        yield return null;
        Play(logicalName, blendSeconds);
    }

    public void CancelOneShot()
    {
        _isOneShot = false;
        _oneShotComplete = null;
    }

    public void CancelOverlay()
    {
        ClearOverlay(invokeComplete: false);
    }

    public bool HasOverlay => _overlayClip != null && _overlayWeight > 0.001f;

    /// <summary>
    /// Play a one-shot blended on top of the current base clip (locomotion keeps playing).
    /// </summary>
    public bool PlayOverlayOnce(string logicalName, float blendSeconds, Action onComplete)
    {
        if (_database?.Rdb == null || _bones == null || _bones.Length == 0 || _resolver == null)
            return false;

        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        string normalized = logicalName.Trim().ToLowerInvariant();
        if (!_resolver.TryResolve(_monsterDataId, _animSet, normalized, out int animId, out _))
        {
            Debug.LogWarning(
                $"CatAnimPlayer: No overlay anim for '{normalized}' (MonsterData={_monsterDataId}, AnimSet={_animSet}).");
            onComplete?.Invoke();
            return false;
        }

        CatAnimRuntimeClip clip = EnsureClip(animId);
        if (clip == null)
        {
            onComplete?.Invoke();
            return false;
        }

        ClearOverlay(invokeComplete: false);

        _overlayClip = clip;
        _overlayTime = 0f;
        _overlayLogicalName = normalized;
        _overlayOneShot = true;
        _overlayComplete = onComplete;
        _overlayFadeDuration = Mathf.Max(0.01f, blendSeconds);
        _overlayFadeElapsed = 0f;
        _overlayFadingIn = blendSeconds > 0f;
        _overlayFadingOut = false;
        _overlayWeight = _overlayFadingIn ? 0f : 1f;
        _hasPose = true;
        ApplyPose();
        return true;
    }

    public bool PlayOnce(string logicalName, float blendSeconds, Action onComplete)
    {
        if (_database?.Rdb == null || _bones == null || _bones.Length == 0 || _resolver == null)
            return false;

        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        string normalized = logicalName.Trim().ToLowerInvariant();
        if (!_resolver.TryResolve(_monsterDataId, _animSet, normalized, out int animId, out _))
        {
            Debug.LogWarning(
                $"CatAnimPlayer: No anim for '{normalized}' (MonsterData={_monsterDataId}, AnimSet={_animSet}).");
            onComplete?.Invoke();
            return false;
        }

        _isOneShot = true;
        _oneShotComplete = onComplete;

        if (!PlayAnimId(animId, blendSeconds))
        {
            _isOneShot = false;
            _oneShotComplete = null;
            onComplete?.Invoke();
            return false;
        }

        _currentLogicalName = normalized;
        return true;
    }

    public bool PlayAnimId(int animId, float blendSeconds = DefaultBlendSeconds)
    {
        CatAnimRuntimeClip clip = EnsureClip(animId);
        if (clip == null)
            return false;

        _currentLogicalName = null;

        if (!_hasPose || blendSeconds <= 0f || _clipA == null)
        {
            _clipA = clip;
            _clipB = null;
            _timeA = 0f;
            _timeB = 0f;
            _weightB = 0f;
            _isCrossFading = false;
            _hasPose = true;
            ApplyPose();
            return true;
        }

        return CrossFadeTo(clip, blendSeconds);
    }

    public bool CrossFadeAnimId(int animId, float blendSeconds = DefaultBlendSeconds)
    {
        CatAnimRuntimeClip clip = EnsureClip(animId);
        if (clip == null)
            return false;

        _currentLogicalName = null;
        if (_clipA == null || blendSeconds <= 0f)
            return PlayAnimId(animId, 0f);

        return CrossFadeTo(clip, Mathf.Max(0.01f, blendSeconds));
    }

    public bool BlendAnims(int animIdA, int animIdB, float weightB, float fadeSeconds = DefaultBlendSeconds)
    {
        CatAnimRuntimeClip clipA = EnsureClip(animIdA);
        CatAnimRuntimeClip clipB = EnsureClip(animIdB);
        if (clipA == null || clipB == null)
            return false;

        _currentLogicalName = null;
        _clipA = clipA;
        _clipB = clipB;
        _timeA = 0f;
        _timeB = 0f;
        _weightB = Mathf.Clamp01(weightB);
        _isCrossFading = false;
        _hasPose = true;
        ApplyPose();
        return true;
    }

    public CatAnimRuntimeClip EnsureClip(int animId)
    {
        if (_database?.Rdb == null || _bones == null || _bones.Length == 0 || animId <= 0)
            return null;

        if (_clipCache.TryGetValue(animId, out CatAnimRuntimeClip cached) && cached != null)
            return cached;

        int boneCount = _bones.Length;
        var sharedKey = (animId, boneCount);
        lock (SharedClipGate)
        {
            if (SharedClipCache.TryGetValue(sharedKey, out CatAnimRuntimeClip shared) && shared != null)
            {
                _clipCache[animId] = shared;
                return shared;
            }
        }

        CATAnim catAnim;
        try
        {
            catAnim = _database.Get<CATAnim>(ResourceTypeId.Anim, animId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CatAnimPlayer: Failed to load CATAnim {animId} ({ex.Message}).");
            return null;
        }

        if (catAnim == null)
        {
            Debug.LogWarning($"CatAnimPlayer: CATAnim {animId} not found.");
            return null;
        }

        CatAnimRuntimeClip clip = CatAnimRuntimeClip.Create(catAnim, animId, boneCount);
        if (clip == null)
        {
            Debug.LogWarning(
                $"CatAnimPlayer: CATAnim {animId} produced no bone tracks (BoneCount={catAnim.BoneCount}, bones={boneCount}).");
            return null;
        }

        lock (SharedClipGate)
        {
            if (SharedClipCache.TryGetValue(sharedKey, out CatAnimRuntimeClip raced) && raced != null)
                clip = raced;
            else
                SharedClipCache[sharedKey] = clip;
        }

        _clipCache[animId] = clip;
        return clip;
    }

    bool CrossFadeTo(CatAnimRuntimeClip clip, float blendSeconds)
    {
        if (_clipB != null && _weightB > 0.001f)
        {
            // Collapse current blend into A as the crossfade source.
            _clipA = _weightB >= 0.5f ? _clipB : _clipA;
            _timeA = _weightB >= 0.5f ? _timeB : _timeA;
        }

        _clipB = clip;
        _timeB = 0f;
        _weightB = 0f;
        _fadeDuration = Mathf.Max(0.01f, blendSeconds);
        _fadeElapsed = 0f;
        _isCrossFading = true;
        _hasPose = true;
        ApplyPose();
        return true;
    }

    void LateUpdate()
    {
        if (!_hasPose || _bones == null || _clipA == null)
            return;

        if (!Paused)
        {
            float dt = UnityEngine.Time.deltaTime * PlaybackSpeed;
            AdvanceClipTime(ref _timeA, _clipA, dt, isClipA: true);

            if (_clipB != null)
                AdvanceClipTime(ref _timeB, _clipB, dt, isClipA: false);

            if (_isCrossFading && _clipB != null)
            {
                _fadeElapsed += dt;
                _weightB = Mathf.Clamp01(_fadeElapsed / _fadeDuration);
                if (_weightB >= 1f)
                {
                    _clipA = _clipB;
                    _timeA = _timeB;
                    _clipB = null;
                    _weightB = 0f;
                    _isCrossFading = false;
                }
            }

            // Overlay advances at authored rate (not locomotion playback scaling).
            AdvanceOverlay(UnityEngine.Time.deltaTime);
        }

        ApplyPose();
    }

    void AdvanceOverlay(float dt)
    {
        if (_overlayClip == null)
            return;

        if (_overlayFadingIn)
        {
            _overlayFadeElapsed += dt;
            _overlayWeight = Mathf.Clamp01(_overlayFadeElapsed / _overlayFadeDuration);
            if (_overlayWeight >= 1f)
            {
                _overlayWeight = 1f;
                _overlayFadingIn = false;
                _overlayFadeElapsed = 0f;
            }
        }
        else if (_overlayFadingOut)
        {
            _overlayFadeElapsed += dt;
            _overlayWeight = 1f - Mathf.Clamp01(_overlayFadeElapsed / _overlayFadeDuration);
            if (_overlayWeight <= 0f)
            {
                ClearOverlay(invokeComplete: false);
                return;
            }
        }

        if (!_overlayOneShot)
            return;

        float end = _overlayClip.GetOneShotDuration();
        _overlayTime += dt;
        if (_overlayTime < end)
            return;

        _overlayTime = end;
        _overlayOneShot = false;
        Action callback = _overlayComplete;
        _overlayComplete = null;

        // Fade overlay out while locomotion continues underneath.
        _overlayFadingOut = true;
        _overlayFadingIn = false;
        _overlayFadeElapsed = 0f;
        if (_overlayFadeDuration <= 0.01f)
        {
            ClearOverlay(invokeComplete: false);
            callback?.Invoke();
            return;
        }

        callback?.Invoke();
    }

    void ClearOverlay(bool invokeComplete)
    {
        Action callback = invokeComplete ? _overlayComplete : null;
        _overlayClip = null;
        _overlayTime = 0f;
        _overlayWeight = 0f;
        _overlayFadeDuration = 0f;
        _overlayFadeElapsed = 0f;
        _overlayFadingIn = false;
        _overlayFadingOut = false;
        _overlayOneShot = false;
        _overlayComplete = null;
        _overlayLogicalName = null;
        callback?.Invoke();
    }

    void AdvanceClipTime(ref float time, CatAnimRuntimeClip clip, float dt, bool isClipA)
    {
        if (clip == null)
            return;

        bool oneShotClip = _isOneShot && IsOneShotClip(isClipA);
        float clipDuration = oneShotClip ? clip.GetOneShotDuration() : clip.Duration;
        if (clipDuration <= 0f)
            return;

        time += dt;

        // Outgoing clip during crossfade must hold its final frame — wrapping back to t=0
        // causes a one-frame flash (especially after one-shot sit/stand transitions).
        if (_isCrossFading && isClipA)
        {
            time = Mathf.Min(time, clipDuration);
            return;
        }

        if (_clipB != null)
        {
            // Incoming one-shot holds at its end during the fade; don't wrap into the loop region.
            if (oneShotClip)
            {
                if (time > clipDuration)
                    time = clipDuration;
                return;
            }

            if (time > clipDuration)
                time %= clipDuration;
            return;
        }

        if (_isOneShot)
        {
            if (time >= clipDuration)
            {
                time = clipDuration;
                _isOneShot = false;
                // Absolute one-shots end at LoopStart in source time. Remap to loop-local 0 so a
                // same-frame Play crossfade doesn't sample LoopStart+LoopStart.
                if (clip.HasLoopTiming && clip.LoopStart > 0.001f)
                    time = 0f;

                Action callback = _oneShotComplete;
                _oneShotComplete = null;
                callback?.Invoke();
            }

            return;
        }

        time %= clipDuration;
    }

    bool IsOneShotClip(bool isClipA)
    {
        // One-shot is always the incoming/current clip, never the fade-out source.
        if (_clipB != null)
            return !isClipA;

        return isClipA;
    }

    void ApplyPose()
    {
        if (_bones == null || _clipA == null)
            return;

        for (int i = 0; i < _bones.Length; i++)
        {
            Transform bone = _bones[i];
            if (bone == null)
                continue;

            Vector3 pos = _bindLocalPositions[i];
            Quaternion rot = _bindLocalRotations[i];

            EvaluateBone(_clipA, _timeA, i, out Vector3? posA, out Quaternion? rotA);
            if (posA.HasValue)
                pos = posA.Value;
            if (rotA.HasValue)
                rot = rotA.Value;

            if (_clipB != null && _weightB > 0f)
            {
                EvaluateBone(_clipB, _timeB, i, out Vector3? posB, out Quaternion? rotB);
                if (posB.HasValue)
                    pos = Vector3.Lerp(pos, posB.Value, _weightB);
                if (rotB.HasValue)
                    rot = Quaternion.Slerp(rot, rotB.Value, _weightB);
            }

            if (_overlayClip != null && _overlayWeight > 0f)
            {
                EvaluateOverlayBone(i, out Vector3? posO, out Quaternion? rotO);
                if (posO.HasValue)
                    pos = Vector3.Lerp(pos, posO.Value, _overlayWeight);
                if (rotO.HasValue)
                    rot = Quaternion.Slerp(rot, rotO.Value, _overlayWeight);
            }

            bone.localPosition = pos;
            bone.localRotation = rot;
        }
    }

    void EvaluateOverlayBone(int boneIndex, out Vector3? localPosition, out Quaternion? localRotation)
    {
        _overlayClip.Evaluate(boneIndex, _overlayTime, absoluteSourceTime: true, out localPosition, out localRotation);

        float duration = _overlayClip.GetOneShotDuration();
        if (_overlayTime >= duration)
            return;

        // No loop-smooth on overlay one-shots.
    }

    void EvaluateBone(
        CatAnimRuntimeClip clip,
        float time,
        int boneIndex,
        out Vector3? localPosition,
        out Quaternion? localRotation)
    {
        bool absoluteSourceTime = UsesAbsoluteSourceTime(clip);
        clip.Evaluate(boneIndex, time, absoluteSourceTime, out localPosition, out localRotation);

        float duration = absoluteSourceTime ? clip.GetOneShotDuration() : clip.Duration;
        // Held final frame (one-shot end, crossfade source) — do not blend back toward t=0.
        if (time >= duration)
            return;

        float blend = _loopSmoothSeconds;
        if (_isOneShot || (_isCrossFading && clip == _clipA))
            blend = 0f;

        if (blend <= 0f || duration <= blend)
            return;

        float windowStart = duration - blend;
        if (time < windowStart)
            return;

        float w = Mathf.SmoothStep(0f, 1f, (time - windowStart) / blend);
        clip.Evaluate(boneIndex, 0f, out Vector3? startPos, out Quaternion? startRot);

        if (localPosition.HasValue && startPos.HasValue)
            localPosition = Vector3.Lerp(localPosition.Value, startPos.Value, w);
        else if (startPos.HasValue)
            localPosition = startPos;

        if (localRotation.HasValue && startRot.HasValue)
            localRotation = Quaternion.Slerp(localRotation.Value, startRot.Value, w);
        else if (startRot.HasValue)
            localRotation = startRot;
    }

    bool UsesAbsoluteSourceTime(CatAnimRuntimeClip clip)
    {
        if (!_isOneShot || clip == null)
            return false;

        // One-shot target uses source timeline (0 → loopstart/source end).
        if (_clipB != null)
            return clip == _clipB;

        return clip == _clipA;
    }

    void CacheBindPose()
    {
        if (_bones == null)
        {
            _bindLocalPositions = Array.Empty<Vector3>();
            _bindLocalRotations = Array.Empty<Quaternion>();
            return;
        }

        _bindLocalPositions = new Vector3[_bones.Length];
        _bindLocalRotations = new Quaternion[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null)
            {
                _bindLocalPositions[i] = Vector3.zero;
                _bindLocalRotations[i] = Quaternion.identity;
                continue;
            }

            _bindLocalPositions[i] = _bones[i].localPosition;
            _bindLocalRotations[i] = _bones[i].localRotation;
        }
    }
}
