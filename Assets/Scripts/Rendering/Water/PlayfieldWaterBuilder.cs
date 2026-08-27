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

        var root = new GameObject($"Water_{playfieldId}");
        root.transform.SetParent(parent, false);

        for (int i = 0; i < waterMeshes.Count; i++)
        {
            CreateWaterBody(root.transform, waterMeshes[i], i);
            if ((i + 1) % 8 == 0)
                yield return null;
        }
    }

    void CreateWaterBody(Transform parent, PfWaterMeshData source, int index)
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
        mesh.UploadMeshData(markNoLongerReadable: true);

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
        ConfigureWaterSurface(water, renderer);
    }

    void ConfigureWaterSurface(WaterSurface water, MeshRenderer renderer)
    {
        WaterSurfaceType surfaceType = _renderConfig != null
            ? _renderConfig.WaterSurfaceType
            : WaterSurfaceType.Pool;

        // Calm AO-style bodies: pool preset values with custom mesh geometry.
        water.surfaceType = surfaceType;
        water.geometryType = WaterGeometryType.Custom;
        water.meshRenderers = new List<MeshRenderer> { renderer };
        water.timeMultiplier = 0.8f;
        water.scriptInteractions = false;
        water.tessellation = false;
        water.ripples = true;
        water.ripplesWindSpeed = 5f;
        water.ripplesChaos = 1f;
        water.refractionColor = new Color(0.2f, 0.55f, 0.55f).linear;
        water.maxRefractionDistance = 0.35f;
        water.absorptionDistance = 5f;
        water.scatteringColor = new Color(0f, 0.5f, 0.6f).linear;
        water.ambientScattering = 0.6f;
        water.heightScattering = 0f;
        water.displacementScattering = 0f;
        water.directLightBodyScattering = 0.2f;
        water.directLightTipScattering = 0.2f;
        water.caustics = true;
        water.causticsPlaneBlendDistance = 2f;
        water.underWater = false;
        water.foam = false;

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
