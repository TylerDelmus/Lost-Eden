using System;
using System.Collections.Generic;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using UnityEngine;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVector3 = AODB.Common.Structs.Vector3;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Mesh = UnityEngine.Mesh;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

public sealed class CatMeshLoader
{
    const string VisualRootName = "Visual";

    readonly ResourceDatabase _database;
    readonly CatMeshMaterialFactory _materials;
    Dictionary<string, int> _catMeshNameToId;

    public CatMeshLoader(ResourceDatabase database, CatMeshMaterialFactory materials)
    {
        _database = database;
        _materials = materials ?? new CatMeshMaterialFactory(new AbiffMaterialFactory(database));
    }

    public bool TryResolveAppearanceCatMeshId(
        Breed breed,
        Gender gender,
        Fatness fatness,
        bool robe,
        out int catMeshId)
    {
        catMeshId = 0;
        if (_database?.Rdb == null)
            return false;

        if (!TryBuildAppearanceName(breed, gender, fatness, robe, out string name))
            return false;

        EnsureCatMeshNameCache();
        return _catMeshNameToId != null
            && _catMeshNameToId.TryGetValue(name, out catMeshId)
            && catMeshId > 0;
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

        CreateAttractors(catMesh, bones, visualRoot);

        if (playIdle && monsterDataId > 0)
            player.Play("idle");

        return true;
    }

    static void CreateAttractors(RDBCatMesh catMesh, Transform[] bones, GameObject visualRoot)
    {
        var collection = visualRoot.AddComponent<AttractorCollection>();
        if (catMesh?.Attractors == null || catMesh.Attractors.Count == 0)
            return;

        Transform visualTransform = visualRoot.transform;

        for (int i = 0; i < catMesh.Attractors.Count; i++)
        {
            RDBCatMesh.Attractor src = catMesh.Attractors[i];
            if (src == null)
                continue;

            string name = string.IsNullOrEmpty(src.Name) ? $"Attractor_{i}" : src.Name.Trim();
            if (!AttractorPlaceUtil.TryParse(name, out AttractorPlace place))
            {
                Debug.LogWarning($"CatMeshLoader: Unknown attractor name '{name}', skipping.");
                continue;
            }

            Transform parent = visualTransform;
            int jointIndex = src.BoneIdx;
            if (bones != null && jointIndex >= 0 && jointIndex < bones.Length && bones[jointIndex] != null)
                parent = bones[jointIndex];

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ToUnity(src.Position);
            go.transform.localRotation = ToUnity(src.Rotation);

            float scale = src.Scale;
            go.transform.localScale = scale > 0f ? Vector3.one * scale : Vector3.one;

            var attractor = go.AddComponent<Attractor>();
            collection.Add(place, attractor);
        }
    }

    static Vector3 ToUnity(AoVector3 v) => new Vector3(v.X, v.Y, v.Z);

    static Quaternion ToUnity(AoQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

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

    static bool TryBuildAppearanceName(
        Breed breed,
        Gender gender,
        Fatness fatness,
        bool robe,
        out string name)
    {
        name = null;

        string breedName = breed switch
        {
            Breed.Solitus => "solitus",
            Breed.Opifex => "opifex",
            Breed.Nanomage => "nanomage",
            Breed.Atrox => "athrox",
            _ => null
        };

        string genderName = gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            Gender.Uni => "male",
            _ => null
        };

        if (breedName == null || genderName == null)
            return false;

        string fatnessSuffix = fatness switch
        {
            Fatness.Thin => "_thin",
            Fatness.Fat => "_fat",
            _ => string.Empty
        };

        string robeSuffix = robe ? "_robe" : string.Empty;
        name = $"{breedName}_{genderName}{fatnessSuffix}{robeSuffix}.cir";
        return true;
    }

    void EnsureCatMeshNameCache()
    {
        if (_catMeshNameToId != null)
            return;

        _catMeshNameToId = new Dictionary<string, int>(StringComparer.Ordinal);
        if (_database?.Rdb == null)
            return;

        try
        {
            InfoObject info = _database.Get<InfoObject>(1);
            if (info?.Types == null)
                return;

            if (!info.Types.TryGetValue(ResourceTypeId.CatMesh, out Dictionary<int, string> names) || names == null)
                return;

            foreach (KeyValuePair<int, string> pair in names)
            {
                string normalized = NormalizeCatMeshName(pair.Value);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (!_catMeshNameToId.ContainsKey(normalized))
                    _catMeshNameToId[normalized] = pair.Key;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CatMeshLoader: Failed to load InfoObject CatMesh names ({ex.Message}).");
            _catMeshNameToId = new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    static string NormalizeCatMeshName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return name.Trim().Trim('\0').ToLowerInvariant();
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
