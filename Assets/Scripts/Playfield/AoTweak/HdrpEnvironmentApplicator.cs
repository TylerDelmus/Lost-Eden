using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static AoTweakObjectParser;

/// <summary>
/// Applies AO playfield fog/sky meshes, then Lost Eden JSON overlays named like AO includes
/// (e.g. <c>Tweak_Rubi-Ka_Sun.json</c>) for primary/companion sun. Removable via Clear.
/// </summary>
public sealed class HdrpEnvironmentApplicator
{
    readonly ResourceDatabase _database;
    readonly AbiffLoader _abiffLoader;
    readonly AoTweakMeshNames _meshNames;

    bool _applied;

    Volume _sourceVolume;
    bool _sourceVolumeEnabled;
    float _sourceVolumeWeight;
    Volume _volume;
    VolumeProfile _clonedProfile;
    VolumeProfile _savedGlobalDefaultProfile;
    Fog _fog;
    bool _hadVolumeClone;

    HDAdditionalLightData _primaryHd;
    Light _primaryLight;
    bool _savedPrimary;
    float _savedPrimaryIntensity;
    float _savedPrimaryAngularDiameter;
    float _savedPrimaryFlareSize;
    Color _savedPrimaryFlareTint;
    float _savedPrimaryFlareFalloff;
    float _savedPrimaryFlareMultiplier;
    Color _savedPrimarySurfaceTint;

    GameObject _environmentRoot;
    GameObject _companionSun;
    GameObject _clonedVolumeGo;

    public HdrpEnvironmentApplicator(ResourceDatabase database, AbiffLoader abiffLoader)
    {
        _database = database;
        _abiffLoader = abiffLoader;
        _meshNames = new AoTweakMeshNames(database);
    }

    public void Apply(int playfieldId, bool loadSkyMeshes = false)
    {
        Clear();

        if (_database?.Rdb == null)
        {
            Debug.LogWarning("[AoTweak] ResourceDatabase not initialized; skipping.");
            return;
        }

        string aoBase = _database.Rdb.BaseAoPath;
        if (!AoTweakIncludeLoader.TryLoadPlayfieldFlattened(
                aoBase, playfieldId, out string flattened, out _, out List<string> includedInOrder))
            return;

        Dictionary<string, AoObject> objects = AoTweakObjectParser.Parse(flattened);
        PlayfieldEnvironmentTweak tweak = AoTweakEnvironmentBuilder.Build(objects);
        LostEdenTweakFile le = LostEdenTweakCatalog.LoadMergedForPlayfield(playfieldId, includedInOrder);

        bool companion = le.CompanionSun != null && le.CompanionSun.Enabled == true;
        bool primarySun = le.PrimarySun != null;
        bool leFog = le.Fog != null;

        // Always runtime-clone the Global Volume so playfield work never mutates the shared asset.
        CloneGlobalVolume();
        ApplyLostEdenFogOverlay(le);
        if (loadSkyMeshes)
        {
            ApplyLostEdenSkyObjectOverrides(tweak, le);
            SpawnSkyMeshes(tweak, le);
        }
        ApplyLostEdenSunOverlays(le);

        _applied = true;
        Debug.Log(
            $"[AoTweak] Applied playfield {playfieldId}: " +
            $"skyMeshes={(loadSkyMeshes ? tweak.SkyMeshes.Count : 0)}" +
            $"{(loadSkyMeshes ? "" : " (skipped)")}, leFog={leFog}, " +
            $"volumeClone={_hadVolumeClone}, " +
            $"primarySun={primarySun}, companionSun={companion}");
    }

