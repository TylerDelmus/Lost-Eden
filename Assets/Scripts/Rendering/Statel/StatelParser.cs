using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AODB.Common.DbClasses;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed class StatelParser
{
    const int InstantiateBatchSize = 64;

    /// <summary>
    /// When true, each placement root gets a <see cref="StatelDebugInfo"/> component.
    /// Leave off for normal play; <see cref="PlayfieldTest_DEV"/> enables it.
    /// </summary>
    public static bool AttachDebugInfo;
    readonly ResourceDatabase _database;
    readonly RenderConfig _renderConfig;
    readonly AbiffMaterialFactory _materials;

    readonly Dictionary<MeshVariantKey, Mesh[]> _unityMeshCache = new Dictionary<MeshVariantKey, Mesh[]>();

    Dictionary<ResourceTypeId, Dictionary<int, string>> _rdbNames;

    public StatelParser(ResourceDatabase database, RenderConfig renderConfig, AbiffMaterialFactory materials)
    {
        _database = database;
        _renderConfig = renderConfig;
        _materials = materials ?? new AbiffMaterialFactory(database);
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        if (_database?.Rdb == null)
        {
            Debug.LogError("StatelParser: ResourceDatabase is not initialized.");
            yield break;
        }

        if (_renderConfig == null)
            Debug.LogWarning("StatelParser: RenderConfig is missing; continuing with defaults.");

        List<StatelPlacement> placements = ParsePlacements(playfieldId);
        if (placements.Count == 0)
            yield break;

        TryLoadNames();
        RemoveSkippedPlacements(placements);
        if (placements.Count == 0)
            yield break;

        List<StatelPlacement> lightPlacements = ExtractLightPlacements(placements);

        var root = new GameObject($"Statels_{playfieldId}");
        root.transform.SetParent(parent, false);

        // Lights must not live under the static mesh root — static flags / batching can
        // prevent punctual lights from contributing in HDRP.
        Transform lightRoot = root.transform;
        if (lightPlacements.Count > 0)
        {
            var lightsGo = new GameObject($"StatelLights_{playfieldId}");
            lightsGo.transform.SetParent(parent, false);
            lightRoot = lightsGo.transform;
        }

        int created = 0;
        for (int i = 0; i < lightPlacements.Count; i++)
        {
            InstantiateLight(lightRoot, lightPlacements[i], i);
            created++;
            if (created % InstantiateBatchSize == 0)
                yield return null;
        }

        if (lightPlacements.Count > 0)
            Debug.Log($"StatelParser: Spawned {lightPlacements.Count} lights from Lights.json for playfield {playfieldId}.");

        if (placements.Count == 0)
        {
            root.isStatic = true;
            yield break;
        }

        Dictionary<int, MeshSource> meshSources = SnapshotMeshes(placements);
        CollectAndCreateMaterials(placements, meshSources);
        yield return null;

        List<MeshVariantKey> neededKeys = CollectMeshKeys(placements);
        Dictionary<MeshVariantKey, AbiffMeshData[]> built = null;
        yield return RunParallel(() =>
        {
            built = BuildMeshDataParallel(neededKeys, meshSources);
        });

        CreateUnityMeshes(built);
        yield return null;

        for (int i = 0; i < placements.Count; i++)
        {
            StatelPlacement placement = placements[i];
            if (!meshSources.TryGetValue(placement.MeshId, out MeshSource source))
                continue;

            MeshVariantKey key = MeshVariantKey.FromPlacement(placement);
            if (!_unityMeshCache.TryGetValue(key, out Mesh[] meshes))
                continue;

            InstantiatePlacement(root.transform, placement, source, meshes, i);
            created++;

            if (created % InstantiateBatchSize == 0)
                yield return null;
        }

        root.isStatic = true;
    }

    void InstantiatePlacement(
        Transform parent,
        StatelPlacement placement,
        MeshSource source,
        Mesh[] meshes,
        int index)
    {
        string name = ResolveMeshName(placement.MeshId);
        var go = new GameObject($"{name}_{index}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = placement.Position;
        go.transform.localRotation = placement.Rotation;
        // Direct-quat arm (flags&1): stock Anim = PostScale(1,1/var,1/var) then
        // shear(s,0), with uniform FinalScale=base*var on the ref-frame.
        // Net axis scales = (FinalScale, BaseScale, BaseScale). Using (Final,1,1)
        // only matches when base≈1 (e.g. pf 4582 fences); pf 4310 brink uses
        // base=0.43 and looked stretched on Y/Z.
        // Lat/lon arm: uniform FinalScale.
        ScaleRotationInfo xform = placement.Transform;
        go.transform.localScale = xform.Sheared
            ? new Vector3(xform.FinalScale, xform.BaseScale, xform.BaseScale)
            : placement.Scale;

        if (AttachDebugInfo)
        {
            var debug = go.AddComponent<StatelDebugInfo>();
            debug.Set(
                index,
                placement.MeshId,
                name,
                placement.Position,
                go.transform.localScale,
                placement.Flag,
                placement.Flags2,
                placement.TextureOverrides,
                placement.Transform);
        }

        Dictionary<int, int> overrides = BuildOverrideMap(placement.TextureOverrides);
        bool hasUvAnim = false;

        for (int s = 0; s < source.Submeshes.Length; s++)
        {
            AbiffSubmeshSource sub = source.Submeshes[s];
            Mesh mesh = meshes[s];
            if (mesh == null)
                continue;

            var subGo = new GameObject($"Sub_{s}");
            subGo.transform.SetParent(go.transform, false);
            subGo.transform.localPosition = sub.BasePosition;
            subGo.transform.localRotation = sub.BaseRotation;
            subGo.transform.localScale = Vector3.one;


            var filter = subGo.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = subGo.AddComponent<MeshRenderer>();
            AbiffMaterialDesc material = sub.Material;
            if (overrides != null && overrides.TryGetValue(s, out int overridden) && overridden > 0)
                material = material.WithDiffuseTexture(overridden);
            renderer.sharedMaterial = _materials.Get(material);

            if (sub.UvKeys != null && sub.UvKeys.Length >= 2)
            {
                hasUvAnim = true;
                var animator = subGo.AddComponent<AbiffUvAnimator>();
                animator.Init(sub.UvKeys, sub.UvLoop, sub.UvDuration);
            }
        }

        // UV-animated materials need per-instance property blocks; static batching would freeze them.
        if (!hasUvAnim)
            go.isStatic = true;
    }

    List<StatelPlacement> ExtractLightPlacements(List<StatelPlacement> placements)
    {
        var lights = new List<StatelPlacement>();
        for (int i = placements.Count - 1; i >= 0; i--)
        {
            if (!TryGetRawMeshName(placements[i].MeshId, out string rawName))
                continue;

            if (!StatelLightsCatalog.TryGet(rawName, out StatelLightTweak settings) || settings == null)
                continue;

            placements[i].LightSettings = settings;
            lights.Add(placements[i]);
            placements.RemoveAt(i);
        }

        lights.Reverse();
        return lights;
    }

    void InstantiateLight(Transform parent, StatelPlacement placement, int index)
    {
        StatelLightTweak cfg = placement.LightSettings;
        if (cfg == null)
            return;

        string name = ResolveMeshName(placement.MeshId);
        var go = new GameObject($"Light_{name}_{index}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = placement.Position;
        go.transform.localRotation = placement.Rotation;
        go.transform.localScale = Vector3.one;

        LightType lightType = ParseLightType(cfg.Type);
        Color color = LostEdenTweakColorUtil.TryReadRgb(cfg.Color, out Color c) ? c : Color.white;
        float intensity = Mathf.Max(0f, cfg.Intensity ?? 5000f);
        float range = Mathf.Max(0.1f, cfg.Range ?? 15f);
        bool shadows = cfg.Shadows ?? false;
        // HDRP volumetric dimmer is 0–16 (not 0–1).
        float volumetric = Mathf.Clamp(cfg.VolumetricDimmer ?? 1f, 0f, 16f);
        float bounce = Mathf.Max(0f, cfg.BounceIntensity ?? 1f);

        HDAdditionalLightData hd = go.AddHDLight(lightType);
        Light light = go.GetComponent<Light>();

        // AddHDLight enables color temperature by default; that fights authored RGB colors.
        hd.EnableColorTemperature(false);
        hd.affectDiffuse = true;
        hd.affectSpecular = true;
        hd.applyRangeAttenuation = true;
        hd.affectsVolumetric = volumetric > 0f;

        // High Fidelity enables HDRP light layers — ensure we hit default mesh layers.
        hd.linkShadowLayers = false;
        hd.SetLightLayer(
            UnityEngine.Rendering.HighDefinition.RenderingLayerMask.Everything,
            UnityEngine.Rendering.HighDefinition.RenderingLayerMask.Everything);

        light.color = color;
        light.range = range;
        light.bounceIntensity = bounce;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.enabled = true;
#if UNITY_EDITOR
        light.lightmapBakeType = LightmapBakeType.Realtime;
#endif

        if (lightType == LightType.Spot)
        {
            float spotAngle = Mathf.Clamp(cfg.SpotAngle ?? 60f, 1f, 179f);
            float innerPercent = Mathf.Clamp(cfg.InnerSpotPercent ?? 50f, 0f, 100f);
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle * (innerPercent / 100f);
        }

        // Intensity is authored in Candela for punctual lights (native HDRP unit).
        // Avoid Lumen conversion — it has been unreliable across HDRP lightUnit changes.
        if (lightType == LightType.Point || lightType == LightType.Spot)
        {
            light.lightUnit = LightUnit.Candela;
            light.intensity = intensity;
        }
        else
        {
            light.lightUnit = LightUnit.Lux;
            light.intensity = intensity;
        }

        hd.SetColor(color);
        hd.range = range;
        hd.SetLightDimmer(1f, volumetric);
        hd.EnableShadows(shadows);
        hd.UpdateAllLightValues();
    }

    static LightType ParseLightType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return LightType.Point;

        if (type.Equals("Spot", StringComparison.OrdinalIgnoreCase))
            return LightType.Spot;

        if (type.Equals("Directional", StringComparison.OrdinalIgnoreCase))
            return LightType.Directional;

        return LightType.Point;
    }

    static List<MeshVariantKey> CollectMeshKeys(List<StatelPlacement> placements)
    {
        var seen = new HashSet<MeshVariantKey>();
        var keys = new List<MeshVariantKey>();
        for (int i = 0; i < placements.Count; i++)
        {
            MeshVariantKey key = MeshVariantKey.FromPlacement(placements[i]);
            if (seen.Add(key))
                keys.Add(key);
        }

        return keys;
    }

    static Dictionary<MeshVariantKey, AbiffMeshData[]> BuildMeshDataParallel(
        List<MeshVariantKey> keyList,
        Dictionary<int, MeshSource> meshSources)
    {
        var result = new ConcurrentDictionary<MeshVariantKey, AbiffMeshData[]>();

        Parallel.For(0, keyList.Count, i =>
        {
            MeshVariantKey key = keyList[i];
            if (!meshSources.TryGetValue(key.MeshId, out MeshSource source))
                return;

            float shear = BitConverter.Int32BitsToSingle(key.ShearBits);
            bool applyShear = key.ApplyShear;
            var meshes = new AbiffMeshData[source.Submeshes.Length];
            for (int s = 0; s < source.Submeshes.Length; s++)
                meshes[s] = StatelMeshBuilder.Build(source.Submeshes[s], applyShear, shear);

            result[key] = meshes;
        });

        return new Dictionary<MeshVariantKey, AbiffMeshData[]>(result);
    }

    void CreateUnityMeshes(Dictionary<MeshVariantKey, AbiffMeshData[]> built)
    {
        if (built == null)
            return;

        foreach (KeyValuePair<MeshVariantKey, AbiffMeshData[]> kvp in built)
        {
            var meshes = new Mesh[kvp.Value.Length];
            for (int i = 0; i < kvp.Value.Length; i++)
            {
                meshes[i] = AbiffMeshFactory.CreateUnityMesh(
                    kvp.Value[i],
                    $"Statel_{kvp.Key.MeshId}_{kvp.Key.ShearBits}_{i}");
            }

            _unityMeshCache[kvp.Key] = meshes;
        }
    }

    Dictionary<int, MeshSource> SnapshotMeshes(List<StatelPlacement> placements)
    {
        var uniqueIds = new HashSet<int>();
        for (int i = 0; i < placements.Count; i++)
            uniqueIds.Add(placements[i].MeshId);

        var sources = new Dictionary<int, MeshSource>(uniqueIds.Count);
        foreach (int meshId in uniqueIds)
        {
            RDBMesh rdbMesh = _database.Get<RDBMesh>(meshId);
            if (rdbMesh?.SubMeshes == null || rdbMesh.SubMeshes.Count == 0)
            {
                Debug.LogWarning($"StatelParser: Missing RDBMesh {meshId}.");
                continue;
            }

            sources[meshId] = new MeshSource
            {
                MeshId = meshId,
                Submeshes = AbiffMeshSnapshot.FromRdbMesh(rdbMesh)
            };
        }

        return sources;
    }

    void CollectAndCreateMaterials(List<StatelPlacement> placements, Dictionary<int, MeshSource> meshSources)
    {
        var unique = new HashSet<AbiffMaterialDesc>();
        foreach (KeyValuePair<int, MeshSource> kvp in meshSources)
        {
            for (int s = 0; s < kvp.Value.Submeshes.Length; s++)
                unique.Add(kvp.Value.Submeshes[s].Material);
        }

        for (int i = 0; i < placements.Count; i++)
        {
            if (!meshSources.TryGetValue(placements[i].MeshId, out MeshSource source))
                continue;

            Dictionary<int, int> overrides = BuildOverrideMap(placements[i].TextureOverrides);
            if (overrides == null)
                continue;

            foreach (KeyValuePair<int, int> ov in overrides)
            {
                if (ov.Key < 0 || ov.Key >= source.Submeshes.Length || ov.Value <= 0)
                    continue;

                unique.Add(source.Submeshes[ov.Key].Material.WithDiffuseTexture(ov.Value));
            }
        }

        foreach (AbiffMaterialDesc desc in unique)
            _materials.Get(desc);
    }

    static Dictionary<int, int> BuildOverrideMap(int[] textureOverrides)
    {
        if (textureOverrides == null || textureOverrides.Length == 0)
            return null;

        var map = new Dictionary<int, int>();
        uint mask = unchecked((uint)textureOverrides[0]);
        int overrideIndex = 1;
        for (int bit = 0; bit < 32; bit++)
        {
            uint flag = 1u << bit;
            if ((mask & flag) == 0)
                continue;

            if (overrideIndex >= textureOverrides.Length)
                break;

            map[bit] = textureOverrides[overrideIndex++];
        }

        return map.Count > 0 ? map : null;
    }

    void TryLoadNames()
    {
        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            _rdbNames = info?.Types;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"StatelParser: Failed to load InfoObject names ({ex.Message}).");
            _rdbNames = null;
        }
    }

    void RemoveSkippedPlacements(List<StatelPlacement> placements)
    {
        for (int i = placements.Count - 1; i >= 0; i--)
        {
            if (ShouldSkipMesh(placements[i].MeshId))
                placements.RemoveAt(i);
        }
    }

    bool ShouldSkipMesh(int meshId)
    {
        if (!TryGetRawMeshName(meshId, out string name))
            return false;

        return SkippedStatelsCatalog.ShouldSkip(name);
    }

    string ResolveMeshName(int meshId)
    {
        if (TryGetRawMeshName(meshId, out string name))
            return SanitizeName(name);

        return meshId.ToString();
    }

    bool TryGetRawMeshName(int meshId, out string name)
    {
        name = null;
        if (_rdbNames == null ||
            !_rdbNames.TryGetValue(ResourceTypeId.RdbMesh, out Dictionary<int, string> names) ||
            names == null ||
            !names.TryGetValue(meshId, out name) ||
            string.IsNullOrEmpty(name))
        {
            return false;
        }

        name = name.Trim('\0');
        return !string.IsNullOrEmpty(name);
    }

    static string SanitizeName(string name)
    {
        char[] chars = name.Trim('\0').ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                chars[i] = '_';
        }

        return new string(chars);
    }

    List<StatelPlacement> ParsePlacements(int playfieldId)
    {
        var placements = new List<StatelPlacement>();
        string path = Path.Combine(_database.Rdb.BaseAoPath, "cd_image", "data", "statels", $"{playfieldId}.pf");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"StatelParser: Statels file not found: {path}");
            return placements;
        }

        byte[] bytes = File.ReadAllBytes(path);
        using (var ms = new MemoryStream(bytes))
        using (var reader = new BinaryReader(ms))
        {
            int version = reader.ReadInt32();

            var offsets = new List<int>();
            int statelOffset = reader.ReadInt32();
            while (offsets.Count == 0 || offsets[offsets.Count - 1] < statelOffset)
            {
                offsets.Add(statelOffset);
                statelOffset = reader.ReadInt32();
            }

            reader.BaseStream.Position -= 4;

            ParseFirstBlock(reader, placements);

            for (int i = 1; i < offsets.Count; i++)
            {
                uint len = (uint)((i == offsets.Count - 1) ? -1 : (offsets[i + 1] - offsets[i]));
                if ((int)len < 0)
                    continue;

                ParseSet(reader, offsets[i], placements);
            }
        }

        return placements;
    }

    void ParseFirstBlock(BinaryReader reader, List<StatelPlacement> placements)
    {
        reader.ReadInt32(); // BlockLength
        ParseBuilding(reader, placements);
        ParseShortBuilding(reader);
    }

    void ParseSet(BinaryReader reader, int pos, List<StatelPlacement> placements)
    {
        reader.BaseStream.Position = pos;

        int extraShorts = reader.ReadUInt16();
        for (int i = 0; i < extraShorts; i++)
            reader.ReadUInt16();

        for (int i = 0; i < 5; i++)
            ParseBuilding(reader, placements);
    }

    static void ParseShortBuilding(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            int byte6 = reader.ReadByte();
            if (byte6 > 0)
            {
                int mask = reader.ReadInt32();
                int texCount = byte6 + ((mask & 0x7F0000) == 0x7F0000 ? 1 : 0);
                reader.BaseStream.Position += texCount * 4;
            }
        }
    }

    void ParseBuilding(BinaryReader reader, List<StatelPlacement> placements)
    {
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            float px = reader.ReadSingle();
            float py = reader.ReadSingle();
            float pz = reader.ReadSingle();
            int flags = reader.ReadInt32();
            uint meshId = reader.ReadUInt32();
            byte flags2 = reader.ReadByte();

            ScaleRotationInfo transform = CalculateScaleAndRotation(flags, flags2);

            byte byte6 = reader.ReadByte();
            int[] textureOverrides = Array.Empty<int>();
            if (byte6 > 0)
            {
                int mask = reader.ReadInt32();
                int texCount = byte6 + ((mask & 0x7F0000) == 0x7F0000 ? 1 : 0);
                textureOverrides = new int[1 + texCount];
                textureOverrides[0] = mask;
                for (int o = 1; o < textureOverrides.Length; o++)
                    textureOverrides[o] = reader.ReadInt32();
            }

            // Large mesh IDs share the same trailer but are not Statel placements.
            if (meshId > 300000)
                continue;

            placements.Add(new StatelPlacement
            {
                MeshId = (int)meshId,
                Position = new Vector3(px, py, pz),
                Rotation = transform.Rotation,
                Scale = new Vector3(transform.FinalScale, transform.FinalScale, transform.FinalScale),
                ShearFactor = transform.ShearFactor,
                Flag = flags,
                Flags2 = flags2,
                Transform = transform,
                TextureOverrides = textureOverrides
            });
        }
    }

    public static ScaleRotationInfo CalculateScaleAndRotation(int flags, byte flags2)
    {
        var info = new ScaleRotationInfo
        {
            Sheared = (flags & 1) != 0,
            RotationPacked = (uint)flags >> 7,
            BaseScale = flags2 / 100f + 0.1000000014901161f,
            FinalScale = flags2 / 100f + 0.1000000014901161f,
            ShearMatrix = Matrix4x4.identity
        };

        if (info.Sheared)
        {
            info.RotationSteps = (int)(info.RotationPacked / 0x276);
            info.YawRadians = (float)(info.RotationPacked % 0x276 * 0.009973309934139252);
            info.YawDegrees = info.YawRadians * Mathf.Rad2Deg;
            info.Rotation = AxisAngle(Vector3.up, info.YawRadians);

            info.ScaleSteps = info.RotationSteps % 0xD3;
            info.ScaleFactor = info.ScaleSteps / 100f + 0.5f;
            info.FinalScale = info.BaseScale * info.ScaleFactor;

            // Stock direct-quat anim matrix:
            //   Mat4_PostScaleByDiagXYZ(1, 1/var, 1/var)
            //   FUN_10026d5c(mat, shear, 0)  → Y-slant (y += shear * x)
            // Shear magnitude from high bits; second arg always 0. Applied scale
            // on the root is (FinalScale, BaseScale, BaseScale).
            info.ShearFactor = info.RotationSteps / 0xD3 / 100f - 1.25f;
            info.ShearMatrix = CreateShear(new Vector2(info.ShearFactor, 0f));
        }
        else
        {
            if (info.RotationPacked < 0x163F500)
            {
                info.X = info.RotationPacked % 0xb4;
                info.V4 = info.RotationPacked / 0xb4;
            }
            else
            {
                info.RotationOverflow = true;
                info.X = 180;
                info.V4 = info.RotationPacked - 0x163F500;
            }

            info.AngleYRadians = (float)(info.V4 % 360 * 6.283199787139893 / 360.0);
            info.AngleYDegrees = info.AngleYRadians * Mathf.Rad2Deg;
            info.Rotation = AxisAngle(Vector3.up, info.AngleYRadians);

            info.AngleXRadians = (float)((int)(info.X - 90) * 6.283199787139893 / 360.0);
            info.AngleXDegrees = info.AngleXRadians * Mathf.Rad2Deg;
            // MulQuat(a,b) ≡ b*a (Hamilton): left-multiply / world axes.
            info.Rotation = MulQuat(info.Rotation, AxisAngle(Vector3.right, info.AngleXRadians));

            info.AngleZRadians = (float)(info.V4 / 360 * 6.283199787139893 / 360.0);
            info.AngleZDegrees = info.AngleZRadians * Mathf.Rad2Deg;
            info.Rotation = MulQuat(info.Rotation, AxisAngle(Vector3.forward, info.AngleZRadians));
        }

        return info;
    }

    /// <summary>
    /// System.Numerics / D3D-style shear on identity (row-vector v*M: y' += a.x*x, z' += a.y*x).
    /// Returned as a Unity matrix for MultiplyPoint (column vector), i.e. the transpose of the SN layout.
    /// </summary>
    public static Matrix4x4 CreateShear(Vector2 a)
    {
        // SN identity after CreateShear(a): row0=(1,a.x,a.y,0)
        // Unity M*v with columns = transpose of those rows:
        var result = Matrix4x4.identity;
        result.m10 = a.x; // y += a.x * x
        result.m20 = a.y; // z += a.y * x
        return result;
    }

    /// <summary>Matches the client AxisAngle (radians). Differs from Unity for angle &lt; 0 or &gt;= 2π.</summary>
    public static Quaternion AxisAngle(Vector3 axis, float angle)
    {
        double v4 = angle;
        double v5;
        if (angle < 0f || v4 >= 6.283185307179586)
        {
            float a3a = (float)(v4 / 6.283185482025146);
            float a3b = Mathf.Floor(a3a);
            float a3c = a3a - a3b;
            v5 = a3c * 3.141592741012573;
        }
        else
        {
            v5 = v4 * 0.5;
        }

        float s = Mathf.Sin((float)v5);
        return new Quaternion(axis.x * s, axis.y * s, s * axis.z, Mathf.Cos((float)v5));
    }

    /// <summary>Client MulQuat(a1,a2) ≡ Hamilton a2*a1 (Unity left-multiply).</summary>
    public static Quaternion MulQuat(Quaternion a1, Quaternion a2) => a2 * a1;

    public struct ScaleRotationInfo
    {
        public bool Sheared;
        public uint RotationPacked;
        public float BaseScale;
        public float FinalScale;
        public float ShearFactor;
        public Matrix4x4 ShearMatrix;
        public Quaternion Rotation;

        // Shear path
        public int RotationSteps;
        public float YawRadians;
        public float YawDegrees;
        public int ScaleSteps;
        public float ScaleFactor;

        // Non-shear path
        public bool RotationOverflow;
        public uint X;
        public uint V4;
        public float AngleXRadians;
        public float AngleYRadians;
        public float AngleZRadians;
        public float AngleXDegrees;
        public float AngleYDegrees;
        public float AngleZDegrees;
    }

    static IEnumerator RunParallel(Action work)
    {
        Task task = Task.Run(work);
        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            throw task.Exception?.InnerException ?? task.Exception;
    }

    sealed class StatelPlacement
    {
        public int MeshId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public float ShearFactor;
        public int Flag;
        public byte Flags2;
        public ScaleRotationInfo Transform;
        public int[] TextureOverrides;
        public StatelLightTweak LightSettings;
    }

    sealed class MeshSource
    {
        public int MeshId;
        public AbiffSubmeshSource[] Submeshes;
    }

    readonly struct MeshVariantKey : IEquatable<MeshVariantKey>
    {
        public readonly int MeshId;
        public readonly bool ApplyShear;
        public readonly int ShearBits;

        public MeshVariantKey(int meshId, bool applyShear, int shearBits)
        {
            MeshId = meshId;
            ApplyShear = applyShear;
            ShearBits = shearBits;
        }

        public static MeshVariantKey FromPlacement(StatelPlacement placement)
        {
            ScaleRotationInfo t = placement.Transform;
            return new MeshVariantKey(
                placement.MeshId,
                t.Sheared,
                BitConverter.SingleToInt32Bits(t.ShearFactor));
        }

        public bool Equals(MeshVariantKey other) =>
            MeshId == other.MeshId
            && ApplyShear == other.ApplyShear
            && ShearBits == other.ShearBits;

        public override bool Equals(object obj) => obj is MeshVariantKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MeshId * 397;
                hash = (hash * 397) ^ (ApplyShear ? 1 : 0);
                hash = (hash * 397) ^ ShearBits;
                return hash;
            }
        }
    }
}
