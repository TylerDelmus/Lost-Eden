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
    [SerializeField] int _terrainAtlasPadding = 16;
    [Tooltip("First mip level to blur (0 = full res, kept sharp). 2 softens only farther distances.")]
    [SerializeField] int _terrainAtlasFirstMipToSoften = 2;
    [Tooltip("Base 3x3 blur passes at the first softened mip; higher mips get more.")]
    [SerializeField] int _terrainAtlasMipBlurPasses = 3;
    [SerializeField] int _terrainAtlasAnisoLevel = 8;
    [Tooltip("Positive bias makes distant ground pick softer mips sooner.")]
    [SerializeField] float _terrainAtlasMipBias = 0.5f;

    [Header("Water")]
    [Tooltip("Fallback HDRP water body type when no playfield tweak overrides a mesh (tweaks default to Pool).")]
    [SerializeField] WaterSurfaceType _waterSurfaceType = WaterSurfaceType.Pool;
    [Tooltip("Optional custom HDRP water material. Leave empty to use the default water material.")]
    [SerializeField] Material _waterMaterial;
    [Tooltip("Optional Shore Wave WaterDecal material. Leave empty to build one from HDRP's migration shader at runtime.")]
    [SerializeField] Material _shoreWaveMaterial;

    [Header("AO Playfield Tweaks")]
    [Tooltip("When enabled, load AO cd_image/twk environment (sun/fog/sky meshes) per playfield. Disable to keep stock HDRP.")]
    [SerializeField] bool _applyAoPlayfieldTweaks = true;
    [Tooltip("When enabled, spawn AO camera-locked sky/cloud meshes (star dome, thick clouds, horizon, etc.).")]
    [SerializeField] bool _applyAoSkyMeshes = false;

    public float[] TerrainLodScreenHeights => _terrainLodScreenHeights;
    public int TerrainAtlasMaxSize => _terrainAtlasMaxSize;
    public int TerrainAtlasPadding => _terrainAtlasPadding;
    public int TerrainAtlasFirstMipToSoften => _terrainAtlasFirstMipToSoften;
    public int TerrainAtlasMipBlurPasses => _terrainAtlasMipBlurPasses;
    public int TerrainAtlasAnisoLevel => _terrainAtlasAnisoLevel;
    public float TerrainAtlasMipBias => _terrainAtlasMipBias;
    public WaterSurfaceType WaterSurfaceType => _waterSurfaceType;
    public Material WaterMaterial => _waterMaterial;
    public Material ShoreWaveMaterial => _shoreWaveMaterial;
    public bool ApplyAoPlayfieldTweaks => _applyAoPlayfieldTweaks;
    public bool ApplyAoSkyMeshes => _applyAoSkyMeshes;

    public float GetTerrainLodScreenHeight(int lod)
    {
        if (_terrainLodScreenHeights == null || _terrainLodScreenHeights.Length == 0)
            return 0.1f;

        int index = Mathf.Clamp(lod, 0, _terrainLodScreenHeights.Length - 1);
        return Mathf.Clamp01(_terrainLodScreenHeights[index]);
    }
}
