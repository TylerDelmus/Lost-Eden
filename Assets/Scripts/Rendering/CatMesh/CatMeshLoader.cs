using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using UnityEngine;

public sealed class CatMeshLoader
{
    const string VisualRootName = "Visual";

    readonly ResourceDatabase _database;
    readonly CatMeshMaterialFactory _materials;

    public CatMeshLoader(ResourceDatabase database, CatMeshMaterialFactory materials)
    {
        _database = database;
        _materials = materials ?? new CatMeshMaterialFactory(new AbiffMaterialFactory(database));
    }

    public void ApplyMonsterVisual(
        Transform dynelRoot,
        int monsterDataId,
        int animSet,
        ref GameObject visualRoot,
        ref int loadedMonsterDataId)
    {
        if (dynelRoot == null)
            return;

        if (monsterDataId == loadedMonsterDataId)
        {
            if (visualRoot != null && visualRoot.TryGetComponent(out CatAnimPlayer existingPlayer))
                existingPlayer.SetAnimSet(animSet);
            return;
        }

        if (monsterDataId <= 0)
        {
            DestroyVisual(ref visualRoot);
            loadedMonsterDataId = 0;
            return;
        }

        if (!MonsterDataResolver.TryResolveBodyCatMeshId(_database, monsterDataId, out int catMeshId))
        {
            Debug.LogWarning($"CatMeshLoader: MonsterData {monsterDataId} has no BodyCatMesh stat.");
            return;
        }

        if (!ApplyCatMeshVisual(dynelRoot, catMeshId, monsterDataId, animSet, ref visualRoot))
            return;

        loadedMonsterDataId = monsterDataId;
    }

    public bool ApplyCatMeshVisual(
        Transform dynelRoot,
        int catMeshId,
        int monsterDataId,
        int animSet,
        ref GameObject visualRoot,
        bool playIdle = true)
    {
        if (dynelRoot == null)
            return false;

        if (catMeshId <= 0)
        {
            DestroyVisual(ref visualRoot);
            return false;
        }

        if (_database?.Rdb == null)
        {
            Debug.LogWarning("CatMeshLoader: ResourceDatabase is not initialized.");
            return false;
        }

        RDBCatMesh catMesh = _database.Get<RDBCatMesh>(ResourceTypeId.CatMesh, catMeshId);
        if (catMesh == null)
        {
            Debug.LogWarning($"CatMeshLoader: CatMesh {catMeshId} not found.");
            return false;
        }

        DestroyVisual(ref visualRoot);

        visualRoot = new GameObject(VisualRootName);
        visualRoot.transform.SetParent(dynelRoot, false);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;

        CATAnim restAnim = TryLoadRestPoseAnim(monsterDataId, animSet);
        Transform[] bones;
        CatMeshSubmeshSource[] submeshes;

        if (restAnim != null)
        {
            // CirExport path: rest locals = first keyframe, verts from RelToJoint.
            bones = CatMeshSkeleton.CreateHierarchy(catMesh, visualRoot.transform);
            CatMeshSkeleton.ApplyFirstFramePose(bones, restAnim);
            submeshes = CatMeshSnapshot.FromRdbCatMesh(catMesh, bones, visualRoot.transform);
        }
        else
        {
            CatMeshBindPose bindPose = CatMeshBindPose.FromRdbCatMesh(catMesh);
            bones = CatMeshSkeleton.Create(catMesh, bindPose, visualRoot.transform);
            submeshes = CatMeshSnapshot.FromRdbCatMesh(catMesh);
        }

        if (submeshes.Length == 0)
        {
            Debug.LogWarning($"CatMeshLoader: CatMesh {catMeshId} has no renderable meshes.");
            DestroyVisual(ref visualRoot);
            return false;
        }

        Matrix4x4[] bindPoses = CatMeshSkeleton.CreateBindPoses(bones, visualRoot.transform);
        var createdMeshes = new List<Mesh>(submeshes.Length);
        var groupRoots = new Dictionary<string, Transform>();

        for (int i = 0; i < submeshes.Length; i++)
        {
            CatMeshSubmeshSource sub = submeshes[i];
            if (sub.Positions == null || sub.Positions.Length == 0)
                continue;

            Mesh mesh = CatMeshFactory.CreateSkinnedMesh(sub, bindPoses, $"CatMesh_{catMeshId}_{i}");
            if (mesh == null)
                continue;

            createdMeshes.Add(mesh);

            Transform groupRoot = GetOrCreateGroup(visualRoot.transform, groupRoots, sub.GroupName);
            var subGo = new GameObject($"Mesh_{i}");
            subGo.transform.SetParent(groupRoot, false);
            subGo.transform.localPosition = Vector3.zero;
            subGo.transform.localRotation = Quaternion.identity;
            subGo.transform.localScale = Vector3.one;

            var renderer = subGo.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = FindRootBone(bones, visualRoot.transform);
            renderer.quality = SkinQuality.Bone2;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterial = _materials.Get(sub.Material);
        }

        if (createdMeshes.Count == 0)
        {
            DestroyVisual(ref visualRoot);
            return false;
        }

        var player = visualRoot.AddComponent<CatAnimPlayer>();
        player.Initialize(_database, bones, monsterDataId, animSet);

        var holder = visualRoot.AddComponent<CatMeshVisualHolder>();
        holder.Set(createdMeshes, bones, player);

        if (playIdle && monsterDataId > 0)
            player.Play("idle");

        return true;
    }

