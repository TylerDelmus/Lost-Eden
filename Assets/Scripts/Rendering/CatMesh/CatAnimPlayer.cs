using System;
using System.Collections;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class CatAnimPlayer : MonoBehaviour
{
    public const float DefaultBlendSeconds = 0.2f;
    public const float DefaultLoopSmoothSeconds = 0.15f;
    public const int DefaultPriority = 0;
    public const int StrafePriority = 5;
    public const int OverlayPriority = 10;
    public const int LayerBits = 3;
    public const float DefaultStrafeBlendWeight = 0.3f;

    ResourceDatabase _database;
    CatAnimResolver _resolver;
    Transform[] _bones;
    Vector3[] _bindLocalPositions;
    Quaternion[] _bindLocalRotations;
    int _monsterDataId;
    int _animSet;
    string _currentLogicalName;
    bool _hasPose;
    float _loopSmoothSeconds = DefaultLoopSmoothSeconds;

    readonly Dictionary<int, CatAnimRuntimeClip> _clipCache = new Dictionary<int, CatAnimRuntimeClip>();
    const int ClipCacheVersion = 2;
    static readonly Dictionary<(int animId, int boneCount, int version), CatAnimRuntimeClip> SharedClipCache =
        new Dictionary<(int animId, int boneCount, int version), CatAnimRuntimeClip>();
    static readonly object SharedClipGate = new object();

    readonly List<AnimInstance> _instances = new List<AnimInstance>();
    readonly List<AnimInstance> _removeBuffer = new List<AnimInstance>();
    readonly List<AnimInstance> _applyOrder = new List<AnimInstance>();
    readonly List<Action> _completedCallbacks = new List<Action>();

    sealed class AnimInstance
    {
        public CatAnimRuntimeClip Clip;
        public string LogicalName;
        public float Time;
        public float Weight = 1f;
        public float TargetWeight = 1f;
        public float FadeFromWeight;
        public int Priority;
        public int ClaimBits = LayerBits;
        public int ActiveMask = LayerBits;
        public bool OneShot;
        public Action OnComplete;
        public float FadeDuration;
        public float FadeElapsed;
        public bool FadingIn;
        public bool FadingOut;
        public bool OutgoingCrossFade;
        public bool UseUnscaledTime;
    }

    public int MonsterDataId => _monsterDataId;
    public int AnimSet => _animSet;
    public string CurrentLogicalName => _currentLogicalName;
    public CatAnimRuntimeClip CurrentClip => GetCurrentBase()?.Clip;
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
        get
        {
            AnimInstance current = GetCurrentBase();
            return current != null ? current.Time : 0f;
        }
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

    public bool HasOverlay => FindByPriority(OverlayPriority) != null;

    public void SetTime(float time)
    {
        if (!_hasPose)
            return;

        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != DefaultPriority || instance.Clip == null)
                continue;

            float duration = Mathf.Max(instance.Clip.Duration, 0.001f);
            instance.Time = Mathf.Clamp(time, 0f, duration);
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
        _hasPose = false;
        _instances.Clear();
        _applyOrder.Clear();
        _clipCache.Clear();

        CacheBindPose();

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
        => Play(logicalName, blendSeconds, DefaultPriority, LayerBits);

    public bool Play(string logicalName, float blendSeconds, int priority, int claimBits)
        => Play(logicalName, blendSeconds, priority, claimBits, 1f);

    public bool Play(string logicalName, float blendSeconds, int priority, int claimBits, float targetWeight)
    {
        if (!TryResolve(logicalName, out string normalized, out int animId))
            return false;

        if (IsStableAt(priority, normalized))
        {
            // Same clip is already on this channel. A later Play is an arbitration write.
            Arbitrate();
            return true;
        }

        if (priority == DefaultPriority)
            ClearBaseOneShot();

        if (!PlayResolved(
            animId,
            normalized,
            blendSeconds,
            priority,
            claimBits,
            Mathf.Clamp01(targetWeight),
            oneShot: false,
            null,
            unscaledTime: false))
            return false;

        if (priority == DefaultPriority)
            _currentLogicalName = normalized;
        return true;
    }

    public bool PlayStrafe(string logicalName, float blendSeconds = DefaultBlendSeconds)
        => Play(logicalName, blendSeconds, StrafePriority, 0, DefaultStrafeBlendWeight);

    public void CancelStrafe(float blendSeconds = DefaultBlendSeconds)
        => FadeOutPriority(StrafePriority, blendSeconds);

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
        ClearBaseOneShot();
    }

    public void CancelOverlay()
    {
        RemoveByPriority(OverlayPriority, invokeComplete: false);
    }

    public bool PlayOverlayOnce(string logicalName, float blendSeconds, Action onComplete)
        => PlayOverlayOnce(logicalName, blendSeconds, onComplete, OverlayPriority, LayerBits);

    /// <summary>
    /// Play a one-shot at a higher priority. Locomotion keeps advancing underneath.
    /// Disabled base tracks are skipped; ending this clip does not restore their mask.
    /// </summary>
    public bool PlayOverlayOnce(
        string logicalName,
        float blendSeconds,
        Action onComplete,
        int priority,
        int claimBits)
    {
        if (!TryResolve(logicalName, out string normalized, out int animId, overlay: true))
        {
            onComplete?.Invoke();
            return false;
        }

        CatAnimRuntimeClip clip = EnsureClip(animId);
        if (clip == null)
        {
            onComplete?.Invoke();
            return false;
        }

        RemoveByPriority(priority, invokeComplete: false);
        return PlayResolved(
            animId,
            normalized,
            blendSeconds,
            priority,
            claimBits,
            1f,
            oneShot: true,
            onComplete,
            unscaledTime: true);
    }

    public bool PlayOnce(string logicalName, float blendSeconds, Action onComplete)
        => PlayOnce(logicalName, blendSeconds, onComplete, DefaultPriority, LayerBits);

    public bool PlayOnce(string logicalName, float blendSeconds, Action onComplete, int priority, int claimBits)
    {
        if (!TryResolve(logicalName, out string normalized, out int animId))
        {
            onComplete?.Invoke();
            return false;
        }

        if (!PlayResolved(
            animId,
            normalized,
            blendSeconds,
            priority,
            claimBits,
            1f,
            oneShot: true,
            onComplete,
            unscaledTime: false))
        {
            onComplete?.Invoke();
            return false;
        }

        if (priority == DefaultPriority)
            _currentLogicalName = normalized;
        return true;
    }

    public bool PlayAnimId(int animId, float blendSeconds = DefaultBlendSeconds)
        => PlayAnimId(animId, blendSeconds, DefaultPriority, LayerBits);

    public bool PlayAnimId(int animId, float blendSeconds, int priority, int claimBits)
    {
        if (priority == DefaultPriority)
            _currentLogicalName = null;
        return PlayResolved(animId, null, blendSeconds, priority, claimBits, 1f, oneShot: false, null, unscaledTime: false);
    }

    public bool CrossFadeAnimId(int animId, float blendSeconds = DefaultBlendSeconds)
        => CrossFadeAnimId(animId, blendSeconds, DefaultPriority, LayerBits);

    public bool CrossFadeAnimId(int animId, float blendSeconds, int priority, int claimBits)
    {
        _currentLogicalName = null;
        if (!HasInstanceAt(priority) || blendSeconds <= 0f)
            return PlayAnimId(animId, 0f, priority, claimBits);

        return PlayResolved(
            animId,
            null,
            Mathf.Max(0.01f, blendSeconds),
            priority,
            claimBits,
            1f,
            oneShot: false,
            null,
            unscaledTime: false);
    }

    public bool BlendAnims(int animIdA, int animIdB, float weightB, float fadeSeconds = DefaultBlendSeconds)
    {
        CatAnimRuntimeClip clipA = EnsureClip(animIdA);
        CatAnimRuntimeClip clipB = EnsureClip(animIdB);
        if (clipA == null || clipB == null)
            return false;

        _currentLogicalName = null;
        RemoveByPriority(DefaultPriority, invokeComplete: false);

        float weight = Mathf.Clamp01(weightB);
        _instances.Add(CreateInstance(clipA, null, DefaultPriority, LayerBits, 1f, oneShot: false, null, unscaledTime: false));
        _instances.Add(CreateInstance(clipB, null, DefaultPriority, LayerBits, weight, oneShot: false, null, unscaledTime: false));
        RebuildApplyOrder();
        Arbitrate();
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
        var sharedKey = (animId, boneCount, ClipCacheVersion);
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

    bool TryResolve(string logicalName, out string normalized, out int animId, bool overlay = false)
    {
        normalized = null;
        animId = 0;
        if (_database?.Rdb == null || _bones == null || _bones.Length == 0 || _resolver == null)
            return false;

        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        normalized = logicalName.Trim().ToLowerInvariant();
        if (_resolver.TryResolve(_monsterDataId, _animSet, normalized, out animId, out _))
            return true;

        string kind = overlay ? "overlay anim" : "anim";
        Debug.LogWarning(
            $"CatAnimPlayer: No {kind} for '{normalized}' (MonsterData={_monsterDataId}, AnimSet={_animSet}).");
        return false;
    }

    bool PlayResolved(
        int animId,
        string logicalName,
        float blendSeconds,
        int priority,
        int claimBits,
        float targetWeight,
        bool oneShot,
        Action onComplete,
        bool unscaledTime)
    {
        CatAnimRuntimeClip clip = EnsureClip(animId);
        if (clip == null)
            return false;

        claimBits = SanitizeClaimBits(claimBits);
        targetWeight = Mathf.Clamp01(targetWeight);
        CollapsePriorityChannel(priority);

        bool fade = blendSeconds > 0f;
        if (!fade)
            RemoveByPriority(priority, invokeComplete: false);
        else if (HasInstanceAt(priority))
            MarkOutgoing(priority);

        AnimInstance incoming = CreateInstance(
            clip,
            logicalName,
            priority,
            claimBits,
            fade ? 0f : targetWeight,
            targetWeight,
            oneShot,
            onComplete,
            unscaledTime);
        incoming.FadingIn = fade;
        incoming.FadeFromWeight = 0f;
        incoming.FadeDuration = Mathf.Max(0.01f, blendSeconds);
        incoming.FadeElapsed = 0f;
        _instances.Add(incoming);
        RebuildApplyOrder();

        Arbitrate();
        _hasPose = true;
        ApplyPose();
        return true;
    }

    static AnimInstance CreateInstance(
        CatAnimRuntimeClip clip,
        string logicalName,
        int priority,
        int claimBits,
        float weight,
        bool oneShot,
        Action onComplete,
        bool unscaledTime)
        => CreateInstance(clip, logicalName, priority, claimBits, weight, 1f, oneShot, onComplete, unscaledTime);

    static AnimInstance CreateInstance(
        CatAnimRuntimeClip clip,
        string logicalName,
        int priority,
        int claimBits,
        float weight,
        float targetWeight,
        bool oneShot,
        Action onComplete,
        bool unscaledTime)
    {
        return new AnimInstance
        {
            Clip = clip,
            LogicalName = logicalName,
            Time = 0f,
            Weight = weight,
            TargetWeight = Mathf.Clamp01(targetWeight),
            FadeFromWeight = weight,
            Priority = priority,
            ClaimBits = SanitizeClaimBits(claimBits),
            ActiveMask = LayerBits,
            OneShot = oneShot,
            OnComplete = onComplete,
            UseUnscaledTime = unscaledTime
        };
    }

    static int SanitizeClaimBits(int claimBits)
        => claimBits & LayerBits;

    void Arbitrate()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            int higherClaims = 0;
            for (int j = 0; j < _instances.Count; j++)
            {
                AnimInstance other = _instances[j];
                if (other.Priority > instance.Priority)
                    higherClaims |= other.ClaimBits;
            }

            instance.ActiveMask = (~higherClaims) & LayerBits;
        }
    }

    void LateUpdate()
    {
        if (!_hasPose || _bones == null || _instances.Count == 0)
            return;

        if (!Paused)
        {
            float scaledDt = UnityEngine.Time.deltaTime * PlaybackSpeed;
            float unscaledDt = UnityEngine.Time.deltaTime;

            for (int i = 0; i < _instances.Count; i++)
            {
                AnimInstance instance = _instances[i];
                float dt = instance.UseUnscaledTime ? unscaledDt : scaledDt;
                AdvanceInstance(instance, dt);
                AdvanceFade(instance, dt);
            }

            FlushRemovals();
            InvokeCompletedCallbacks();
        }

        ApplyPose();
    }

    void AdvanceInstance(AnimInstance instance, float dt)
    {
        if (instance.Clip == null)
            return;

        float clipDuration = instance.OneShot ? instance.Clip.GetOneShotDuration() : instance.Clip.Duration;
        if (clipDuration <= 0f)
            return;

        instance.Time += dt;

        if (instance.OutgoingCrossFade)
        {
            instance.Time = Mathf.Min(instance.Time, clipDuration);
            return;
        }

        if (instance.FadingIn && instance.OneShot)
        {
            if (instance.Time > clipDuration)
                instance.Time = clipDuration;
            if (instance.Time < clipDuration)
                return;
        }

        if (instance.OneShot)
        {
            if (instance.Time < clipDuration)
                return;

            instance.Time = clipDuration;
            instance.OneShot = false;
            if (instance.Clip.HasLoopTiming && instance.Clip.LoopStart > 0.001f && instance.Priority == DefaultPriority)
                instance.Time = 0f;

            Action callback = instance.OnComplete;
            instance.OnComplete = null;
            if (instance.Priority != DefaultPriority)
                _removeBuffer.Add(instance);
            if (callback != null)
                _completedCallbacks.Add(callback);
            return;
        }

        if (instance.Time > clipDuration)
            instance.Time %= clipDuration;
    }

    void AdvanceFade(AnimInstance instance, float dt)
    {
        if (!instance.FadingIn && !instance.FadingOut)
            return;

        instance.FadeElapsed += dt;
        float t = Mathf.Clamp01(instance.FadeElapsed / instance.FadeDuration);
        instance.Weight = Mathf.Lerp(instance.FadeFromWeight, instance.TargetWeight, t);
        if (t < 1f)
            return;

        instance.Weight = instance.TargetWeight;
        instance.FadingIn = false;
        if (instance.FadingOut)
        {
            instance.FadingOut = false;
            _removeBuffer.Add(instance);
            return;
        }

        RemoveOutgoing(instance.Priority);
    }

    void ApplyPose()
    {
        if (_bones == null)
            return;

        for (int boneIndex = 0; boneIndex < _bones.Length; boneIndex++)
        {
            Transform bone = _bones[boneIndex];
            if (bone == null)
                continue;

            Vector3 pos = _bindLocalPositions[boneIndex];
            Quaternion rot = _bindLocalRotations[boneIndex];

            for (int i = 0; i < _applyOrder.Count; i++)
            {
                AnimInstance instance = _applyOrder[i];
                if (instance?.Clip == null || instance.Weight <= 0f)
                    continue;
                if (!instance.Clip.IsTrackEnabled(boneIndex, instance.ActiveMask))
                    continue;

                EvaluateBone(instance, boneIndex, out Vector3? samplePos, out Quaternion? sampleRot);
                if (instance.Weight >= 1f)
                {
                    if (samplePos.HasValue)
                        pos = samplePos.Value;
                    if (sampleRot.HasValue)
                        rot = sampleRot.Value;
                    continue;
                }

                if (samplePos.HasValue)
                    pos = Vector3.Lerp(pos, samplePos.Value, instance.Weight);
                if (sampleRot.HasValue)
                    rot = Quaternion.Slerp(rot, sampleRot.Value, instance.Weight);
            }

            bone.localPosition = pos;
            bone.localRotation = rot;
        }
    }

    void EvaluateBone(
        AnimInstance instance,
        int boneIndex,
        out Vector3? localPosition,
        out Quaternion? localRotation)
    {
        bool absoluteSourceTime = instance.OneShot && !instance.OutgoingCrossFade;
        instance.Clip.Evaluate(boneIndex, instance.Time, absoluteSourceTime, out localPosition, out localRotation);

        float duration = absoluteSourceTime ? instance.Clip.GetOneShotDuration() : instance.Clip.Duration;
        if (instance.Time >= duration)
            return;

        float blend = _loopSmoothSeconds;
        if (instance.OneShot || instance.OutgoingCrossFade)
            blend = 0f;

        if (blend <= 0f || duration <= blend)
            return;

        float windowStart = duration - blend;
        if (instance.Time < windowStart)
            return;

        float w = Mathf.SmoothStep(0f, 1f, (instance.Time - windowStart) / blend);
        instance.Clip.Evaluate(boneIndex, 0f, out Vector3? startPos, out Quaternion? startRot);

        if (localPosition.HasValue && startPos.HasValue)
            localPosition = Vector3.Lerp(localPosition.Value, startPos.Value, w);
        else if (startPos.HasValue)
            localPosition = startPos;

        if (localRotation.HasValue && startRot.HasValue)
            localRotation = Quaternion.Slerp(localRotation.Value, startRot.Value, w);
        else if (startRot.HasValue)
            localRotation = startRot;
    }

    AnimInstance GetCurrentBase()
    {
        AnimInstance best = null;
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != DefaultPriority)
                continue;
            if (best == null || instance.Weight > best.Weight || (instance.FadingIn && !best.FadingIn))
                best = instance;
        }

        return best;
    }

    bool HasInstanceAt(int priority)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].Priority == priority)
                return true;
        }

        return false;
    }

    bool IsFadingAt(int priority)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority == priority && (instance.FadingIn || instance.FadingOut))
                return true;
        }

        return false;
    }

    bool IsStableAt(int priority, string logicalName)
    {
        if (IsFadingAt(priority))
            return false;

        if (priority == DefaultPriority)
            return string.Equals(_currentLogicalName, logicalName, StringComparison.Ordinal);

        AnimInstance instance = FindByPriority(priority);
        return instance != null
            && string.Equals(instance.LogicalName, logicalName, StringComparison.Ordinal);
    }

    void FadeOutPriority(int priority, float blendSeconds)
    {
        if (blendSeconds <= 0f)
        {
            RemoveByPriority(priority, invokeComplete: false);
            return;
        }

        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != priority)
                continue;

            instance.FadingOut = true;
            instance.FadingIn = false;
            instance.OutgoingCrossFade = false;
            instance.OneShot = false;
            instance.OnComplete = null;
            instance.FadeFromWeight = instance.Weight;
            instance.TargetWeight = 0f;
            instance.FadeDuration = Mathf.Max(0.01f, blendSeconds);
            instance.FadeElapsed = 0f;
        }
    }

    AnimInstance FindByPriority(int priority)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].Priority == priority)
                return _instances[i];
        }

        return null;
    }

    void CollapsePriorityChannel(int priority)
    {
        AnimInstance keep = null;
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != priority)
                continue;
            if (keep == null || instance.Weight > keep.Weight)
                keep = instance;
        }

        if (keep == null)
            return;

        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority == priority && instance != keep)
                _instances.RemoveAt(i);
        }

        keep.FadingIn = false;
        keep.FadingOut = false;
        keep.OutgoingCrossFade = false;
        keep.Weight = Mathf.Max(keep.Weight, 0.001f);
        RebuildApplyOrder();
    }

    void MarkOutgoing(int priority)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != priority)
                continue;
            instance.OutgoingCrossFade = true;
            instance.FadingIn = false;
            instance.OneShot = false;
            instance.OnComplete = null;
        }
    }

    void RemoveOutgoing(int priority)
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority == priority && instance.OutgoingCrossFade)
                _instances.RemoveAt(i);
        }

        RebuildApplyOrder();
    }

    void RemoveByPriority(int priority, bool invokeComplete)
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != priority)
                continue;

            _instances.RemoveAt(i);
            if (invokeComplete)
                instance.OnComplete?.Invoke();
        }

        RebuildApplyOrder();
    }

    void ClearBaseOneShot()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            AnimInstance instance = _instances[i];
            if (instance.Priority != DefaultPriority)
                continue;
            instance.OneShot = false;
            instance.OnComplete = null;
        }
    }

    void FlushRemovals()
    {
        if (_removeBuffer.Count == 0)
            return;

        for (int i = 0; i < _removeBuffer.Count; i++)
            _instances.Remove(_removeBuffer[i]);
        _removeBuffer.Clear();
        RebuildApplyOrder();
    }

    void InvokeCompletedCallbacks()
    {
        if (_completedCallbacks.Count == 0)
            return;

        for (int i = 0; i < _completedCallbacks.Count; i++)
            _completedCallbacks[i]?.Invoke();
        _completedCallbacks.Clear();
    }

    void RebuildApplyOrder()
    {
        _applyOrder.Clear();
        for (int i = 0; i < _instances.Count; i++)
            _applyOrder.Add(_instances[i]);

        _applyOrder.Sort(CompareApplyOrder);
    }

    static int CompareApplyOrder(AnimInstance a, AnimInstance b)
    {
        int priority = a.Priority.CompareTo(b.Priority);
        if (priority != 0)
            return priority;

        int fade = a.FadingIn.CompareTo(b.FadingIn);
        if (fade != 0)
            return fade;

        return b.Weight.CompareTo(a.Weight);
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
