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

    [Header("Reflections")]
    [Tooltip("When enabled, bake HDRP reflection probes after a playfield finishes loading.")]
    [SerializeField] bool _useReflectionProbe = true;

    /// <summary>
    /// Grass
    /// </summary>
    [Header("Grass - assets")]
    [SerializeField] bool _grassEnabled = true;
    [SerializeField] Mesh _grassMesh;
    [SerializeField] Material _grassMaterial;
    [SerializeField] bool _grassUseInstancedFallback;
    [SerializeField] Material _grassFallbackMaterial;

    [Header("Grass - tile classification (GroundTexture RDB ids)")]
    [Tooltip("Tiles textured entirely with grass. Scatter at full density everywhere.")]
    [SerializeField] int[] _grassFullTextureIds = { 15, 258, 283, 286, 284, 289, 288 };

    [Tooltip("Transition/edge tiles - part grass, part dirt or rock. Scatter only where " +
             "the texture is actually grass-coloured.")]
    [SerializeField] int[] _grassPartialTextureIds = { 87, 262, 264, 261, 263, 86, 269 };

    [Range(4, 64)]
    [SerializeField] int _grassMaskResolution = 16;

    [Tooltip("Fraction of a mask cell's pixels that must read as grass-coloured for " +
             "that cell to accept blades.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassMaskThreshold = 0.5f;

    [Tooltip("Log per-texture grass coverage % at load, so the threshold can be checked " +
             "against the actual art.")]
    [SerializeField] bool _grassLogMaskCoverage = true;

    [Header("Grass - placement")]
    [Tooltip("Candidate blades per square metre of ground before coverage/slope/mask rejection.")]
    [SerializeField] float _grassDensityPerSquareMetre = 4f;

    [Tooltip("Fraction of candidate points that survive. Thin the field out without " +
             "changing the sampling lattice.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassCoverage = 1f;

    [Range(0f, 90f)]
    [SerializeField] float _grassMaxSlopeDegrees = 35f;

    [SerializeField] float _grassMinScale = 0.8f;
    [SerializeField] float _grassMaxScale = 1.3f;

    [Tooltip("0 = blades stand straight up, 1 = blades lie along the terrain normal.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassNormalAlignment = 0.35f;

    [Tooltip("Sink blades slightly so their base is never floating over the mesh.")]
    [SerializeField] float _grassHeightOffset = -0.05f;

    [SerializeField] int _grassMaxInstancesPerChunk = 200000;

    [Header("Grass - rendering")]
    [Tooltip("Height of the grass mesh in its own local units. Drives both the wind bend " +
             "falloff and the chunk bounds padding - get this wrong and blades either " +
             "bend from the wrong point or get culled early.")]
    [SerializeField] float _grassBladeHeight = 1f;

    [SerializeField] float _grassCullDistance = 120f;

    [Tooltip("Width of the band before the cull distance over which blades fade out.")]
    [SerializeField] float _grassFadeBand = 25f;

    [Tooltip("HDRP rendering layer mask. 0 is treated as 1 (default layer).")]
    [SerializeField] uint _grassRenderingLayerMask = 1;

    [Header("Grass - wind")]
    [SerializeField] float _grassWindDirectionDegrees = 45f;
    [SerializeField] float _grassWindStrength = 0.15f;
    [SerializeField] float _grassWindFrequency = 1.6f;

    [Tooltip("How fast the sway phase varies across world space. Higher = shorter waves.")]
    [SerializeField] float _grassWindPhaseScale = 0.35f;

    [Tooltip("Amplitude of the slower second harmonic that breaks up the single-sine look.")]
    [SerializeField] float _grassWindGustScale = 0.4f;

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
    public bool UseReflectionProbe => _useReflectionProbe;

    /// <summary>
    /// Grass
    /// </summary>
    public bool GrassEnabled => _grassEnabled;
    public Mesh GrassMesh => _grassMesh;
    public Material GrassMaterial => _grassMaterial;
    public bool GrassUseInstancedFallback => _grassUseInstancedFallback;
    public Material GrassFallbackMaterial => _grassFallbackMaterial;
    public int[] GrassFullTextureIds => _grassFullTextureIds;
    public int[] GrassPartialTextureIds => _grassPartialTextureIds;
    public int GrassMaskResolution => _grassMaskResolution;
    public float GrassMaskThreshold => _grassMaskThreshold;
    public bool GrassLogMaskCoverage => _grassLogMaskCoverage;
    public float GrassDensityPerSquareMetre => _grassDensityPerSquareMetre;
    public float GrassCoverage => _grassCoverage;
    public float GrassMaxSlopeDegrees => _grassMaxSlopeDegrees;
    public float GrassMinScale => _grassMinScale;
    public float GrassMaxScale => _grassMaxScale;
    public float GrassNormalAlignment => _grassNormalAlignment;
    public float GrassHeightOffset => _grassHeightOffset;
    public int GrassMaxInstancesPerChunk => _grassMaxInstancesPerChunk;
    public float GrassBladeHeight => _grassBladeHeight;
    public float GrassCullDistance => _grassCullDistance;
    public float GrassFadeBand => _grassFadeBand;
    public uint GrassRenderingLayerMask => _grassRenderingLayerMask;
    public float GrassWindDirectionDegrees => _grassWindDirectionDegrees;
    public float GrassWindStrength => _grassWindStrength;
    public float GrassWindFrequency => _grassWindFrequency;
    public float GrassWindPhaseScale => _grassWindPhaseScale;
    public float GrassWindGustScale => _grassWindGustScale;

    public float GetTerrainLodScreenHeight(int lod)
    {
        if (_terrainLodScreenHeights == null || _terrainLodScreenHeights.Length == 0)
            return 0.1f;

        int index = Mathf.Clamp(lod, 0, _terrainLodScreenHeights.Length - 1);
        return Mathf.Clamp01(_terrainLodScreenHeights[index]);
    }
}