    CATAnim TryLoadRestPoseAnim(int monsterDataId, int animSet)
    {
        if (monsterDataId <= 0)
            return null;

        if (!MonsterDataResolver.TryGetAnimIds(_database, monsterDataId, animSet, out List<int> animIds)
            || animIds == null
            || animIds.Count == 0)
            return null;

        CATAnim best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < animIds.Count; i++)
        {
            int id = animIds[i];
            if (id <= 0)
                continue;

            CATAnim candidate;
            try
            {
                candidate = _database.Get<CATAnim>(ResourceTypeId.Anim, id);
            }
            catch
            {
                continue;
            }

            if (candidate?.Animation.BoneData == null || candidate.Animation.BoneData.Count == 0)
                continue;

            int score = ScoreRestPoseCandidate(candidate.Name);
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    static int ScoreRestPoseCandidate(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return 0;

        string name = rawName.Trim().Trim('\0').ToLowerInvariant();
        if (name.Contains("noanim") || name.Contains("no-anim") || name.Contains("no_anim"))
            return 100;
        if (name.Contains("idle-stand") || name.Contains("idle_stand"))
            return 80;
        if (name.Contains("idle"))
            return 40;
        return 10;
    }

    static Transform FindRootBone(Transform[] bones, Transform visualRoot)
    {
        if (bones == null || bones.Length == 0)
            return visualRoot;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null && bones[i].parent == visualRoot)
                return bones[i];
        }

        return bones[0] != null ? bones[0] : visualRoot;
    }

    static Transform GetOrCreateGroup(Transform visualRoot, Dictionary<string, Transform> groups, string groupName)
    {
        string key = string.IsNullOrEmpty(groupName) ? "Meshes" : groupName;
        if (groups.TryGetValue(key, out Transform existing))
            return existing;

        var go = new GameObject(key);
        go.transform.SetParent(visualRoot, false);
        groups[key] = go.transform;
        return go.transform;
    }

    static void DestroyVisual(ref GameObject visualRoot)
    {
        if (visualRoot == null)
            return;

        if (visualRoot.TryGetComponent(out CatMeshVisualHolder holder))
            holder.DestroyMeshes();

        UnityEngine.Object.Destroy(visualRoot);
        visualRoot = null;
    }
}

public sealed class CatMeshVisualHolder : MonoBehaviour
{
    Mesh[] _meshes;
    Transform[] _bones;
    CatAnimPlayer _player;

    public Transform[] Bones => _bones;
    public CatAnimPlayer Player => _player;

    public void Set(List<Mesh> meshes, Transform[] bones, CatAnimPlayer player)
    {
        _meshes = meshes?.ToArray();
        _bones = bones;
        _player = player;
    }

    public void DestroyMeshes()
    {
        if (_meshes == null)
            return;

        for (int i = 0; i < _meshes.Length; i++)
        {
            if (_meshes[i] != null)
                Destroy(_meshes[i]);
        }

        _meshes = null;
    }

    void OnDestroy() => DestroyMeshes();
}