    public void Clear()
    {
        if (_environmentRoot != null)
        {
            UnityEngine.Object.Destroy(_environmentRoot);
            _environmentRoot = null;
        }

        if (_companionSun != null)
        {
            UnityEngine.Object.Destroy(_companionSun);
            _companionSun = null;
        }

        if (_applied)
        {
            if (_hadVolumeClone)
                DestroyVolumeClone();
            RestorePrimarySun();
        }

        // Always drop clone refs even if Apply failed mid-way before _applied.
        if (_clonedVolumeGo != null || _clonedProfile != null)
            DestroyVolumeClone();

        _applied = false;
        _volume = null;
        _fog = null;
        _hadVolumeClone = false;
        _primaryHd = null;
        _primaryLight = null;
        _savedPrimary = false;
        LostEdenTweakCatalog.ClearCache();
    }

    void ApplyLostEdenSunOverlays(LostEdenTweakFile le)
    {
        if (le == null)
            return;

        Light primary = FindPrimarySunLight();
        if (primary == null)
            return;

        var hd = primary.GetComponent<HDAdditionalLightData>();
        if (hd == null)
            return;

        if (le.PrimarySun != null)
        {
            SnapshotPrimarySun(primary, hd);
            ApplyPrimarySun(primary, hd, le.PrimarySun);
        }

        if (le.CompanionSun != null && le.CompanionSun.Enabled == true)
            SpawnCompanionSun(primary, le.CompanionSun);
    }

    void SnapshotPrimarySun(Light light, HDAdditionalLightData hd)
    {
        _primaryLight = light;
        _primaryHd = hd;
        _savedPrimary = true;
        _savedPrimaryIntensity = hd.intensity;
        _savedPrimaryAngularDiameter = hd.angularDiameter;
        _savedPrimaryFlareSize = hd.flareSize;
        _savedPrimaryFlareTint = hd.flareTint;
        _savedPrimaryFlareFalloff = hd.flareFalloff;
        _savedPrimaryFlareMultiplier = hd.flareMultiplier;
        _savedPrimarySurfaceTint = hd.surfaceTint;
    }

    void ApplyPrimarySun(Light light, HDAdditionalLightData hd, LostEdenPrimarySunTweak cfg)
    {
        if (cfg.Intensity.HasValue)
        {
            float intensity = Mathf.Max(0f, cfg.Intensity.Value);
            hd.intensity = intensity;
            light.intensity = intensity;
        }

        if (cfg.AngularDiameter.HasValue)
            hd.angularDiameter = cfg.AngularDiameter.Value;
        if (cfg.FlareSize.HasValue)
            hd.flareSize = cfg.FlareSize.Value;
        if (LostEdenTweakColorUtil.TryReadRgb(cfg.FlareTint, out Color flareTint))
            hd.flareTint = flareTint;
        if (cfg.FlareFalloff.HasValue)
            hd.flareFalloff = cfg.FlareFalloff.Value;
        if (cfg.FlareMultiplier.HasValue)
            hd.flareMultiplier = cfg.FlareMultiplier.Value;
        if (LostEdenTweakColorUtil.TryReadRgb(cfg.SurfaceTint, out Color surfaceTint))
            hd.surfaceTint = surfaceTint;
    }

    void RestorePrimarySun()
    {
        if (!_savedPrimary || _primaryHd == null)
            return;

        _primaryHd.intensity = _savedPrimaryIntensity;
        if (_primaryLight != null)
            _primaryLight.intensity = _savedPrimaryIntensity;
        _primaryHd.angularDiameter = _savedPrimaryAngularDiameter;
        _primaryHd.flareSize = _savedPrimaryFlareSize;
        _primaryHd.flareTint = _savedPrimaryFlareTint;
        _primaryHd.flareFalloff = _savedPrimaryFlareFalloff;
        _primaryHd.flareMultiplier = _savedPrimaryFlareMultiplier;
        _primaryHd.surfaceTint = _savedPrimarySurfaceTint;
    }

