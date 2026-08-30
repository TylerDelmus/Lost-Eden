using System.Collections;
using System.Collections.Generic;
using AODB;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using AoVector3 = AODB.Common.Structs.Vector3;

public sealed class PlayfieldWaterBuilder
{
    readonly ResourceDatabase _database;
    readonly RenderConfig _renderConfig;

    public PlayfieldWaterBuilder(ResourceDatabase database, RenderConfig renderConfig)
    {
        _database = database;
        _renderConfig = renderConfig;
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        if (_database?.Rdb == null)
        {
            Debug.LogError("PlayfieldWaterBuilder: ResourceDatabase is not initialized.");
            yield break;
        }

        List<PfWaterMeshData> waterMeshes;
        try
        {
            waterMeshes = new WaterParser(_database.Rdb).Get(playfieldId);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PlayfieldWaterBuilder: Failed to parse water for playfield {playfieldId}: {ex.Message}");
            yield break;
        }

        if (waterMeshes == null || waterMeshes.Count == 0)
            yield break;

        PlayfieldTweakFile tweak = PlayfieldTweakCatalog.Get(playfieldId);

        var root = new GameObject($"Water_{playfieldId}");
        root.transform.SetParent(parent, false);

        ShoreWaveController shoreWaves = null;

        for (int i = 0; i < waterMeshes.Count; i++)
        {
            CreateWaterBody(root.transform, waterMeshes[i], i, tweak, ref shoreWaves);
            if ((i + 1) % 8 == 0)
                yield return null;
        }

        if (shoreWaves != null)
        {
            Material overrideMat = _renderConfig != null ? _renderConfig.ShoreWaveMaterial : null;
            shoreWaves.Build(playfieldId, _database, overrideMat);
        }
    }

    void CreateWaterBody(
        Transform parent,
        PfWaterMeshData source,
        int index,
        PlayfieldTweakFile tweak,
        ref ShoreWaveController shoreWaves)
    {
        if (source?.Vertices == null || source.Vertices.Length == 0 ||
            source.Triangles == null || source.Triangles.Length < 3)
            return;

        if (!TryConvertVertices(source.Vertices, out Vector3[] vertices, out float waterLevel))
            return;

        var mesh = new Mesh
        {
            name = $"WaterMesh_{index}",
            indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(source.Triangles, 0, calculateBounds: false);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Bounds worldBounds = mesh.bounds;
        mesh.UploadMeshData(markNoLongerReadable: true);

        WaterSurfaceType surfaceType = PlayfieldTweakCatalog.ResolveSurfaceType(tweak, index);
        ShoreWaveSettings waveSettings = PlayfieldTweakCatalog.ResolveWaves(tweak, index);

        var surfaceGo = new GameObject($"WaterBody_{index}");
        surfaceGo.transform.SetParent(parent, false);
        surfaceGo.transform.position = new Vector3(0f, waterLevel, 0f);

        var meshGo = new GameObject("Geometry");
        meshGo.transform.SetParent(parent, false);
        meshGo.transform.localPosition = Vector3.zero;
        meshGo.transform.localRotation = Quaternion.identity;
        meshGo.transform.localScale = Vector3.one;

        var filter = meshGo.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = meshGo.AddComponent<MeshRenderer>();
        renderer.forceRenderingOff = true;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var water = surfaceGo.AddComponent<WaterSurface>();
        ConfigureWaterSurface(water, renderer, surfaceType, waveSettings);

        if (surfaceType == WaterSurfaceType.OceanSeaLake && waveSettings.Enabled)
        {
            if (shoreWaves == null)
                shoreWaves = parent.gameObject.AddComponent<ShoreWaveController>();

            shoreWaves.AddOceanBody(waterLevel, worldBounds, waveSettings);
        }
    }

    void ConfigureWaterSurface(
        WaterSurface water,
        MeshRenderer renderer,
        WaterSurfaceType surfaceType,
        ShoreWaveSettings waveSettings)
    {
        water.surfaceType = surfaceType;
        water.geometryType = WaterGeometryType.Custom;
        water.meshRenderers = new List<MeshRenderer> { renderer };
        water.timeMultiplier = 0.8f;
        water.scriptInteractions = false;
        water.tessellation = false;
        water.ripples = true;
        water.ripplesWindSpeed = 5f;
        water.ripplesChaos = 1f;
        water.refractionColor = new Color(0xE2 / 255f, 0xE2 / 255f, 0xE2 / 255f).linear;
        water.maxRefractionDistance = 0.35f;
        water.absorptionDistance = 5f;
        water.scatteringColor = Color.white.linear;
        water.ambientScattering = 0.6f;
        water.heightScattering = 0f;
        water.displacementScattering = 0f;
        water.directLightBodyScattering = 0.2f;
        water.directLightTipScattering = 0.2f;
        water.caustics = true;
        water.causticsPlaneBlendDistance = 2f;
        water.underWater = false;

        bool ocean = surfaceType == WaterSurfaceType.OceanSeaLake;
        water.foam = ocean;
        water.deformation = ocean;

        if (ocean)
        {
            float region = waveSettings.ActivationRadius * 2f + 40f;
            water.decalRegionSize = new Vector2(region, region);
            water.largeWindSpeed = 20f;
            water.foamPersistenceMultiplier = 0.5f;
            water.foamTextureTiling = 0.15f;
            water.simulationFoamAmount = 0.05f;
        }

        if (_renderConfig != null && _renderConfig.WaterMaterial != null)
            water.customMaterial = _renderConfig.WaterMaterial;
    }

    static bool TryConvertVertices(AoVector3[] source, out Vector3[] vertices, out float waterLevel)
    {
        vertices = null;
        waterLevel = 0f;

        if (source == null || source.Length == 0)
            return false;

        vertices = new Vector3[source.Length];
        double sumY = 0d;
        for (int i = 0; i < source.Length; i++)
        {
            AoVector3 v = source[i];
            vertices[i] = new Vector3(v.X, v.Y, v.Z);
            sumY += v.Y;
        }

        waterLevel = (float)(sumY / source.Length);
        return true;
    }
}
