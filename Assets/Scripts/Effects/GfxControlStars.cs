using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 2004 — DiaBill point cloud (up to 128 sprites). Priority cases 0 / 3 / 6·11 / 19.
/// </summary>
public sealed class GfxControlStars : GfxControl
{
    const int MaxPoints = 128;
    const float ImmediateSpawn = -100f;

    struct StarPoint
    {
        public Vector3 Position;
        public Vector3 Anchor;
        public Vector3 Velocity;
        public float SpawnTimer;
        public float Size;
        public float Alpha;
        /// <summary>0 at spawn → 1 at end of particle life. Used for start→end color.</summary>
        public float LifeFrac;
        public bool Active;
    }

    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly int _firstFrame;
    readonly int _lastFrame;
    readonly int _starType;
    readonly float _sizeCurveScale;
    readonly float _spawnRadius;
    readonly float _spawnInterval;
    readonly float _particleLifetime;
    readonly float _turbulence;
    readonly Color _startColor;
    readonly Color _endColor;
    readonly Vector3[] _dirs;
    readonly StarPoint[] _points = new StarPoint[MaxPoints];
    readonly bool _looping;
    readonly EffectLightPool _lights;
    EffectLightLease _lightLease;
    bool _seeded;

    public GfxControlStars(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame,
        EffectLightPool lights = null)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        _lights = lights;

        _starType = record != null ? record.FieldInt(10, 6) : 6;
        _sizeCurveScale = record != null ? Mathf.Max(0.05f, record.Field(28, 0.35f)) : 0.35f;
        _spawnRadius = record != null ? Mathf.Max(0.05f, record.Field(29, 1f)) : 1f;
        // Field 30 is particle lifetime in ms (stock: this+0x16cc = field30/1000).
        float lifeMs = record != null ? record.FieldInt(30, 0) : 0;
        _particleLifetime = lifeMs > 0 ? lifeMs / 1000f : 0f;
        // Field 11 is a per-type tune float — only some cases treat it as a respawn interval.
        float interval = record != null ? record.Field(11, 0f) : 0f;
        if (interval < 0.05f || float.IsNaN(interval))
            interval = _particleLifetime > 0.05f ? _particleLifetime : 0.45f;
        _spawnInterval = interval;
        _turbulence = (record != null ? record.FieldInt(31, 10) : 10) / 100f;

        _startColor = EffectColors.ReadArgbBlock(record, 18, Color.white);
        _endColor = EffectColors.ReadArgbBlock(record, 22, _startColor);
        _looping = _starType != 0x19;

        float duration = record != null ? record.Field(26, 2f) : 2f;
        if (duration < 0f)
            SetDuration(InfiniteDuration);
        else if (IsContinuousEmitter(_starType))
            SetDuration(duration > 0.05f ? duration : InfiniteDuration);
        else
            SetDuration(duration > 0.05f ? duration : 1.6f);

        _dirs = BuildDirectionTemplate();

