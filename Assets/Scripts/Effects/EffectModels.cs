using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// ABIFF models for the effect controls that draw one (EffectMesh, MParticle), loaded by file name the
/// way stock's <c>InstanceManager_t::GetTypeInstance(1010001, name)</c> does, and cached for the session.
/// Each part is one ABIFF submesh with its base transform, material and diffuse texture, built by the same
/// snapshot, bake and material code as <see cref="AbiffLoader"/>, but drawn through
/// <see cref="EffectBillboardBatch"/> instead of as scene objects.
/// </summary>
public sealed class EffectModels
{
    public sealed class Part
    {
        public Mesh Mesh;
        public Matrix4x4 Local;
        public AbiffMaterialDesc Desc;
        public Texture2D Texture;
        public Material Lit;

        /// <summary>The part's node's UV track (null without one), for EffectMesh flag 0x1000.</summary>
        public StockUvTrack Uv;
        Material _litFade;
        Material _litAdditive;

        /// <summary>
        /// The lit material as a transparent alpha blend, for a model faded below full opacity (stock's
        /// <c>VisualMesh_t::SetTransparency</c>).
        /// </summary>
        public Material LitFade
        {
            get
            {
                if (_litFade != null || Lit == null)
                    return _litFade;
                _litFade = new Material(Lit) { name = Lit.name + "_Fade" };
                HDMaterial.SetSurfaceType(_litFade, transparent: true);
                if (_litFade.HasProperty("_BlendMode"))
                    _litFade.SetFloat("_BlendMode", 0f);
                if (_litFade.HasProperty("_ZWrite"))
                    _litFade.SetFloat("_ZWrite", 0f);
                HDMaterial.ValidateMaterial(_litFade);
                return _litFade;
            }
        }

        /// <summary>
        /// The lit material as an additive blend, for rendering effect 4. Stock's effect object
        /// (factory <c>DisplaySystem 1006c62e</c>, its arm at <c>1006c912</c>) sets
        /// <c>D3DRS_LIGHTING = 1</c> alongside <c>SRCBLEND = SRCALPHA</c>, <c>DESTBLEND = ONE</c>,
        /// <c>ZWRITEENABLE = 0</c> and <c>CULLMODE = NONE</c> — so it is lit <i>and</i> additive, which
        /// is the one combination the other two variants here do not cover.
        /// </summary>
        public Material LitAdditive
        {
            get
            {
                if (_litAdditive != null || Lit == null)
                    return _litAdditive;
                _litAdditive = new Material(Lit) { name = Lit.name + "_Add" };
                HDMaterial.SetSurfaceType(_litAdditive, transparent: true);
                // HDRP's _BlendMode: 0 alpha (what LitFade takes), 1 additive.
                if (_litAdditive.HasProperty("_BlendMode"))
                    _litAdditive.SetFloat("_BlendMode", 1f);
                if (_litAdditive.HasProperty("_ZWrite"))
                    _litAdditive.SetFloat("_ZWrite", 0f);
                if (_litAdditive.HasProperty("_CullMode"))
                    _litAdditive.SetFloat("_CullMode", 0f);
                if (_litAdditive.HasProperty("_DoubleSidedEnable"))
                    _litAdditive.SetFloat("_DoubleSidedEnable", 1f);
                HDMaterial.ValidateMaterial(_litAdditive);
                return _litAdditive;
            }
        }
    }

    public sealed class Model
    {
        public string Name;
        public int MeshId;
        public Part[] Parts;

        /// <summary>
        /// <c>GetAnimationTreeTotalTime</c> (randy31 <c>10045728</c>): the longest animation in the tree. The
        /// port counts the parts' UV-animated nodes; every model an EffectMesh animates (0x1000) has only those.
        /// </summary>
        public float AnimationTotalTime;
    }

    readonly ResourceDatabase _database;
    readonly Dictionary<string, Model> _cache = new Dictionary<string, Model>(System.StringComparer.OrdinalIgnoreCase);
    AbiffMeshNames _names;
    AbiffMaterialFactory _materials;

    public EffectModels(ResourceDatabase database)
    {
        _database = database;
    }

    /// <summary>The model for <paramref name="fileName"/>, or null when it cannot be found or has no parts.</summary>
    public Model Get(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;
        if (_cache.TryGetValue(fileName, out Model cached))
            return cached;

        Model model = Load(fileName);
        _cache[fileName] = model;
        if (model == null)
            Debug.LogWarning($"[Effects] effect model '{fileName}' not found.");
        return model;
    }

    Model Load(string fileName)
    {
        if (_database?.Rdb == null)
            return null;

        _names ??= new AbiffMeshNames(_database);
        _materials ??= new AbiffMaterialFactory(_database);
        if (!_names.TryResolve(fileName, out int meshId) || meshId <= 0)
            return null;

        RDBMesh rdbMesh;
        try
        {
            rdbMesh = _database.Get<RDBMesh>(meshId);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Effects] effect model '{fileName}' ({meshId}) failed to load: {ex.Message}");
            return null;
        }

        AbiffSubmeshSource[] submeshes = AbiffMeshSnapshot.FromRdbMesh(rdbMesh);
        if (submeshes == null || submeshes.Length == 0)
            return null;

        var parts = new List<Part>(submeshes.Length);
        for (int i = 0; i < submeshes.Length; i++)
        {
            AbiffSubmeshSource sub = submeshes[i];
            Mesh mesh = AbiffMeshFactory.CreateUnityMesh(AbiffMeshFactory.Bake(sub), $"EffectModel_{meshId}_{i}");
            if (mesh == null)
                continue;

            parts.Add(new Part
            {
                Mesh = mesh,
                Local = Matrix4x4.TRS(sub.BasePosition, sub.BaseRotation, Vector3.one),
                Desc = sub.Material,
                Texture = _materials.GetTexture(sub.Material.DiffuseTextureId),
                Lit = _materials.Get(sub.Material),
                Uv = UvTrack(sub),
            });
        }

        if (parts.Count == 0)
            return null;
        float total = 0f;
        foreach (Part part in parts)
        {
            if (part.Uv != null && total < part.Uv.TotalTime)
                total = part.Uv.TotalTime;
        }
        return new Model { Name = fileName, MeshId = meshId, Parts = parts.ToArray(), AnimationTotalTime = total };
    }

    static StockUvTrack UvTrack(AbiffSubmeshSource sub)
    {
        AbiffUvKey[] keys = sub.UvKeys;
        if (keys == null || keys.Length == 0)
            return null;
        int n = keys.Length;
        var times = new float[n];
        var tileU = new float[n];
        var tileV = new float[n];
        var offU = new float[n];
        var offV = new float[n];
        var lerp = new bool[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = keys[i].Time;
            tileU[i] = keys[i].Tiling.x;
            tileV[i] = keys[i].Tiling.y;
            offU[i] = keys[i].Offset.x;
            offV[i] = keys[i].Offset.y;
            lerp[i] = keys[i].Lerp;
        }
        return new StockUvTrack(times, tileU, tileV, offU, offV, lerp, sub.UvLoop, sub.UvDuration);
    }
}
