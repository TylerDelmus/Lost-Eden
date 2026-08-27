using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[CreateAssetMenu(fileName = "RenderConfig", menuName = "Lost Eden/Render Config")]
public sealed class RenderConfig : ScriptableObject
{
    [Header("Terrain LOD")]
    [Tooltip("LODGroup screen-relative heights for terrain chunk LOD0, LOD1, LOD2 (0-1).")]
    [SerializeField] float[] _terrainLodScreenHeights = { 0.6f, 0.25f, 0.08f };

    [Header("Terrain Atlas")]
    [SerializeField] int _terrainAtlasMaxSize = 8192;
    [SerializeField] int _terrainAtlasPadding = 0;

    [Header("Water")]
    [Tooltip("HDRP water body type used for AODB water meshes.")]
    [SerializeField] WaterSurfaceType _waterSurfaceType = WaterSurfaceType.Pool;
    [Tooltip("Optional custom HDRP water material. Leave empty to use the default water material.")]
    [SerializeField] Material _waterMaterial;

    public float[] TerrainLodScreenHeights => _terrainLodScreenHeights;
    public int TerrainAtlasMaxSize => _terrainAtlasMaxSize;
    public int TerrainAtlasPadding => _terrainAtlasPadding;
    public WaterSurfaceType WaterSurfaceType => _waterSurfaceType;
    public Material WaterMaterial => _waterMaterial;

    public float GetTerrainLodScreenHeight(int lod)
    {
        if (_terrainLodScreenHeights == null || _terrainLodScreenHeights.Length == 0)
            return 0.1f;

        int index = Mathf.Clamp(lod, 0, _terrainLodScreenHeights.Length - 1);
        return Mathf.Clamp01(_terrainLodScreenHeights[index]);
    }
}
