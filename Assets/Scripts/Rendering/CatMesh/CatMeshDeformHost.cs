using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stock CAT meshes are skinned on the CPU, and <c>CATRender_t::RegisterVertexProcessCallback</c> lets an
/// effect move the skinned vertices before they are drawn (the Deformer). Unity skins on the GPU, so
/// while any deformer is attached this bakes each submesh after the pose is set, applies the
/// deformers along the skinned normals and draws the result in place of the skinned renderer.
/// </summary>
[DefaultExecutionOrder(20300)]
public sealed class CatMeshDeformHost : MonoBehaviour
{
    readonly List<GfxControlDeformer> _deformers = new List<GfxControlDeformer>(2);
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

    void LateUpdate()
    {
        _deformers.RemoveAll(d => d == null || !d.IsAlive);
        if (_deformers.Count == 0)
        {
            Restore();
            return;
        }

        _block ??= new MaterialPropertyBlock();
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(false);
        for (int r = 0; r < renderers.Length; r++)
        {
            SkinnedMeshRenderer renderer = renderers[r];
            if (renderer == null || renderer.sharedMesh == null
                || !renderer.TryGetComponent(out CatMeshSourceVertices source) || source.Positions == null)
                continue;

            if (!_baked.TryGetValue(renderer, out Mesh baked) || baked == null)
            {
                baked = new Mesh { name = "CatMeshDeformed" };
                baked.MarkDynamic();
                _baked[renderer] = baked;
            }

            renderer.BakeMesh(baked, true);
            baked.GetVertices(_positions);
            baked.GetNormals(_normals);
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
            Transform t = renderer.transform;
            Matrix4x4 matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Material[] materials = renderer.sharedMaterials;
            for (int s = 0; s < baked.subMeshCount && s < materials.Length; s++)
            {
                Graphics.DrawMesh(
                    baked, matrix, materials[s], renderer.gameObject.layer, null, s, _block,
                    renderer.shadowCastingMode, renderer.receiveShadows);
            }
        }
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