        if (_lights != null)
            _lights.TryAcquire(out _lightLease);
    }

    static bool IsContinuousEmitter(int starType)
    {
        return starType is 3 or 4 or 5 or 6 or 9 or 10 or 11 or 12 or 14
               || (starType >= 16 && starType <= 20)
               || starType >= 24;
    }

    static Vector3[] BuildDirectionTemplate()
    {
        var dirs = new Vector3[MaxPoints];
        // Deterministic fibonacci-sphere directions (stock pre-bakes a shape into +0x48).
        const float Golden = 2.399963229728653f;
        for (int i = 0; i < MaxPoints; i++)
        {
            float y = 1f - (i / (float)(MaxPoints - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = Golden * i;
            dirs[i] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
        }
        return dirs;
    }

    protected override void OnProcess(float dt)
    {
        EnsureSeeded();

        switch (_starType)
        {
            case 0:
                ProcessCase0();
                break;
            case 3:
                ProcessCase3(dt);
                break;
            case 4:
            case 5:
                ProcessContinuous(sphere: true, stagger: false, turb: 0.1f);
                break;
            case 6:
            case 11:
            case 9:
            case 10:
                ProcessContinuous(sphere: false, stagger: true, turb: _turbulence);
                break;
            case 19:
                ProcessCase19();
                break;
            default:
                // Closest-matching stub for unread cases.
                if (IsContinuousEmitter(_starType))
                    ProcessContinuous(sphere: false, stagger: true, turb: _turbulence);
                else
                    ProcessCase0();
                break;
        }
    }

    void EnsureSeeded()
    {
        if (_seeded)
            return;
        _seeded = true;

        bool continuous = IsContinuousEmitter(_starType) || _starType == 19;
        for (int i = 0; i < MaxPoints; i++)
        {
            _points[i].SpawnTimer = continuous ? ImmediateSpawn : 0f;
            _points[i].Active = false;
            _points[i].Alpha = 1f;
        }
    }

    void ProcessCase0()
    {
        float age = Age;
        float radiusSq = 1.6f - age;
        if (radiusSq < 0f)
        {
            ReadyFlag = true;
            ReleaseLight();
            return;
        }

        float radius = Mathf.Sqrt(radiusSq);
        float sizeBase = (1.5f - age) * 0.25f;
        float sizeScale = Mathf.Max(0.02f, sizeBase * _sizeCurveScale * 2f);
        float lifeFrac = Mathf.Clamp01(age / 1.6f);
        float alpha = Color.Lerp(_startColor, _endColor, lifeFrac).a;

        Matrix4x4 world = WorldMatrix;
        for (int i = 0; i < MaxPoints; i++)
        {
            Vector3 local = _dirs[i] * radius;
            _points[i].Position = world.MultiplyPoint3x4(local);
            _points[i].Size = sizeScale;
            _points[i].Alpha = alpha;
            _points[i].LifeFrac = lifeFrac;
            _points[i].Active = true;
        }
    }

    /// <summary>
    /// Stock starType 3 — exact port of FUN_100f826a case 3.
    /// Per particle:
    ///   +0xc48 worldPos, +0x648 tangent (seeded as horizontal perpendicular to spawn offset).
    /// Each stock frame (no dt in the binary):
    ///   tangent += (locator - worldPos) * 0.1
    ///   worldPos += tangent * 0.1
    /// Stock is frame-rate dependent; we scale 0.1 by dt / FirstFrameDt (~30 FPS) so Unity
    /// high-FPS matches the AO timing.
    /// </summary>
    void ProcessCase3(float dt)
    {
        // 0.1 per Process at ~30 FPS (GfxControl.FirstFrameDt). Clamp avoids a hitch spiral.
        float k = 0.1f * (dt / FirstFrameDt);
        if (k > 0.35f)
            k = 0.35f;
        if (k < 0f)
            k = 0f;

        int spawnBudget = 2;
        Vector3 origin = WorldMatrix.GetColumn(3);
        bool fading = IsTerminating;
        float life = _particleLifetime > 0.05f ? _particleLifetime : 0.9f;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];

            if (p.SpawnTimer > Age)
            {
                // Velocity = tangent accumulator (+0x648); Position = world (+0xc48).
                Vector3 worldPos = p.Position;
                Vector3 tangent = p.Velocity;

                Vector3 delta = origin - worldPos;
                tangent += delta * k;
                worldPos += tangent * k;

                p.Velocity = tangent;
                p.Position = worldPos;
                p.Anchor = worldPos;

                float lifeFrac = (life + Age - p.SpawnTimer) / life;
                lifeFrac = Mathf.Clamp01(lifeFrac);
                float size = (lifeFrac + 0.2f) * _sizeCurveScale * (1f - lifeFrac * lifeFrac);
                p.Size = Mathf.Max(0.05f, size);
                p.Alpha = 1f;
                p.LifeFrac = lifeFrac;
                continue;
            }

            if (spawnBudget == 0 || fading)
            {
                p.Active = false;
                continue;
            }

            // Stock GetRandomPointInSphere (rejection in unit ball), then scale X/Z by field29 only.
            Vector3 offset = RandomPointInUnitBall();
            offset.x *= _spawnRadius;
            offset.z *= _spawnRadius;

            Vector3 spawnPos = origin + offset;
            // Horizontal perpendicular — seeds the orbital component of the swirl.
            Vector3 spawnTangent = new Vector3(offset.z, 0f, -offset.x);

            p.Position = spawnPos;
            p.Anchor = spawnPos;
            p.Velocity = spawnTangent;
            p.SpawnTimer = Age + life;
            p.LifeFrac = 0f;
            p.Alpha = 1f;
            p.Size = Mathf.Max(0.05f, 0.2f * _sizeCurveScale);
            p.Active = true;
            spawnBudget--;
        }
    }

    /// <summary>Stock _GfxControl_t::GetRandomPointInSphere — uniform in the unit ball.</summary>
    static Vector3 RandomPointInUnitBall()
    {
        Vector3 p;
        do
        {
            p = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f));
        } while (p.sqrMagnitude >= 1f);
        return p;
    }

    void ProcessContinuous(bool sphere, bool stagger, float turb)
    {
        int spawnBudget = 2;
        Matrix4x4 world = WorldMatrix;
        Vector3 origin = world.GetColumn(3);
        bool fading = IsTerminating;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            float due = p.SpawnTimer;
            if (stagger)
                due += i / 9.5f;

            if (due <= Age)
            {
                if (spawnBudget == 0 || fading)
                {
                    p.Active = false;
                    continue;
                }

                Vector3 dir = sphere ? RandomInSphere() : RandomOnCircle();
                dir *= _spawnRadius;
                p.Velocity = world.MultiplyVector(dir);
                p.Anchor = origin + p.Velocity;
                if (!sphere)
                    p.Anchor.y -= 1f;
                p.SpawnTimer = Age + _spawnInterval;
                p.Alpha = sphere ? 1f : 0.5f;
                p.LifeFrac = 0f;
                p.Active = true;
                spawnBudget--;
            }
            else if (p.Active)
            {
                // Turbulence drift (stock FUN_1008718b-style small offsets).
                p.Anchor += Random.insideUnitSphere * (turb * 0.02f);
                p.Anchor += p.Velocity.normalized * (0.01f * _spawnRadius);
                p.Position = p.Anchor;

                float lifeFrac = (_spawnInterval + Age - p.SpawnTimer) / Mathf.Max(0.05f, _spawnInterval);
                lifeFrac = Mathf.Clamp01(lifeFrac);
                float size = (lifeFrac + 0.2f) * _sizeCurveScale * (1f - lifeFrac * lifeFrac);
                p.Size = Mathf.Max(0.02f, size);
                p.Alpha = Mathf.Clamp01(1f - lifeFrac);
                p.LifeFrac = lifeFrac;
            }
        }
    }

    void ProcessCase19()
    {
        // Hit-location blend: without a hit-loc system, expand from origin then contract toward it.
        int spawnBudget = 15;
        Matrix4x4 world = WorldMatrix;
        Vector3 origin = world.GetColumn(3);
        float lifeRatio = Duration > 0f ? Mathf.Clamp01(Age / Duration) : Mathf.Clamp01(Age);
        float fade = 1f - lifeRatio;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            if (p.SpawnTimer <= Age)
            {
                if (spawnBudget == 0)
                {
                    p.Active = false;
                    continue;
                }

                Vector3 dir = RandomInSphere() * _spawnRadius;
                p.Velocity = dir;
                p.Anchor = origin + dir;
                p.SpawnTimer = Age + _spawnInterval * 0.5f;
                p.Active = true;
                spawnBudget--;
            }

            if (!p.Active)
                continue;

            Vector3 start = origin + p.Velocity;
            Vector3 end = origin;
            p.Position = Vector3.Lerp(start, end, lifeRatio);
            float size = (fade + 0.2f) * _sizeCurveScale * (1f - lifeRatio * lifeRatio);
            p.Size = Mathf.Max(0.02f, size);
            p.Alpha = fade;
            p.LifeFrac = lifeRatio;
        }
    }

    static Vector3 RandomInSphere() => Random.onUnitSphere;

    static Vector3 RandomOnCircle()
    {
        float a = Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    protected override void OnTerminateGracefully()
    {
        // Continuous types stop emitting; finite bursts finish current age window.
        if (IsContinuousEmitter(_starType) && (Duration < 0f || Age + 0.5f < Duration))
            SetDuration(Age + 0.5f);
        else
        {
            ReadyFlag = true;
            ReleaseLight();
        }
    }

    protected override void OnReleased(bool immediate)
    {
        ReleaseLight();
    }

    void ReleaseLight()
    {
        _lightLease?.Release();
        _lightLease = null;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null)
        {
            ReleaseLight();
            return;
        }

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : Mathf.Clamp01(Age / 2f);
        int frameCount = _lastFrame - _firstFrame + 1;
        // Continuous emitters color per particle life so start+end hues coexist (stock DiaBill).
        // Burst case 0 keeps a shared lifeFrac so the whole shell shifts together.
        bool perParticleColor = IsContinuousEmitter(_starType) || _starType == 19;
        bool case3 = _starType == 3;

        UpdateLight(perParticleColor);

        if (_atlas == null)
            return;

        for (int i = 0; i < MaxPoints; i++)
        {
            ref StarPoint p = ref _points[i];
            if (!p.Active)
                continue;

            float life = perParticleColor ? Mathf.Clamp01(p.LifeFrac) : t01;
            Color c = Color.Lerp(_startColor, _endColor, life);
            c.a *= Mathf.Clamp01(p.Alpha);
            if (c.a < 0.01f)
                continue;

            int frame;
            if (case3 && frameCount > 1)
            {
                // Stock: frame = clamp(15 - round((timer - Age) * 16 / life), 0, 15)
                //       = clamp(15 - round((1 - lifeFrac) * 16), 0, 15)
                int idx = 15 - Mathf.RoundToInt((1f - life) * 16f);
                if (idx < 0)
                    idx = 0;
                else if (idx > 15)
                    idx = 15;
                frame = _firstFrame + Mathf.Clamp(idx, 0, frameCount - 1);
            }
            else if (!_looping && frameCount > 1)
            {
                frame = _lastFrame;
            }
            else if (frameCount <= 1)
            {
                frame = _firstFrame;
            }
            else
            {
                frame = _firstFrame + Mathf.Clamp(Mathf.FloorToInt(t01 * frameCount), 0, frameCount - 1);
            }

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, frame)
                : _atlas;
            if (frameTex == null)
                continue;

            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(p.Position, Quaternion.identity, Vector3.one),
                Scale = Mathf.Max(0.05f, p.Size),
                Color = c,
                Texture = frameTex,
                Additive = true,
            });
        }
    }

    void UpdateLight(bool perParticleColor)
    {
        if (_lightLease == null)
            return;

        Vector3 origin = WorldMatrix.GetColumn(3);
        float alphaSum = 0f;
        float lifeSum = 0f;
        int active = 0;
        float effectRadius = Mathf.Max(_spawnRadius, _sizeCurveScale);
        for (int i = 0; i < MaxPoints; i++)
        {
            if (!_points[i].Active)
                continue;
            alphaSum += Mathf.Clamp01(_points[i].Alpha);
            lifeSum += Mathf.Clamp01(_points[i].LifeFrac);
            active++;
            float d = Vector3.Distance(_points[i].Position, origin);
            if (d > effectRadius)
                effectRadius = d;
        }

        float t01 = Duration > 0f ? Mathf.Clamp01(Age / Duration) : Mathf.Clamp01(Age / 2f);
        float life = perParticleColor && active > 0 ? lifeSum / active : t01;
        Color lightColor = Color.Lerp(_startColor, _endColor, life);
        if (active > 0)
            lightColor.a *= alphaSum / active;

        // One stable light at the effect center; range tracks the live cloud so the
        // rim stays washed without hopping between particles.
        float cloudRange = Mathf.Max(
            EffectLightPool.StarsRange(_spawnRadius) * 2f,
            effectRadius * 7f + 16f) * 10f;

        _lightLease.Update(
            origin,
            lightColor,
            EffectLightPool.StarsIntensity(lightColor),
            cloudRange,
            rangeAttenuation: false);
    }
}