    void SpawnCompanionSun(Light primary, LostEdenCompanionSunTweak cfg)
    {
        float yaw = cfg.YawOffsetDeg ?? 6f;
        float pitch = cfg.PitchOffsetDeg ?? 0f;
        float angular = cfg.AngularDiameter ?? 0.9f;
        // Emission shades the disc from light intensity; 0 makes the body invisible.
        float intensity = Mathf.Max(0f, cfg.Intensity ?? 100000f);
        Color color = LostEdenTweakColorUtil.TryReadRgb(cfg.Color, out Color c)
            ? c
            : new Color(100f / 255f, 155f / 255f, 1f);
        Color flareTint = LostEdenTweakColorUtil.TryReadRgb(cfg.FlareTint, out Color ft) ? ft : color;
        Color surfaceTint = LostEdenTweakColorUtil.TryReadRgb(cfg.SurfaceTint, out Color st) ? st : color;
        float flareSize = cfg.FlareSize ?? 2.5f;
        float flareFalloff = cfg.FlareFalloff ?? 4f;
        float flareMultiplier = cfg.FlareMultiplier ?? 1.25f;

        _companionSun = new GameObject("AoTweakCompanionSun");
        _companionSun.transform.SetPositionAndRotation(
            primary.transform.position,
            // Pitch: negative = higher in the sky for a typical downward-aimed sun.
            primary.transform.rotation * Quaternion.Euler(pitch, yaw, 0f));

        var light = _companionSun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.bounceIntensity = 0f;

        var hd = _companionSun.AddComponent<HDAdditionalLightData>();
        hd.celestialBodyShadingSource = HDAdditionalLightData.CelestialBodyShadingSource.Emission;
        hd.sunLightOverride = null;
        hd.interactsWithSky = true;
        hd.angularDiameter = angular;
        hd.intensity = intensity;
        // Disc/flare only — do not light the ground or take a cascade shadow slot.
        hd.affectDiffuse = false;
        hd.affectSpecular = false;
        hd.volumetricDimmer = 0f;
        hd.shadowDimmer = 0f;
        hd.EnableShadows(false);
        light.shadows = LightShadows.None;
        hd.SetColor(color);
        hd.surfaceTint = surfaceTint;
        hd.flareTint = flareTint;
        hd.flareSize = flareSize;
        hd.flareFalloff = flareFalloff;
        hd.flareMultiplier = flareMultiplier;
    }

    /// <summary>
    /// Instantiates a per-playfield <c>AoTweakGlobalVolume</c> with a cloned profile and disables
    /// the scene Global Volume. Always runs on Apply so the shared asset is never mutated.
    /// Destroyed on Clear; next Apply creates a new one.
    /// </summary>
    void CloneGlobalVolume()
    {
        DestroyVolumeClone();

        _sourceVolume = FindSceneGlobalVolume();
        _hadVolumeClone = false;
        _fog = null;
        _volume = null;
        if (_sourceVolume == null)
        {
            Debug.LogWarning("[AoTweak] No Global Volume found; skipping volume clone.");
            return;
        }

        VolumeProfile sourceProfile = _sourceVolume.sharedProfile;
        if (sourceProfile == null || sourceProfile.components == null)
        {
            Debug.LogWarning("[AoTweak] Global Volume has no profile; skipping volume clone.");
            return;
        }

        _clonedProfile = CloneVolumeProfile(sourceProfile);
        if (_clonedProfile == null)
            return;

        _clonedProfile.TryGet(out _fog);

        _clonedVolumeGo = UnityEngine.Object.Instantiate(_sourceVolume.gameObject);
        _clonedVolumeGo.name = "AoTweakGlobalVolume";
        _clonedVolumeGo.transform.SetParent(null, true);
        _volume = _clonedVolumeGo.GetComponent<Volume>();
        if (_volume == null)
        {
            UnityEngine.Object.Destroy(_clonedVolumeGo);
            UnityEngine.Object.Destroy(_clonedProfile);
            _clonedVolumeGo = null;
            _clonedProfile = null;
            _fog = null;
            return;
        }

        _volume.enabled = true;
        _volume.sharedProfile = _clonedProfile;
        _volume.priority = _sourceVolume.priority + 1f;
        _volume.weight = 1f;

        _sourceVolumeEnabled = _sourceVolume.enabled;
        _sourceVolumeWeight = _sourceVolume.weight;
        _sourceVolume.enabled = false;

        _savedGlobalDefaultProfile = VolumeManager.instance.globalDefaultProfile;
        VolumeManager.instance.SetGlobalDefaultProfile(_clonedProfile);

        _hadVolumeClone = true;
    }

