using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws one chunk's grass with a single indirect instanced draw call.
///
/// Positions are baked once by <see cref="PlayfieldGrassBuilder"/> at playfield load;
/// per-frame cost is a distance test, a frustum test and one
/// <see cref="Graphics.RenderMeshIndirect"/> call while the chunk is visible.
///
/// There is no MeshRenderer/MeshFilter here, so none of Unity's usual visual debugging
/// applies. The gizmos and the one-shot state logging exist to make this otherwise
/// invisible pipeline diagnosable, and <see cref="Mode"/> lets you swap the custom
/// indirect path for a stock instanced path to work out whether a problem is in the
/// placement data or in the shader.
/// </summary>
public sealed class GrassChunkRenderer : MonoBehaviour
{
    public enum DrawMode
    {
        /// <summary>One RenderMeshIndirect call, transforms read from a GraphicsBuffer
        /// by the Shader Graph's procedural instancing Setup(). Requires the grass
        /// material to use GrassInstancing.hlsl.</summary>
        Indirect = 0,

        /// <summary>RenderMeshInstanced in batches of 511, transforms uploaded from the
        /// CPU each frame using stock unity_ObjectToWorld instancing. Slower, but if grass
        /// appears in this mode and not in Indirect, the placement data is fine and the
        /// problem is the shader.
        ///
        /// Needs its OWN plain material (RenderConfig.GrassFallbackMaterial): the grass
        /// Shader Graph declares procedural instancing, so under a non-indirect draw its
        /// Setup() would still fire and read transforms out of the buffer that this path
        /// is not using.</summary>
        InstancedFallback = 1
    }

    const int InstancedBatchSize = 511;

    [SerializeField] DrawMode _mode = DrawMode.Indirect;

    Mesh _mesh;
    Material _material;          // per-chunk instance; owns the buffer binding
    Material _fallbackMaterial;  // plain instanced material, InstancedFallback mode only
    GraphicsBuffer _instanceBuffer;
    GraphicsBuffer _argsBuffer;
    Bounds _bounds;
    float _cullDistance;
    int _instanceCount;
    uint _renderingLayerMask;

    Matrix4x4[] _instances;      // kept for the fallback path and for gizmos
    Matrix4x4[] _batchScratch;

    List<Vector3> _gizmoPositions;

    static readonly int InstanceBufferId = Shader.PropertyToID("_GrassInstances");

    static bool _loggedMissingCamera;
    bool _loggedCullState;
    bool _loggedFallbackMaterial;

    public DrawMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public int InstanceCount => _instanceCount;

    public void Initialize(
        Mesh mesh,
        Material sharedMaterial,
        Matrix4x4[] instances,
        Bounds chunkBounds,
        float cullDistance,
        uint renderingLayerMask,
        Material fallbackMaterial = null)
    {
        _fallbackMaterial = fallbackMaterial;

        if (mesh == null || sharedMaterial == null || instances == null || instances.Length == 0)
        {
            Debug.LogWarning(
                $"GrassChunkRenderer on '{name}': Initialize called with mesh={(mesh != null)}, " +
                $"material={(sharedMaterial != null)}, instanceCount={(instances?.Length ?? -1)} - disabling.");
            enabled = false;
            return;
        }

        if (mesh.subMeshCount < 1)
        {
            Debug.LogError($"GrassChunkRenderer on '{name}': grass mesh '{mesh.name}' has no submeshes.");
            enabled = false;
            return;
        }

        _mesh = mesh;
        _instances = instances;
        _instanceCount = instances.Length;
        _bounds = chunkBounds;
        _cullDistance = cullDistance;
        _renderingLayerMask = renderingLayerMask;

        // One material instance per chunk. Material.SetBuffer, not
        // MaterialPropertyBlock.SetBuffer: MPB-bound StructuredBuffers do not reliably
        // reach procedurally instanced shaders under SRP, and when they do not you get
        // an all-zero matrix per instance, i.e. every blade collapsed into a degenerate
        // triangle at the world origin - which looks exactly like "nothing renders".
        _material = new Material(sharedMaterial) { name = $"{sharedMaterial.name}_{name}" };

        // The Instance ID node returns 0 unless instancing is actually in use, which
        // would collapse every blade onto instance 0's transform.
        _material.enableInstancing = true;

        _instanceBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, _instanceCount, sizeof(float) * 16);
        _instanceBuffer.SetData(instances);
        _material.SetBuffer(InstanceBufferId, _instanceBuffer);

        // Index start and base vertex must come from the submesh, not be hard-coded to
        // zero: a mesh imported as part of a larger asset can have a non-zero base
        // vertex, and zeros there draw garbage or nothing at all.
        var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        args[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = _mesh.GetIndexCount(0),
            instanceCount = (uint)_instanceCount,
            startIndex = _mesh.GetIndexStart(0),
            baseVertexIndex = _mesh.GetBaseVertex(0),
            startInstance = 0
        };

        _argsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        _argsBuffer.SetData(args);

        _batchScratch = new Matrix4x4[Mathf.Min(InstancedBatchSize, _instanceCount)];

        const int sampleCap = 300;
        int step = Mathf.Max(1, _instanceCount / sampleCap);
        _gizmoPositions = new List<Vector3>(Mathf.Min(_instanceCount, sampleCap));
        for (int i = 0; i < _instanceCount; i += step)
            _gizmoPositions.Add(instances[i].GetColumn(3));

