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
        Material _litFade;

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
    }

    public sealed class Model
    {
        public string Name;
        public int MeshId;
        public Part[] Parts;
    }

    readonly ResourceDatabase _database;
    readonly Dictionary<string, Model> _cache = new Dictionary<string, Model>(System.StringComparer.OrdinalIgnoreCase);
    AoTweakMeshNames _names;
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

        _names ??= new AoTweakMeshNames(_database);
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
            });
        }

        if (parts.Count == 0)
            return null;
        return new Model { Name = fileName, MeshId = meshId, Parts = parts.ToArray() };
    }
}
