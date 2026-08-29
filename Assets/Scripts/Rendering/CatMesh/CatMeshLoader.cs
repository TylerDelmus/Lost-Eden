using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using UnityEngine;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVector3 = AODB.Common.Structs.Vector3;
using Debug = UnityEngine.Debug;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Mesh = UnityEngine.Mesh;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

public sealed class CatMeshLoader
{
    const string VisualRootName = "Visual";
    const string CacheRootName = "CatMeshVisualCache";

    readonly ResourceDatabase _database;
    readonly CatMeshMaterialFactory _materials;
    readonly Dictionary<(int catMeshId, int restAnimId), CatMeshCacheEntry> _visualCache = new();
    readonly Dictionary<(int monsterDataId, int animSet), int> _restAnimIdCache = new();
    readonly Dictionary<(int catMeshId, int restAnimId), TaskCompletionSource<bool>> _inflightBuilds = new();
    readonly object _rdbGate = new object();
    readonly object _cacheGate = new object();

    Dictionary<string, int> _catMeshNameToId;
    Transform _cacheRoot;

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

    public bool TryGetCachedVisual(int catMeshId, int monsterDataId, int animSet, out CatMeshCacheEntry entry)
    {
        entry = null;
        int restAnimId = ResolveRestAnimId(monsterDataId, animSet);
        lock (_cacheGate)
            return _visualCache.TryGetValue((catMeshId, restAnimId), out entry) && entry?.Prototype != null;
    }

