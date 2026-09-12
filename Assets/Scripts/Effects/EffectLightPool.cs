using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Fixed pool of HDRP point lights for additive nano billboards and Stars.
/// Acquire fails silently when full so Meta/Sequencer spam cannot flood the budget.
/// </summary>
public sealed class EffectLightPool
{
    public const int Capacity = 16;
    public const float BaseNanoCandela = 156250f;
    public const float BaseStarsCandela = 250000f;

    // HDRP volumetric dimmer is 0–16 (same as StatelParser playfield lights).
    const float VolumetricDimmer = 8f;

    readonly Slot[] _slots = new Slot[Capacity];
    Transform _root;
    bool _initialized;
    int _stealCursor;

    struct Slot
    {
        public GameObject Go;
        public Light Light;
        public HDAdditionalLightData Hd;
        public bool InUse;
    }

    /// <summary>Optional: parent lights under a live scene object (EffectRuntimeHost).</summary>
    public void SetParent(Transform parent)
    {
        EnsureInitialized();
        if (_root == null || parent == null || _root.parent == parent)
            return;
        _root.SetParent(parent, false);
    }

    public bool TryAcquire(out EffectLightLease lease)
    {
        EnsureInitialized();
        for (int i = 0; i < Capacity; i++)
        {
            if (_slots[i].InUse)
                continue;

            _slots[i].InUse = true;
            lease = new EffectLightLease(this, i);
            return true;
        }

        // Steal oldest slot so cast-FX spam cannot permanently starve hit lights.
        int steal = _stealCursor % Capacity;
        _stealCursor = steal + 1;
        Release(steal);
        _slots[steal].InUse = true;
        lease = new EffectLightLease(this, steal);
        return true;
    }

    internal void Update(
        int index,
        Vector3 position,
        Color rgb,
        float intensityCandela,
        float range,
        bool rangeAttenuation = true)
    {
        if (index < 0 || index >= Capacity || !_slots[index].InUse)
            return;

        Slot slot = _slots[index];
        if (slot.Go == null || slot.Light == null || slot.Hd == null)
            return;

        if (!slot.Go.activeSelf)
            slot.Go.SetActive(true);

        position.y = Mathf.Max(position.y, 1f);
        slot.Go.transform.position = position;

        Color color = new Color(
            Mathf.Max(0.05f, rgb.r),
            Mathf.Max(0.05f, rgb.g),
            Mathf.Max(0.05f, rgb.b),
            1f);
        float intensity = Mathf.Max(0f, intensityCandela);
        float lightRange = Mathf.Max(1f, range);

        // Never toggle Light.enabled — HDRP culling can drop disabled punctuals and not
        // pick them back up reliably. Drive visibility with intensity + dimmer only
        // (intensity 0 is ignored by VisibleLight culling).
        slot.Light.type = LightType.Point;
        slot.Light.lightUnit = LightUnit.Candela;
        slot.Light.color = color;
        slot.Light.intensity = intensity;
        slot.Light.range = lightRange;
        slot.Light.enabled = true;
        slot.Light.cullingMask = ~0;

        slot.Hd.EnableColorTemperature(false);
        slot.Hd.SetColor(color);
        slot.Hd.range = lightRange;
        slot.Hd.applyRangeAttenuation = rangeAttenuation;
        slot.Hd.affectDiffuse = true;
        slot.Hd.affectSpecular = true;
        slot.Hd.affectsVolumetric = intensity > 0.01f;
        slot.Hd.SetLightDimmer(intensity > 0.01f ? 1f : 0f, intensity > 0.01f ? VolumetricDimmer : 0f);
        slot.Hd.UpdateAllLightValues();
    }

    internal void Release(int index)
    {
        if (index < 0 || index >= Capacity || !_slots[index].InUse)
            return;

        Slot slot = _slots[index];
        slot.InUse = false;
        if (slot.Light != null)
        {
            // Keep enabled; zero intensity removes it from HDRP VisibleLight results.
            slot.Light.intensity = 0f;
            slot.Light.enabled = true;
        }

        if (slot.Hd != null)
        {
            slot.Hd.affectsVolumetric = false;
            slot.Hd.SetLightDimmer(0f, 0f);
            slot.Hd.UpdateAllLightValues();
        }

        _slots[index] = slot;
    }

    public static float Luminance(Color c)
        => Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);

    public static float NanoIntensity(Color color)
    {
        float lum = Mathf.Max(0.5f, Luminance(color));
        return BaseNanoCandela * lum * Mathf.Clamp01(color.a > 0f ? color.a : 1f);
    }

    public static float StarsIntensity(Color color)
    {
        float lum = Mathf.Max(0.5f, Luminance(color));
        return BaseStarsCandela * lum * Mathf.Clamp01(color.a > 0f ? color.a : 1f);
    }

    public static float NanoRange(float scale)
        => Mathf.Clamp(40f * Mathf.Max(0.05f, scale), 24f, 120f);

    public static float StarsRange(float spawnRadius)
        => Mathf.Clamp(spawnRadius * 32f, 40f, 140f);

    void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        var rootGo = new GameObject("EffectLights");
        _root = rootGo.transform;

        for (int i = 0; i < Capacity; i++)
        {
            var go = new GameObject($"EffectLight_{i}");
            go.transform.SetParent(_root, false);

            // Exact same construction path as StatelParser playfield lights.
            HDAdditionalLightData hd = go.AddHDLight(LightType.Point);
            Light light = go.GetComponent<Light>();

            hd.EnableColorTemperature(false);
            hd.affectDiffuse = true;
            hd.affectSpecular = true;
            hd.applyRangeAttenuation = true;
            hd.affectsVolumetric = true;
            hd.linkShadowLayers = false;
            hd.SetLightLayer(
                UnityEngine.Rendering.HighDefinition.RenderingLayerMask.Everything,
                UnityEngine.Rendering.HighDefinition.RenderingLayerMask.Everything);
            hd.EnableShadows(false);
            hd.SetLightDimmer(0f, 0f);

            light.type = LightType.Point;
            light.lightUnit = LightUnit.Candela;
            light.color = Color.white;
            light.intensity = 0f;
            light.range = 12f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.cullingMask = ~0;
            light.enabled = true;
#if UNITY_EDITOR
            light.lightmapBakeType = LightmapBakeType.Realtime;
#endif

            hd.range = 12f;
            hd.UpdateAllLightValues();

            _slots[i] = new Slot
            {
                Go = go,
                Light = light,
                Hd = hd,
                InUse = false,
            };
        }
    }
}

/// <summary>Opaque lease returned by <see cref="EffectLightPool.TryAcquire"/>.</summary>
public sealed class EffectLightLease
{
    readonly EffectLightPool _pool;
    readonly int _index;
    bool _released;

    internal EffectLightLease(EffectLightPool pool, int index)
    {
        _pool = pool;
        _index = index;
    }

    public void Update(
        Vector3 position,
        Color rgb,
        float intensityCandela,
        float range,
        bool rangeAttenuation = true)
    {
        if (_released || _pool == null)
            return;
        _pool.Update(_index, position, rgb, intensityCandela, range, rangeAttenuation);
    }

    public void Release()
    {
        if (_released)
            return;
        _released = true;
        _pool?.Release(_index);
    }
}