    void DestroyVolumeClone()
    {
        if (_savedGlobalDefaultProfile != null || VolumeManager.instance.globalDefaultProfile == _clonedProfile)
        {
            VolumeManager.instance.SetGlobalDefaultProfile(_savedGlobalDefaultProfile);
            _savedGlobalDefaultProfile = null;
        }

        if (_clonedVolumeGo != null)
        {
            UnityEngine.Object.Destroy(_clonedVolumeGo);
            _clonedVolumeGo = null;
        }

        if (_clonedProfile != null)
        {
            UnityEngine.Object.Destroy(_clonedProfile);
            _clonedProfile = null;
        }

        if (_sourceVolume != null)
        {
            _sourceVolume.enabled = _sourceVolumeEnabled;
            _sourceVolume.weight = _sourceVolumeWeight;
            _sourceVolume = null;
        }

        _volume = null;
        _fog = null;
        _hadVolumeClone = false;
    }

    static VolumeProfile CloneVolumeProfile(VolumeProfile source)
    {
        var clone = ScriptableObject.CreateInstance<VolumeProfile>();
        clone.name = string.IsNullOrEmpty(source.name) ? "AoTweakVolumeProfile" : source.name + " (AoTweak)";
        for (int i = 0; i < source.components.Count; i++)
        {
            VolumeComponent component = source.components[i];
            if (component == null)
                continue;
            VolumeComponent copy = UnityEngine.Object.Instantiate(component);
            clone.components.Add(copy);
        }

        return clone;
    }

    void ApplyLostEdenFogOverlay(LostEdenTweakFile le)
    {
        if (!_hadVolumeClone || _fog == null || le?.Fog == null)
            return;

        LostEdenFogTweak fog = le.Fog;

        // Only override fields present in the LE JSON — leave Volume defaults for the rest.
        if (fog.AttenuationDistance.HasValue
            || fog.BaseHeight.HasValue
            || fog.MaximumHeight.HasValue
            || fog.MaxFogDistance.HasValue
            || fog.EnableVolumetricFog.HasValue
            || fog.GiDimmer.HasValue
            || fog.VolumetricLighting.HasValue
            || !string.IsNullOrWhiteSpace(fog.DenoisingMode)
            || !string.IsNullOrWhiteSpace(fog.Tier)
            || !string.IsNullOrWhiteSpace(fog.Tint)
            || fog.TintRgb != null)
        {
            _fog.active = true;
            _fog.enabled.overrideState = true;
            _fog.enabled.value = true;
        }

        if (fog.AttenuationDistance.HasValue)
        {
            _fog.meanFreePath.overrideState = true;
            _fog.meanFreePath.value = Mathf.Max(1f, fog.AttenuationDistance.Value);
        }

        if (fog.BaseHeight.HasValue)
        {
            _fog.baseHeight.overrideState = true;
            _fog.baseHeight.value = fog.BaseHeight.Value;
        }

        // Null / unspecified → disable Volume override for these fog controls.
        ApplyOrDisable(_fog.maximumHeight, fog.MaximumHeight);
        ApplyOrDisable(_fog.maxFogDistance, fog.MaxFogDistance, v => Mathf.Max(1f, v));
        ApplyOrDisable(_fog.globalLightProbeDimmer, fog.GiDimmer, v => Mathf.Clamp01(v));
        ApplyOrDisable(_fog.volumetricLightingDensityCutoff, fog.VolumetricLighting);

        if (TryParseFogDenoisingMode(fog.DenoisingMode, out FogDenoisingMode denoising))
        {
            _fog.denoisingMode.overrideState = true;
            _fog.denoisingMode.value = denoising;
        }
        else
        {
            _fog.denoisingMode.overrideState = false;
        }

        if (TryParseFogTier(fog.Tier, out int tier))
        {
            _fog.quality.overrideState = true;
            _fog.quality.value = tier;
        }
        else
        {
            _fog.quality.overrideState = false;
        }

        if (LostEdenTweakColorUtil.TryReadTint(fog, out Color tint))
        {
            _fog.tint.overrideState = true;
            _fog.tint.value = tint;
        }

        if (fog.EnableVolumetricFog.HasValue)
        {
            _fog.enableVolumetricFog.overrideState = true;
            _fog.enableVolumetricFog.value = fog.EnableVolumetricFog.Value;
        }

        // Cloned profile is also the HDRP global default — refresh default state after edits.
        VolumeManager.instance.OnVolumeProfileChanged(_clonedProfile);

        Debug.Log(
            $"[AoTweak] LE fog: mfp={_fog.meanFreePath.value:0.#}, " +
            $"baseH={_fog.baseHeight.value:0.#}, tint={_fog.tint.value}, " +
            $"maxH={_fog.maximumHeight.overrideState}, maxDist={_fog.maxFogDistance.overrideState}, " +
            $"gi={_fog.globalLightProbeDimmer.overrideState}, denoise={_fog.denoisingMode.overrideState}, " +
            $"tier={_fog.quality.overrideState}, volLight={_fog.volumetricLightingDensityCutoff.overrideState}");
    }