        Debug.Log(
            $"GrassChunkRenderer '{name}': {_instanceCount} instances, mesh='{mesh.name}' " +
            $"(indexCount={args[0].indexCountPerInstance}, startIndex={args[0].startIndex}, " +
            $"baseVertex={args[0].baseVertexIndex}), material='{sharedMaterial.name}' " +
            $"(shader='{sharedMaterial.shader.name}'), bounds center={chunkBounds.center} size={chunkBounds.size}.");
    }

    void Update()
    {
        if (_mesh == null || _instanceCount == 0)
            return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            if (!_loggedMissingCamera)
            {
                _loggedMissingCamera = true;
                Debug.LogWarning(
                    "GrassChunkRenderer: Camera.main is null, so grass is never drawn. Camera.main only " +
                    "finds cameras tagged 'MainCamera', regardless of which camera is actually rendering - " +
                    "check the tag on your gameplay camera.");
            }

            return;
        }

        // Cheap chunk-level cull. Per-blade fade happens in the shader via
        // _GrassFadeParams so there is no hard edge where a chunk drops out.
        float distSqr = (_bounds.center - cam.transform.position).sqrMagnitude;
        float maxDist = _cullDistance + _bounds.extents.magnitude;
        if (distSqr > maxDist * maxDist)
        {
            LogCullOnce($"culled by DISTANCE - camera is {Mathf.Sqrt(distSqr):F1} units from bounds " +
                        $"center, threshold {maxDist:F1}.");
            return;
        }

        if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), _bounds))
        {
            LogCullOnce("culled by FRUSTUM TEST - bounds outside the camera frustum.");
            return;
        }

        LogCullOnce($"passed both culls - drawing {_instanceCount} instances in {_mode} mode.");

        if (_mode == DrawMode.Indirect)
            DrawIndirect();
        else
            DrawInstancedFallback();
    }

    RenderParams BuildRenderParams(Material material) => new RenderParams(material)
    {
        worldBounds = _bounds,
        shadowCastingMode = ShadowCastingMode.Off,
        receiveShadows = true,
        layer = gameObject.layer,
        renderingLayerMask = _renderingLayerMask,

        // Object motion vectors would be wrong here: HDRP has no previous-frame matrix
        // for a procedurally instanced draw, so TAA smears the grass. Camera-only motion
        // vectors are correct because the blades never actually move between frames -
        // the wind displacement is a vertex effect.
        motionVectorMode = MotionVectorGenerationMode.Camera,

        // BlendProbes is not valid for instanced draws and makes Unity log per frame.
        // HDRP's ambient probe still lights the grass.
        lightProbeUsage = LightProbeUsage.Off,
        reflectionProbeUsage = ReflectionProbeUsage.BlendProbes
    };

    void DrawIndirect()
    {
        RenderParams rp = BuildRenderParams(_material);
        Graphics.RenderMeshIndirect(rp, _mesh, _argsBuffer, 1, 0);
    }

    void DrawInstancedFallback()
    {
        if (_fallbackMaterial == null)
        {
            LogFallbackMaterialOnce();
            return;
        }

        RenderParams rp = BuildRenderParams(_fallbackMaterial);

        for (int start = 0; start < _instanceCount; start += InstancedBatchSize)
        {
            int count = Mathf.Min(InstancedBatchSize, _instanceCount - start);
            if (_batchScratch.Length < count)
                _batchScratch = new Matrix4x4[count];

            System.Array.Copy(_instances, start, _batchScratch, 0, count);
            Graphics.RenderMeshInstanced(rp, _mesh, 0, _batchScratch, count);
        }
    }

    void LogFallbackMaterialOnce()
    {
        if (_loggedFallbackMaterial)
            return;

        _loggedFallbackMaterial = true;
        Debug.LogWarning(
            $"GrassChunkRenderer '{name}': InstancedFallback mode needs " +
            "RenderConfig.GrassFallbackMaterial set to a plain HDRP Lit material with GPU " +
            "Instancing ticked. Reusing the grass Shader Graph material here would not be a " +
            "valid test - its procedural Setup() would still read the instance buffer.");
    }

    void LogCullOnce(string message)
    {
        if (_loggedCullState)
            return;

        _loggedCullState = true;
        Debug.Log($"GrassChunkRenderer '{name}': {message}");
    }

    void OnDrawGizmosSelected()
    {
        // Draws regardless of whether the material renders anything, so it stays useful
        // when the draw call is silently failing.
        Gizmos.color = new Color(1f, 0f, 1f, 0.8f);
        Gizmos.DrawWireCube(_bounds.center, _bounds.size);

        if (_gizmoPositions == null)
            return;

        Gizmos.color = Color.green;
        float gizmoRadius = Mathf.Max(0.05f, _bounds.extents.magnitude * 0.01f);
        for (int i = 0; i < _gizmoPositions.Count; i++)
            Gizmos.DrawSphere(_gizmoPositions[i], gizmoRadius);
    }

    void OnDestroy()
    {
        _instanceBuffer?.Release();
        _instanceBuffer = null;
        _argsBuffer?.Release();
        _argsBuffer = null;

        if (_material != null)
            Destroy(_material);
    }
}