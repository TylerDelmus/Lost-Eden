using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class AbiffLoader
{
    readonly ResourceDatabase _database;
    readonly AbiffMaterialFactory _materials;
    readonly AoImageTextureCache _images;

    public AbiffLoader(
        ResourceDatabase database,
        AbiffMaterialFactory materials,
        AoImageTextureCache images = null)
    {
        _database = database;
        _materials = materials ?? new AbiffMaterialFactory(database);
        _images = images;
    }

    public bool TryCreateVisual(int meshId, Transform parent, out GameObject visualRoot)
        => TryCreateVisual(meshId, parent, overrideTextureId: 0, skyUnlit: false, out visualRoot);

    public bool TryCreateVisual(
        int meshId,
        Transform parent,
        int overrideTextureId,
        out GameObject visualRoot)
        => TryCreateVisual(meshId, parent, overrideTextureId, skyUnlit: false, skyIntensity: 1f, out visualRoot);

    public bool TryCreateSkyVisual(int meshId, Transform parent, float intensity, out GameObject visualRoot)
        => TryCreateVisual(meshId, parent, overrideTextureId: 0, skyUnlit: true, skyIntensity: intensity, out visualRoot);

    public bool TryCreateSkyVisual(int meshId, Transform parent, out GameObject visualRoot)
        => TryCreateSkyVisual(meshId, parent, 1f, out visualRoot);

    public bool TryCreateVisual(
        int meshId,
        Transform parent,
        int overrideTextureId,
        bool skyUnlit,
        out GameObject visualRoot)
        => TryCreateVisual(meshId, parent, overrideTextureId, skyUnlit, skyIntensity: 1f, out visualRoot);

    public bool TryCreateVisual(
        int meshId,
        Transform parent,
        int overrideTextureId,
        bool skyUnlit,
        float skyIntensity,
        out GameObject visualRoot)
    {
        visualRoot = null;
        if (parent == null || meshId <= 0)
            return false;

        if (_database?.Rdb == null)
        {
            Debug.LogWarning("AbiffLoader: ResourceDatabase is not initialized.");
            return false;
        }

        RDBMesh rdbMesh = _database.Get<RDBMesh>(meshId);
        if (rdbMesh?.SubMeshes == null || rdbMesh.SubMeshes.Count == 0)
        {
            Debug.LogWarning($"AbiffLoader: RDBMesh {meshId} not found or empty.");
            return false;
        }

        AbiffSubmeshSource[] submeshes = AbiffMeshSnapshot.FromRdbMesh(rdbMesh);
        if (submeshes == null || submeshes.Length == 0)
        {
            Debug.LogWarning($"AbiffLoader: RDBMesh {meshId} has no renderable submeshes.");
            return false;
        }

        Texture2D overrideDiffuse = null;
        if (overrideTextureId > 0 && _images != null)
            overrideDiffuse = _images.GetAoTexture(overrideTextureId);

        visualRoot = new GameObject($"Abiff_{meshId}");
        visualRoot.transform.SetParent(parent, false);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;

        for (int i = 0; i < submeshes.Length; i++)
        {
            AbiffSubmeshSource sub = submeshes[i];
            AbiffMeshData baked = AbiffMeshFactory.Bake(sub);
            Mesh mesh = AbiffMeshFactory.CreateUnityMesh(baked, $"Abiff_{meshId}_{i}");
            if (mesh == null)
                continue;

            var subGo = new GameObject($"Sub_{i}");
            subGo.transform.SetParent(visualRoot.transform, false);
            subGo.transform.localPosition = sub.BasePosition;
            subGo.transform.localRotation = sub.BaseRotation;
            subGo.transform.localScale = Vector3.one;

            var filter = subGo.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = subGo.AddComponent<MeshRenderer>();
            Material shared = skyUnlit
                ? _materials.GetSkyUnlit(sub.Material)
                : _materials.Get(sub.Material);

            Material material = shared;
            if (skyUnlit && Mathf.Abs(skyIntensity - 1f) > 0.001f)
            {
                material = new Material(shared);
                _materials.ApplySkyIntensity(material, skyIntensity);
            }

            if (overrideDiffuse != null)
            {
                Material instance = new Material(material);
                if (instance.HasProperty("_UnlitColorMap"))
                    instance.SetTexture("_UnlitColorMap", overrideDiffuse);
                else if (instance.HasProperty("_BaseColorMap"))
                    instance.SetTexture("_BaseColorMap", overrideDiffuse);
                else if (instance.HasProperty("_MainTex"))
                    instance.SetTexture("_MainTex", overrideDiffuse);
                renderer.sharedMaterial = instance;
            }
            else
            {
                renderer.sharedMaterial = material;
            }

            TryAttachUvAnimator(subGo, sub);
        }

        return true;
    }

    static void TryAttachUvAnimator(GameObject subGo, AbiffSubmeshSource sub)
    {
        if (sub?.UvKeys == null || sub.UvKeys.Length < 2)
            return;

        var animator = subGo.AddComponent<AbiffUvAnimator>();
        animator.Init(sub.UvKeys, sub.UvLoop, sub.UvDuration);
    }
}