    static void ApplyOrDisable(FloatParameter param, float? value, Func<float, float> sanitize = null)
    {
        if (value.HasValue)
        {
            param.overrideState = true;
            param.value = sanitize != null ? sanitize(value.Value) : value.Value;
        }
        else
        {
            param.overrideState = false;
        }
    }

    static bool TryParseFogDenoisingMode(string text, out FogDenoisingMode mode)
    {
        mode = FogDenoisingMode.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (int.TryParse(text, out int asInt) && Enum.IsDefined(typeof(FogDenoisingMode), asInt))
        {
            mode = (FogDenoisingMode)asInt;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out mode);
    }

    static bool TryParseFogTier(string text, out int tier)
    {
        tier = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (int.TryParse(text, out int asInt)
            && asInt >= 0
            && asInt < ScalableSettingLevelParameter.LevelCount)
        {
            tier = asInt;
            return true;
        }

        if (Enum.TryParse(text, ignoreCase: true, out ScalableSettingLevelParameter.Level level))
        {
            tier = (int)level;
            return true;
        }

        return false;
    }

    void ApplyLostEdenSkyObjectOverrides(PlayfieldEnvironmentTweak tweak, LostEdenTweakFile le)
    {
        if (tweak?.SkyMeshes == null || le?.Objects == null || le.Objects.Count == 0)
            return;

        int applied = 0;
        for (int i = 0; i < tweak.SkyMeshes.Count; i++)
        {
            AoSkyMeshPlacement placement = tweak.SkyMeshes[i];
            if (!le.Objects.TryGetValue(placement.ObjectName, out LostEdenSkyObjectTweak ov) || ov == null)
                continue;

            if (ov.Enabled.HasValue)
                placement.Enabled = ov.Enabled.Value;
            if (ov.Scale.HasValue)
                placement.Scale = Mathf.Max(0.01f, ov.Scale.Value);
            if (ov.Intensity.HasValue)
                placement.Intensity = Mathf.Max(0f, ov.Intensity.Value);
            if (LostEdenTweakColorUtil.TryReadVector3(ov.Position, out Vector3 pos))
                placement.PositionOffset = pos;

            tweak.SkyMeshes[i] = placement;
            applied++;
        }

        if (applied > 0)
            Debug.Log($"[AoTweak] Applied LE object overrides to {applied} sky mesh(es).");
    }

