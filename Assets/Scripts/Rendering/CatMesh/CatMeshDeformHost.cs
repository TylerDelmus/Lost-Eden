using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stock CAT meshes are skinned on the CPU, and <c>CATRender_t::RegisterVertexProcessCallback</c> lets an
/// effect see the skinned vertices before they are drawn: the Deformer moves them, Stars starType 15
/// reads them. Unity skins on the GPU, so while any deformer or reader is attached this bakes each
/// submesh after the pose is set. Deformers are applied along the skinned normals and the result is drawn
/// in place of the skinned renderer; readers are then handed each submesh as one CAT group (randy31
/// <c>100546a9</c> calls back once per group, with its vertex count and base index).
/// </summary>
[DefaultExecutionOrder(20300)]
public sealed class CatMeshDeformHost : MonoBehaviour
{
    readonly List<GfxControlDeformer> _deformers = new List<GfxControlDeformer>(2);
    readonly List<ICatVertexReader> _readers = new List<ICatVertexReader>(2);
    readonly List<SkinnedMeshRenderer> _groups = new List<SkinnedMeshRenderer>(8);
    readonly List<Vector3> _positions = new List<Vector3>(1024);
    readonly List<Vector3> _normals = new List<Vector3>(1024);
    readonly List<SkinnedMeshRenderer> _hidden = new List<SkinnedMeshRenderer>(8);
    readonly Dictionary<SkinnedMeshRenderer, Mesh> _baked = new Dictionary<SkinnedMeshRenderer, Mesh>();
    MaterialPropertyBlock _block;

    public IReadOnlyList<GfxControlDeformer> Deformers => _deformers;

    public static CatMeshDeformHost For(GameObject root)
        => root.TryGetComponent(out CatMeshDeformHost host) ? host : root.AddComponent<CatMeshDeformHost>();

    public void Add(GfxControlDeformer deformer)
    {
        if (deformer != null && !_deformers.Contains(deformer))
            _deformers.Add(deformer);
    }

    public void Remove(GfxControlDeformer deformer) => _deformers.Remove(deformer);

    public void AddReader(ICatVertexReader reader)
    {
        if (reader != null && !_readers.Contains(reader))
            _readers.Add(reader);
    }

    public void RemoveReader(ICatVertexReader reader) => _readers.Remove(reader);

    /// <summary>True when the host has a CAT mesh to bake: stock's CATRender (+0x90) is there.</summary>
    public static bool HasCatMesh(GameObject root)
    {
        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        for (int r = 0; r < renderers.Length; r++)
        {
            if (IsCatGroup(renderers[r]))
                return true;
        }
        return false;
    }

    static bool IsCatGroup(SkinnedMeshRenderer renderer)
        => renderer != null && renderer.sharedMesh != null
           && renderer.TryGetComponent(out CatMeshSourceVertices source) && source.Positions != null;

    void LateUpdate()
    {
        _deformers.RemoveAll(d => d == null || !d.IsAlive);
        _readers.RemoveAll(r => r == null || !r.IsAlive);
        if (_deformers.Count == 0)
            Restore();
        if (_deformers.Count == 0 && _readers.Count == 0)
            return;

        _block ??= new MaterialPropertyBlock();
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(false);
        _groups.Clear();
        int total = 0;
        for (int r = 0; r < renderers.Length; r++)
        {
            if (!IsCatGroup(renderers[r]))
                continue;
            _groups.Add(renderers[r]);
            total += renderers[r].sharedMesh.vertexCount;
        }

        int baseIndex = 0;
        for (int r = 0; r < _groups.Count; r++)
        {
            SkinnedMeshRenderer renderer = _groups[r];
            CatMeshSourceVertices source = renderer.GetComponent<CatMeshSourceVertices>();

            if (!_baked.TryGetValue(renderer, out Mesh baked) || baked == null)
            {
                baked = new Mesh { name = "CatMeshDeformed" };
                baked.MarkDynamic();
                _baked[renderer] = baked;
            }

            renderer.BakeMesh(baked, true);
            baked.GetVertices(_positions);
            baked.GetNormals(_normals);
            Transform t = renderer.transform;
            Matrix4x4 matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            if (_deformers.Count == 0)
            {
                Read(_positions.Count, baseIndex, total, matrix);
                baseIndex += renderer.sharedMesh.vertexCount;
                continue;
            }

            int count = Mathf.Min(Mathf.Min(_positions.Count, _normals.Count), source.Positions.Length);
            for (int v = 0; v < count; v++)
            {
                Vector3 b = source.Positions[v];
                float weight = 0f;
                for (int d = 0; d < _deformers.Count; d++)
                    weight += _deformers[d].WobbleWeight(b.x, b.y, b.z);
                if (weight != 0f)
                    _positions[v] += _normals[v] * weight;
            }
            baked.SetVertices(_positions);
            baked.RecalculateBounds();

            if (!renderer.forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
                _hidden.Add(renderer);
            }

            renderer.GetPropertyBlock(_block);
            Material[] materials = renderer.sharedMaterials;
            for (int s = 0; s < baked.subMeshCount && s < materials.Length; s++)
            {
                Graphics.DrawMesh(
                    baked, matrix, materials[s], renderer.gameObject.layer, null, s, _block,
                    renderer.shadowCastingMode, renderer.receiveShadows);
            }

            Read(_positions.Count, baseIndex, total, matrix);
            baseIndex += renderer.sharedMesh.vertexCount;
        }
    }

    void Read(int count, int baseIndex, int total, Matrix4x4 toWorld)
    {
        count = Mathf.Min(count, _normals.Count);
        for (int i = 0; i < _readers.Count; i++)
            _readers[i].ReadGroup(count, baseIndex, total, _positions, _normals, toWorld);
    }

    void Restore()
    {
        for (int i = 0; i < _hidden.Count; i++)
        {
            if (_hidden[i] != null)
                _hidden[i].forceRenderingOff = false;
        }
        _hidden.Clear();
    }

    void OnDisable() => Restore();

    void OnDestroy()
    {
        Restore();
        foreach (Mesh mesh in _baked.Values)
        {
            if (mesh != null)
                Destroy(mesh);
        }
        _baked.Clear();
    }
}
