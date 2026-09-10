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

    #region Grass
    [Header("Grass - assets")]
    [SerializeField] bool _grassEnabled = true;
    [SerializeField] Mesh _grassMesh;
    [SerializeField] Material _grassMaterial;

    [Tooltip("Draw with stock GPU instancing instead of the custom indirect path. " +
             "Works with any HDRP material that has GPU Instancing ticked. Slower, but " +
             "if grass shows up here and not in indirect mode, the placement data is " +
             "fine and the problem is the shader.")]
    [SerializeField] bool _grassUseInstancedFallback;

    [Tooltip("Plain HDRP Lit material with GPU Instancing ticked, used only by the " +
             "fallback draw mode. Must NOT be the grass Shader Graph material - that one " +
             "declares procedural instancing, so it would still read the instance buffer " +
             "and the test would prove nothing.")]
    [SerializeField] Material _grassFallbackMaterial;

    [Tooltip("Number of variants in the grass Texture2DArray. Only used as a fallback - if " +
             "the material's Base Color Map is a Texture2DArray, its actual slice count wins.")]
    [Min(1)]
    [SerializeField] int _grassVariantCount = 1;

    [Header("Grass - automatic tile classification")]
    // Which ground textures are grass is worked out from their pixels at load. There is no
    // list to maintain: AO has far too many ground textures to enumerate by hand, and the
    // ids mean different things in different playfields.

    [Range(4, 64)]
    [SerializeField] int _grassMaskResolution = 16;

    [Tooltip("Fraction of a mask cell's pixels that must read as grass-coloured for that " +
             "cell to accept blades.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassMaskThreshold = 0.5f;

    [Tooltip("Coverage at or above which a texture counts as fully grass - the mask is " +
             "dropped so stray pebbles don't punch holes in an otherwise solid lawn.")]
    [Range(0.5f, 1f)]
    [SerializeField] float _grassFullCoverageThreshold = 0.85f;

    [Tooltip("Coverage below which a texture is ignored entirely. Raise this if faintly " +
             "mossy rock or dirt is picking up stray blades.")]
    [Range(0f, 0.5f)]
    [SerializeField] float _grassMinCoverageThreshold = 0.06f;

    [Tooltip("Minimum saturation for a pixel to read as grass.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassMinSaturation = 0.08f;

    [Tooltip("How far the green channel must lead red and blue. The main defence against " +
             "grey-green rock: a desaturated surface can land in the hue band by accident, " +
             "but cannot have green meaningfully ahead of both others. Raise to be stricter.")]
    [Range(0f, 0.3f)]
    [SerializeField] float _grassMinGreenDominance = 0.01f;

    [Tooltip("Logs a table of every ground texture with its coverage % and verdict at load. " +
             "Leave on until you trust the classification for a zone.")]
    [SerializeField] bool _grassLogClassification = true;

    [Tooltip("Escape hatch: GroundTexture RDB ids to always treat as fully grass, when the " +
             "pixel test misses one. Normally empty.")]
    [SerializeField] int[] _grassForceTextureIds = { };

    [Tooltip("Escape hatch: GroundTexture RDB ids to never treat as grass - green water, " +
             "mossy rock, canopy art. Normally empty.")]
    [SerializeField] int[] _grassExcludeTextureIds = { };

    [Header("Grass - placement")]
    [Tooltip("Candidate blades per square metre of ground before slope/mask rejection.")]
    [SerializeField] float _grassDensityPerSquareMetre = 32f;

    [Range(0f, 90f)]
    [SerializeField] float _grassMaxSlopeDegrees = 35f;

    [Tooltip("Width multiplier on the grass mesh's X/Z. Varied independently of height - " +
             "tying the two together just scales one silhouette up and down.")]
    [SerializeField] float _grassMinWidth = 3f;
    [SerializeField] float _grassMaxWidth = 5f;

    [Tooltip("Height multiplier on the grass mesh's Y.")]
    [SerializeField] float _grassMinHeight = 0.4f;
    [SerializeField] float _grassMaxHeight = 0.8f;

    [Tooltip("Safety valve. A chunk that hits this is cut off part-way through, so its " +
             "grass stops abruptly rather than thinning - the load log warns when it happens.")]
    [SerializeField] int _grassMaxInstancesPerChunk = 2000000;

    [Tooltip("Hand-authored boxes, per playfield, where grass must not be placed - bridge " +
             "decks, building floors. Blades whose vertical span intersects one are dropped " +
             "at bake time. Author them with GrassExclusionAuthoring. Leave empty to disable.")]
    [SerializeField] GrassExclusionVolumes _grassExclusionVolumes;

    [Header("Grass - shading")]
    [Tooltip("How far each blade's normal leans toward world up. A grass card is a flat " +
             "quad, so one horizontal normal lights the whole card uniformly - that is what " +
             "makes an unbent field read as flat cut-outs, and dark, since a sideways " +
             "surface catches far less sky than the ground beside it. 0.5-0.8 usually reads " +
             "best. REQUIRES Double-Sided Normal Mode = None on the material.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassNormalUpBlend = 0.65f;

    [Header("Grass - ground tint")]
    // Shifts the coloured grass art toward the hue of the ground beneath it, keeping all the
    // painted detail. Applied in the shader, so this dial is live - no reload needed.

    [Tooltip("How far each blade shifts toward the colour of the ground beneath it. " +
             "0 = the texture's own colours untouched, 1 = fully re-hued to the ground. " +
             "0.3-0.6 usually reads as 'this grass belongs here' without losing the art.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassGroundTintStrength = 0.45f;

    [Tooltip("Amplifies how far each blade's colour departs from the playfield's own mean " +
             "grass colour. A zone's ground art is usually all fairly similar green, so a " +
             "faithful tint gives a faithfully uniform field - the drier patches are there " +
             "but too subtle to read. Raise this to make them show. 1 = faithful, 2-3 = " +
             "clearly patchy. Rebake (reload the zone) to see changes.")]
    [Range(1f, 4f)]
    [SerializeField] float _grassGroundTintContrast = 1.8f;

    [Tooltip("Used only where there is no ground colour to sample. Pick a believable " +
             "mid-green for the zone.")]
    [SerializeField] Color _grassFallbackColour = new Color(0.36f, 0.45f, 0.20f, 1f);

    [Tooltip("Saturation multiplier on the sampled ground colour. Growing grass reads " +
             "richer than the dirt-and-grass average of the texture under it, so passing " +
             "the sample through unchanged gives a washed-out, muddy field.")]
    [Range(0.5f, 3f)]
    [SerializeField] float _grassGroundTintSaturation = 1.35f;

    [Tooltip("Brightness multiplier on the sampled ground colour.")]
    [Range(0.5f, 2f)]
    [SerializeField] float _grassGroundTintBrightness = 1.15f;

    [Tooltip("Floor on brightness, so grass over a very dark texture does not read as black " +
             "silhouettes.")]
    [Range(0f, 1f)]
    [SerializeField] float _grassGroundTintMinValue = 0.22f;

    [Header("Grass - wind")]
    [SerializeField] float _grassWindStrength = 0.3f;
    [SerializeField] float _grassWindFrequency = 1.6f;

    // -----------------------------------------------------------------------
    // Baked grass tuning
    //
    // These settled during bring-up and are the same for every zone, so they are
    // compile-time constants rather than inspector fields - the grass section was
    // crowding out everything else in RenderConfig. They are still read through the
    // properties below, so nothing else in the system had to change.
    //
    // To make one tunable again without putting it back in the inspector, swap the
    // constant for a [HideInInspector] [SerializeField] field: it stays hidden in the
    // normal view, shows up in the Inspector's Debug mode, and can be assigned from
    // script. Worth doing for cull distance and the LOD settings if you end up
    // profiling them.
    // -----------------------------------------------------------------------

    /// <summary>0 = blades stand straight up, 1 = blades lie along the terrain normal.</summary>
    const float BakedGrassNormalAlignment = 0f;

    /// <summary>Sink blades slightly so their base is never floating over the mesh.</summary>
    const float BakedGrassHeightOffset = -0.05f;

    /// <summary>Fraction of candidate points that survive, before slope and mask rejection.</summary>
    const float BakedGrassCoverage = 1f;

    /// <summary>
    /// Height of the grass mesh in its own local units. Drives the wind bend falloff and
    /// the chunk bounds padding - wrong here and blades bend from the wrong point or get
    /// culled early.
    /// </summary>
    const float BakedGrassBladeHeight = 1f;

    const float BakedGrassCullDistance = 120f;

    /// <summary>Width of the band before the cull distance over which blades fade out.</summary>
    const float BakedGrassFadeBand = 25f;

    /// <summary>Full density inside this radius; thinning begins beyond it.</summary>
    const float BakedGrassLodStartDistance = 25f;

    /// <summary>Fraction of a chunk's blades still drawn at the cull distance.</summary>
    const float BakedGrassLodMinDensity = 0.15f;

    /// <summary>Discrete density levels between full and minimum. More = smaller pops.</summary>
    const int BakedGrassLodSteps = 16;

    /// <summary>HDRP rendering layer mask. 0 is treated as 1 (default layer).</summary>
    const uint BakedGrassRenderingLayerMask = 1;

    const float BakedGrassWindDirectionDegrees = 45f;

    /// <summary>How fast the sway phase varies across world space. Higher = shorter waves.</summary>
    const float BakedGrassWindPhaseScale = 0.35f;

    /// <summary>Amplitude of the slower second harmonic that breaks up the single-sine look.</summary>
    const float BakedGrassWindGustScale = 0.4f;

    /// <summary>Random per-blade sway phase offset, 0-1. Stops neighbours moving in lockstep.</summary>
    const float BakedGrassWindPhaseJitter = 0.25f;

    /// <summary>
    /// Margin added around each occluder volume, in metres. A little slack stops blades
    /// clipping the very edge of a deck or wall; too much clears a visible gap around
    /// every object.
    /// </summary>
    const float BakedGrassOccluderPadding = 0.1f;

    // ---- Serialized ----
    public bool GrassEnabled => _grassEnabled;
    public Mesh GrassMesh => _grassMesh;
    public Material GrassMaterial => _grassMaterial;
    public bool GrassUseInstancedFallback => _grassUseInstancedFallback;
    public Material GrassFallbackMaterial => _grassFallbackMaterial;
    public int GrassVariantCount => _grassVariantCount;
    public int GrassMaskResolution => _grassMaskResolution;
    public float GrassMaskThreshold => _grassMaskThreshold;
    public float GrassFullCoverageThreshold => _grassFullCoverageThreshold;
    public float GrassMinCoverageThreshold => _grassMinCoverageThreshold;
    public float GrassMinSaturation => _grassMinSaturation;
    public float GrassMinGreenDominance => _grassMinGreenDominance;
    public bool GrassLogClassification => _grassLogClassification;
    public int[] GrassForceTextureIds => _grassForceTextureIds;
    public int[] GrassExcludeTextureIds => _grassExcludeTextureIds;
    public float GrassDensityPerSquareMetre => _grassDensityPerSquareMetre;
    public float GrassMaxSlopeDegrees => _grassMaxSlopeDegrees;
    public float GrassMinWidth => _grassMinWidth;
    public float GrassMaxWidth => _grassMaxWidth;
    public float GrassMinHeight => _grassMinHeight;
    public float GrassMaxHeight => _grassMaxHeight;
    public int GrassMaxInstancesPerChunk => _grassMaxInstancesPerChunk;
    public float GrassNormalUpBlend => _grassNormalUpBlend;
    public float GrassGroundTintStrength => _grassGroundTintStrength;
    public float GrassGroundTintContrast => _grassGroundTintContrast;
    public Color GrassFallbackColour => _grassFallbackColour;
    public float GrassGroundTintSaturation => _grassGroundTintSaturation;
    public float GrassGroundTintBrightness => _grassGroundTintBrightness;
    public float GrassGroundTintMinValue => _grassGroundTintMinValue;
    public float GrassWindStrength => _grassWindStrength;
    public float GrassWindFrequency => _grassWindFrequency;
    public GrassExclusionVolumes GrassExclusionVolumes => _grassExclusionVolumes;

    // ---- Baked ----
    public float GrassNormalAlignment => BakedGrassNormalAlignment;
    public float GrassHeightOffset => BakedGrassHeightOffset;
    public float GrassCoverage => BakedGrassCoverage;
    public float GrassBladeHeight => BakedGrassBladeHeight;
    public float GrassCullDistance => BakedGrassCullDistance;
    public float GrassFadeBand => BakedGrassFadeBand;
    public float GrassLodStartDistance => BakedGrassLodStartDistance;
    public float GrassLodMinDensity => BakedGrassLodMinDensity;
    public int GrassLodSteps => BakedGrassLodSteps;
    public uint GrassRenderingLayerMask => BakedGrassRenderingLayerMask;
    public float GrassWindDirectionDegrees => BakedGrassWindDirectionDegrees;
    public float GrassWindPhaseScale => BakedGrassWindPhaseScale;
    public float GrassWindGustScale => BakedGrassWindGustScale;
    public float GrassWindPhaseJitter => BakedGrassWindPhaseJitter;
    public float GrassOccluderPadding => BakedGrassOccluderPadding;


    #endregion


    public float GetTerrainLodScreenHeight(int lod)
    {
        if (_terrainLodScreenHeights == null || _terrainLodScreenHeights.Length == 0)
            return 0.1f;

        int index = Mathf.Clamp(lod, 0, _terrainLodScreenHeights.Length - 1);
        return Mathf.Clamp01(_terrainLodScreenHeights[index]);
    }
}