    void SpawnSkyMeshes(PlayfieldEnvironmentTweak tweak, LostEdenTweakFile le)
    {
        if (tweak.SkyMeshes == null || tweak.SkyMeshes.Count == 0 || _abiffLoader == null)
            return;

        HashSet<string> skip = BuildSkipSkySet(le);

        _environmentRoot = new GameObject("AoTweakEnvironment");
        _environmentRoot.AddComponent<AoTweakEnvironmentRoot>();

        int spawned = 0;
        int skipped = 0;
        for (int i = 0; i < tweak.SkyMeshes.Count; i++)
        {
            AoSkyMeshPlacement placement = tweak.SkyMeshes[i];
            if (!placement.Enabled || (skip != null && skip.Contains(placement.ObjectName)))
            {
                skipped++;
                continue;
            }

            if (!_meshNames.TryResolve(placement.MeshName, out int meshId))
            {
                Debug.LogWarning($"[AoTweak] Mesh not found in InfoObject: '{placement.MeshName}' ({placement.ObjectName})");
                continue;
            }

            if (!_abiffLoader.TryCreateSkyVisual(meshId, _environmentRoot.transform, placement.Intensity, out GameObject visual))
                continue;

            visual.name = $"Sky_{placement.ObjectName}_{meshId}";
            visual.transform.localRotation = placement.LocalRotation;
            visual.transform.localScale = Vector3.one * placement.Scale;

            var follower = visual.AddComponent<AoSkyMeshFollower>();
            follower.Init(placement.PositionOffset);

            int transparentFx = LayerMask.NameToLayer("TransparentFX");
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                renderers[r].shadowCastingMode = ShadowCastingMode.Off;
                renderers[r].receiveShadows = false;
                // Keep additive sky out of lighting/probe interactions; exposure still meters color.
                if (transparentFx >= 0)
                    renderers[r].gameObject.layer = transparentFx;
            }

            spawned++;
        }

        if (skipped > 0)
            Debug.Log($"[AoTweak] Skipped {skipped} sky mesh(es) (disabled or skipSkyMeshes).");

        if (spawned == 0)
        {
            UnityEngine.Object.Destroy(_environmentRoot);
            _environmentRoot = null;
        }
    }

    static HashSet<string> BuildSkipSkySet(LostEdenTweakFile le)
    {
        if (le?.SkipSkyMeshes == null || le.SkipSkyMeshes.Length == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < le.SkipSkyMeshes.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(le.SkipSkyMeshes[i]))
                set.Add(le.SkipSkyMeshes[i].Trim());
        }

        return set.Count > 0 ? set : null;
    }

    static Light FindPrimarySunLight()
    {
        GameObject sunGo = GameObject.Find("Sun");
        if (sunGo != null)
        {
            Light light = sunGo.GetComponent<Light>();
            if (light != null && light.type == LightType.Directional && light.intensity > 0f)
                return light;
        }

        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        Light best = null;
        float bestIntensity = 0f;
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || light.type != LightType.Directional || light.intensity <= 0f)
                continue;
            if (light.intensity > bestIntensity)
            {
                bestIntensity = light.intensity;
                best = light;
            }
        }

        return best;
    }

    static Volume FindSceneGlobalVolume()
    {
        GameObject go = GameObject.Find("Global Volume");
        if (go != null)
        {
            Volume v = go.GetComponent<Volume>();
            if (v != null)
                return v;
        }

        Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume == null || !volume.isGlobal)
                continue;
            // Never treat the per-playfield clone as the scene source.
            if (volume.gameObject.name == "AoTweakGlobalVolume")
                continue;
            return volume;
        }

        return null;
    }
}