    /// <summary>
    /// Coordinates cold builds so only one coroutine constructs a given CatMesh prototype.
    /// Waiters should yield on <paramref name="waitTask"/> then apply via cache hit.
    /// </summary>
    public CatMeshBuildRole BeginVisualBuild(
        int catMeshId,
        int monsterDataId,
        int animSet,
        out int restAnimId,
        out Task<bool> waitTask)
    {
        restAnimId = ResolveRestAnimId(monsterDataId, animSet);
        waitTask = Task.FromResult(true);
        var key = (catMeshId, restAnimId);

        lock (_cacheGate)
        {
            if (_visualCache.TryGetValue(key, out CatMeshCacheEntry entry) && entry?.Prototype != null)
                return CatMeshBuildRole.CacheHit;

            if (_inflightBuilds.TryGetValue(key, out TaskCompletionSource<bool> existing))
            {
                waitTask = existing.Task;
                return CatMeshBuildRole.Waiter;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inflightBuilds[key] = tcs;
            waitTask = tcs.Task;
            return CatMeshBuildRole.Builder;
        }
    }

    public void CompleteVisualBuild(int catMeshId, int restAnimId, bool success)
    {
        var key = (catMeshId, restAnimId);
        TaskCompletionSource<bool> tcs;
        lock (_cacheGate)
        {
            if (!_inflightBuilds.TryGetValue(key, out tcs))
                return;
            _inflightBuilds.Remove(key);
        }

        tcs.TrySetResult(success);
    }

    /// <summary>
    /// Main-thread RDB fetch only. Pair with <see cref="BuildDataFromSources"/> on a worker.
    /// </summary>
    public bool TryFetchBuildSources(
        int catMeshId,
        int monsterDataId,
        int animSet,
        out RDBCatMesh catMesh,
        out int restAnimId,
        out CATAnim restAnim)
    {
        catMesh = null;
        restAnimId = 0;
        restAnim = null;

        if (catMeshId <= 0 || _database?.Rdb == null)
            return false;

        var section = Stopwatch.StartNew();
        lock (_rdbGate)
        {
            catMesh = _database.Get<RDBCatMesh>(ResourceTypeId.CatMesh, catMeshId);
            restAnimId = ResolveRestAnimId(monsterDataId, animSet);
            restAnim = restAnimId > 0 ? TryGetAnim(restAnimId) : null;
        }

        Debug.Log(
            $"[CatMeshLoader] FetchBuildSources id={catMeshId} restAnim={restAnimId} " +
            $"rdb={section.Elapsed.TotalMilliseconds:F1}ms");

        if (catMesh == null)
        {
            Debug.LogWarning($"CatMeshLoader: CatMesh {catMeshId} not found.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Main-thread RDB fetch + CPU prep of rest-pose verts (prefer async fetch + BuildDataFromSources).
    /// </summary>
    public bool TryPrepareBuildData(int catMeshId, int monsterDataId, int animSet, out CatMeshBuildData build)
    {
        build = null;
        if (!TryFetchBuildSources(catMeshId, monsterDataId, animSet, out RDBCatMesh catMesh, out int restAnimId, out CATAnim restAnim))
            return false;

        var section = Stopwatch.StartNew();
        build = BuildDataFromSources(catMesh, catMeshId, restAnimId, restAnim);
        Debug.Log(
            $"[CatMeshLoader] PrepareBuildData id={catMeshId} restAnim={restAnimId} " +
            $"submeshes={build.Submeshes.Length} snapshot={section.Elapsed.TotalMilliseconds:F1}ms");
        return build.Submeshes.Length > 0;
    }

    /// <summary>
    /// CPU-only rebuild from already-fetched RDB objects (safe for Task.Run).
    /// </summary>
    public static CatMeshBuildData BuildDataFromSources(
        RDBCatMesh catMesh,
        int catMeshId,
        int restAnimId,
        CATAnim restAnim)
    {
        int jointCount = catMesh?.Joints?.Count ?? 0;
        var build = new CatMeshBuildData
        {
            CatMeshId = catMeshId,
            RestAnimId = restAnimId,
            JointParents = CatMeshSkeleton.BuildParentIndices(catMesh),
            JointNames = new string[jointCount],
            JointScales = new float[jointCount],
            Attractors = ExtractAttractors(catMesh)
        };

        for (int i = 0; i < jointCount; i++)
        {
            RDBCatMesh.Joint joint = catMesh.Joints[i];
            build.JointNames[i] = string.IsNullOrEmpty(joint?.Name) ? $"Joint_{i}" : joint.Name;
            build.JointScales[i] = joint?.Scale > 0f ? joint.Scale : 1f;
        }

        if (restAnim != null && jointCount > 0)
        {
            CatMeshSkeleton.ExtractRestLocals(
                jointCount,
                restAnim,
                out build.RestLocalPositions,
                out build.RestLocalRotations);

            Matrix4x4[] worlds = CatMeshSkeleton.ComputeWorldMatrices(
                build.JointParents,
                build.RestLocalPositions,
                build.RestLocalRotations,
                build.JointScales);

            build.Submeshes = CatMeshSnapshot.FromRdbCatMesh(catMesh, worlds);
            build.BindPoses = CatMeshSkeleton.CreateBindPoses(worlds);
        }
        else
        {
            CatMeshBindPose bindPose = CatMeshBindPose.FromRdbCatMesh(catMesh);
            build.RestLocalPositions = new Vector3[jointCount];
            build.RestLocalRotations = new Quaternion[jointCount];
            for (int i = 0; i < jointCount; i++)
            {
                build.RestLocalPositions[i] = bindPose.GetPosition(i);
                build.RestLocalRotations[i] = bindPose.GetRotation(i);
            }

            // Flat under root (no parent-relative conversion for bind-pose path).
            for (int i = 0; i < build.JointParents.Length; i++)
                build.JointParents[i] = -1;

            build.Submeshes = CatMeshSnapshot.FromRdbCatMesh(catMesh);
            var worlds = new Matrix4x4[jointCount];
            for (int i = 0; i < jointCount; i++)
                worlds[i] = Matrix4x4.TRS(build.RestLocalPositions[i], build.RestLocalRotations[i], Vector3.one * build.JointScales[i]);
            build.BindPoses = CatMeshSkeleton.CreateBindPoses(worlds);
        }

        return build;
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

        var total = Stopwatch.StartNew();
        int restAnimId = ResolveRestAnimId(monsterDataId, animSet);

        CatMeshCacheEntry cached;
        lock (_cacheGate)
            _visualCache.TryGetValue((catMeshId, restAnimId), out cached);

        if (cached?.Prototype != null)
        {
            DestroyVisual(ref visualRoot);
            visualRoot = UnityEngine.Object.Instantiate(cached.Prototype, dynelRoot, false);
            visualRoot.name = VisualRootName;
            visualRoot.SetActive(true);
            // Prototype bones are already in idle pose; skip Play to avoid resolve+ApplyPose hitch.
            FinalizeInstance(
                visualRoot,
                monsterDataId,
                animSet,
                playIdle: false,
                ownsMeshes: false,
                out double initMs,
                out double playMs);
            if (visualRoot.TryGetComponent(out AttractorCollection attractors))
                attractors.RebuildFromChildren();
            if (playIdle && monsterDataId > 0
                && visualRoot.TryGetComponent(out CatAnimPlayer player))
            {
                player.PlayDeferred("idle");
            }

            Debug.Log(
                $"[CatMeshLoader] ApplyCatMeshVisual id={catMeshId} CACHE_HIT restAnim={restAnimId} " +
                $"init={initMs:F1}ms playIdle={playMs:F1}ms total={total.Elapsed.TotalMilliseconds:F1}ms");
            return true;
        }

        if (!TryPrepareBuildData(catMeshId, monsterDataId, animSet, out CatMeshBuildData build))
            return false;

        return ApplyBuildData(dynelRoot, build, monsterDataId, animSet, ref visualRoot, playIdle, cachePrototype: true);
    }

    public bool ApplyBuildData(
        Transform dynelRoot,
        CatMeshBuildData build,
        int monsterDataId,
        int animSet,
        ref GameObject visualRoot,
        bool playIdle,
        bool cachePrototype)
    {
        if (dynelRoot == null || build == null || build.Submeshes == null || build.Submeshes.Length == 0)
            return false;

        var total = Stopwatch.StartNew();
        var section = Stopwatch.StartNew();

        DestroyVisual(ref visualRoot);

        visualRoot = new GameObject(VisualRootName);
        visualRoot.transform.SetParent(dynelRoot, false);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;

        Transform[] bones = CatMeshSkeleton.CreateHierarchyFromBuildData(build, visualRoot.transform);
        double hierarchyMs = section.Elapsed.TotalMilliseconds;

        section.Restart();
        var createdMeshes = new List<Mesh>(build.Submeshes.Length);
        var groupRoots = new Dictionary<string, Transform>();

        for (int i = 0; i < build.Submeshes.Length; i++)
        {
            CatMeshSubmeshSource sub = build.Submeshes[i];
            if (sub.Positions == null || sub.Positions.Length == 0)
                continue;

            Mesh mesh = CatMeshFactory.CreateSkinnedMesh(sub, build.BindPoses, $"CatMesh_{build.CatMeshId}_{i}");
            if (mesh == null)
                continue;

            createdMeshes.Add(mesh);

            Transform groupRoot = GetOrCreateGroup(visualRoot.transform, groupRoots, sub.GroupName);
            var subGo = new GameObject($"Mesh_{i}");
            subGo.transform.SetParent(groupRoot, false);

            var renderer = subGo.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = FindRootBone(bones, visualRoot.transform);
            renderer.quality = SkinQuality.Bone2;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterial = _materials.Get(sub.Material);
        }
        double meshMs = section.Elapsed.TotalMilliseconds;

        if (createdMeshes.Count == 0)
        {
            DestroyVisual(ref visualRoot);
            return false;
        }

        section.Restart();
        CreateAttractors(build.Attractors, bones, visualRoot);
        double attractorMs = section.Elapsed.TotalMilliseconds;

        section.Restart();
        // Apply rest/idle pose off this frame for cold builds too — Instantiation already paid enough.
        FinalizeInstance(
            visualRoot,
            monsterDataId,
            animSet,
            playIdle: false,
            ownsMeshes: true,
            out double initMs,
            out double playMs,
            createdMeshes,
            bones);
        if (playIdle && monsterDataId > 0
            && visualRoot.TryGetComponent(out CatAnimPlayer coldPlayer))
        {
            coldPlayer.PlayDeferred("idle");
        }
        double finalizeMs = section.Elapsed.TotalMilliseconds;

        if (cachePrototype)
            StorePrototype(build.CatMeshId, build.RestAnimId, visualRoot, createdMeshes);

        Debug.Log(
            $"[CatMeshLoader] ApplyBuildData id={build.CatMeshId} restAnim={build.RestAnimId} " +
            $"meshes={createdMeshes.Count} hierarchy={hierarchyMs:F1}ms meshBuild={meshMs:F1}ms " +
            $"attractors={attractorMs:F1}ms init={initMs:F1}ms playIdle={playMs:F1}ms " +
            $"finalize={finalizeMs:F1}ms total={total.Elapsed.TotalMilliseconds:F1}ms");
        return true;
    }

    void StorePrototype(int catMeshId, int restAnimId, GameObject visualRoot, List<Mesh> meshes)
    {
        var key = (catMeshId, restAnimId);
        lock (_cacheGate)
        {
            if (_visualCache.ContainsKey(key) || visualRoot == null)
                return;
        }

        Transform cacheRoot = EnsureCacheRoot();
        GameObject prototype = UnityEngine.Object.Instantiate(visualRoot, cacheRoot, false);
        prototype.name = $"Proto_{catMeshId}_{restAnimId}";
        prototype.SetActive(false);

        if (prototype.TryGetComponent(out CatMeshVisualHolder protoHolder))
            protoHolder.SetOwnsMeshes(false);

        if (prototype.TryGetComponent(out CatAnimPlayer protoPlayer))
            protoPlayer.Paused = true;

        lock (_cacheGate)
        {
            if (!_visualCache.ContainsKey(key))
            {
                _visualCache[key] = new CatMeshCacheEntry
                {
                    CatMeshId = catMeshId,
                    RestAnimId = restAnimId,
                    Prototype = prototype,
                    SharedMeshes = meshes.ToArray()
                };
            }
            else
            {
                UnityEngine.Object.Destroy(prototype);
            }
        }

        // Original instance no longer owns meshes — cache does.
        if (visualRoot.TryGetComponent(out CatMeshVisualHolder holder))
            holder.SetOwnsMeshes(false);
    }

    void FinalizeInstance(
        GameObject visualRoot,
        int monsterDataId,
        int animSet,
        bool playIdle,
        bool ownsMeshes,
        out double initMs,
        out double playMs,
        List<Mesh> createdMeshes = null,
        Transform[] bones = null)
    {
        var sw = Stopwatch.StartNew();
        if (!visualRoot.TryGetComponent(out CatMeshVisualHolder holder))
            holder = visualRoot.AddComponent<CatMeshVisualHolder>();

        if (!visualRoot.TryGetComponent(out CatAnimPlayer player))
            player = visualRoot.AddComponent<CatAnimPlayer>();

        if (bones == null)
            bones = holder.Bones;

        if (bones == null)
        {
            var renderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length > 0)
                bones = renderers[0].bones;
        }

        player.Initialize(_database, bones, monsterDataId, animSet);
        holder.Set(createdMeshes, bones, player, ownsMeshes);
        initMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        if (playIdle && monsterDataId > 0)
            player.Play("idle");
        playMs = sw.Elapsed.TotalMilliseconds;
    }

    Transform EnsureCacheRoot()
    {
        if (_cacheRoot != null)
            return _cacheRoot;

        var go = new GameObject(CacheRootName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _cacheRoot = go.transform;
        return _cacheRoot;
    }

    int ResolveRestAnimId(int monsterDataId, int animSet)
    {
        if (monsterDataId <= 0)
            return 0;

        var key = (monsterDataId, animSet);
        lock (_rdbGate)
        {
            if (_restAnimIdCache.TryGetValue(key, out int cached))
                return cached;

            int id = TryResolveRestAnimIdUncached(monsterDataId, animSet);
            _restAnimIdCache[key] = id;
            return id;
        }
    }

    int TryResolveRestAnimIdUncached(int monsterDataId, int animSet)
    {
        if (!MonsterDataResolver.TryGetAnimIds(_database, monsterDataId, animSet, out List<int> animIds)
            || animIds == null
            || animIds.Count == 0)
            return 0;

        int bestId = 0;
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
            bestId = id;
        }

        return bestId;
    }

    CATAnim TryGetAnim(int animId)
    {
        if (animId <= 0)
            return null;
        try
        {
            return _database.Get<CATAnim>(ResourceTypeId.Anim, animId);
        }
        catch
        {
            return null;
        }
    }

    static CatMeshAttractorData[] ExtractAttractors(RDBCatMesh catMesh)
    {
        if (catMesh?.Attractors == null || catMesh.Attractors.Count == 0)
            return Array.Empty<CatMeshAttractorData>();

        var list = new List<CatMeshAttractorData>(catMesh.Attractors.Count);
        for (int i = 0; i < catMesh.Attractors.Count; i++)
        {
            RDBCatMesh.Attractor src = catMesh.Attractors[i];
            if (src == null)
                continue;

            string name = string.IsNullOrEmpty(src.Name) ? $"Attractor_{i}" : src.Name.Trim();
            if (!AttractorPlaceUtil.TryParse(name, out AttractorPlace place))
                continue;

            list.Add(new CatMeshAttractorData
            {
                Name = name,
                Place = place,
                BoneIndex = src.BoneIdx,
                LocalPosition = ToUnity(src.Position),
                LocalRotation = ToUnity(src.Rotation),
                Scale = src.Scale
            });
        }

        return list.ToArray();
    }

    static void CreateAttractors(CatMeshAttractorData[] attractors, Transform[] bones, GameObject visualRoot)
    {
        var collection = visualRoot.AddComponent<AttractorCollection>();
        if (attractors == null || attractors.Length == 0)
            return;

        Transform visualTransform = visualRoot.transform;
        for (int i = 0; i < attractors.Length; i++)
        {
            CatMeshAttractorData src = attractors[i];
            Transform parent = visualTransform;
            int jointIndex = src.BoneIndex;
            if (bones != null && jointIndex >= 0 && jointIndex < bones.Length && bones[jointIndex] != null)
                parent = bones[jointIndex];

            var go = new GameObject(src.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = src.LocalPosition;
            go.transform.localRotation = src.LocalRotation;
            go.transform.localScale = src.Scale > 0f ? Vector3.one * src.Scale : Vector3.one;

            var attractor = go.AddComponent<Attractor>();
            attractor.Place = src.Place;
            collection.Add(src.Place, attractor);
        }
    }

    static Vector3 ToUnity(AoVector3 v) => new Vector3(v.X, v.Y, v.Z);

    static Quaternion ToUnity(AoQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

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
    bool _ownsMeshes = true;

    public Transform[] Bones => _bones;
    public CatAnimPlayer Player => _player;

    public void Set(List<Mesh> meshes, Transform[] bones, CatAnimPlayer player, bool ownsMeshes = true)
    {
        if (meshes != null)
            _meshes = meshes.ToArray();
        _bones = bones;
        _player = player;
        _ownsMeshes = ownsMeshes;
    }

    public void SetOwnsMeshes(bool ownsMeshes) => _ownsMeshes = ownsMeshes;

    public void DestroyMeshes()
    {
        if (!_ownsMeshes || _meshes == null)
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
