using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using AOSharp.Common.GameData;
using Reflex.Attributes;
using UnityEngine;
using AoMesh = SmokeLounge.AOtomation.Messaging.GameData.Mesh;
using AoTextureSlot = SmokeLounge.AOtomation.Messaging.GameData.Texture;
using Debug = UnityEngine.Debug;

/// <summary>
/// Owns CatMesh body visuals, skin textures, and attached equipment meshes.
/// Works on a Character (via Dynel.Stats) or standalone with its own StatCollection.
/// </summary>
public class VisualDynel : MonoBehaviour
{
    const int VisualFlagRightShoulder = 0x1;
    const int VisualFlagLeftShoulder = 0x2;
    const int VisualFlagShowHelmet = 0x4;

    [Inject] CatMeshLoader _catMeshLoader;
    [Inject] AbiffLoader _abiffLoader;
    [Inject] ResourceDatabase _resourceDatabase;
    [Inject] SkinTextureResolver _skinTextures;
    [Inject] AoImageTextureCache _imageTextures;

    Dynel _dynel;
    StatCollection _ownedStats;
    GameObject _visualRoot;
    int _loadedCatMeshId;
    int _loadedMonsterDataId;
    bool _robe;

    int _loadGeneration;
    Coroutine _loadRoutine;

    static readonly Dictionary<(int skinId, int armorId), Texture2D> SharedBakeCache = new();
    static int _applyFrame = -1;
    static int _appliesThisFrame;
    const int MaxMainThreadAppliesPerFrame = 1;

    static readonly BodyPart[] BodyParts =
    {
        BodyPart.Hands,
        BodyPart.Arms,
        BodyPart.Body,
        BodyPart.Legs,
        BodyPart.Feet
    };

    readonly Dictionary<BodyPart, int> _slotTextures = new Dictionary<BodyPart, int>();
    readonly Dictionary<BodyPart, Texture2D> _catMeshDiffuseByPart = new Dictionary<BodyPart, Texture2D>();
    readonly Dictionary<BodyPart, Texture2D> _bakedByPart = new Dictionary<BodyPart, Texture2D>();
    readonly List<AoMesh> _attachedMeshes = new List<AoMesh>();
    readonly List<GameObject> _attachedMeshRoots = new List<GameObject>();
    readonly List<Texture2D> _bakedSlotTextures = new List<Texture2D>();
    readonly List<Material> _bakedSlotMaterials = new List<Material>();

    public GameObject VisualRoot => _visualRoot;
    public int LoadedCatMeshId => _loadedCatMeshId;
    public int LoadedMonsterDataId => _loadedMonsterDataId;
    public IReadOnlyDictionary<BodyPart, int> ArmorSlotTextureIds => _slotTextures;

    float _cachedMeshHeight;
    bool _meshHeightValid;

    /// <summary>
    /// AO GetIndicatorPosition: head attractor + 0.5 Y (clamp local Y to 1.5),
    /// else plain mesh height + 0.3, else (0, 2, 0).
    /// </summary>
    public bool TryGetIndicatorPosition(Transform dynelTransform, out UnityEngine.Vector3 worldPos)
    {
        if (dynelTransform == null)
        {
            worldPos = default;
            return false;
        }

        if (TryGetAttractor(AttractorPlace.Head, out Attractor head) && head != null)
        {
            worldPos = head.transform.position + UnityEngine.Vector3.up * 0.5f;
            float localY = worldPos.y - dynelTransform.position.y;
            if (localY < 1.5f)
                worldPos.y = dynelTransform.position.y + 1.5f;
            return true;
        }

        if (_visualRoot != null)
        {
            float height = GetCachedMeshHeight();
            worldPos = dynelTransform.TransformPoint(new UnityEngine.Vector3(0f, height + 0.3f, 0f));
            return true;
        }

        worldPos = dynelTransform.position + UnityEngine.Vector3.up * 2f;
        return true;
    }

    void InvalidateMeshHeightCache()
    {
        _meshHeightValid = false;
    }

    float GetCachedMeshHeight()
    {
        if (_meshHeightValid)
            return _cachedMeshHeight;

        _cachedMeshHeight = ComputeMeshHeight();
        _meshHeightValid = true;
        return _cachedMeshHeight;
    }

    float ComputeMeshHeight()
    {
        if (_visualRoot == null)
            return 2f;

        Transform root = _visualRoot.transform;
        Renderer[] renderers = _visualRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return 2f;

        float maxLocalY = 0f;
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            Bounds lb = r.localBounds;
            UnityEngine.Vector3 localMax = root.InverseTransformPoint(r.transform.TransformPoint(lb.max));
            if (!any || localMax.y > maxLocalY)
            {
                maxLocalY = localMax.y;
                any = true;
            }
        }

        return any ? UnityEngine.Mathf.Max(0f, maxLocalY) : 2f;
    }

    public bool TryGetRenderPose(out UnityEngine.Vector3 worldPos, out UnityEngine.Quaternion worldRot)
    {
        if (_visualRoot == null)
        {
            worldPos = default;
            worldRot = default;
            return false;
        }

        Transform t = _visualRoot.transform;
        worldPos = t.position;
        worldRot = t.rotation;
        return true;
    }

    public void SetRenderWorldPose(UnityEngine.Vector3 worldPos, UnityEngine.Quaternion worldRot)
    {
        if (_visualRoot == null)
            return;

        _visualRoot.transform.SetPositionAndRotation(worldPos, worldRot);
    }

    public void ClearRenderOffset()
    {
        if (_visualRoot == null)
            return;

        Transform t = _visualRoot.transform;
        t.localPosition = UnityEngine.Vector3.zero;
        t.localRotation = UnityEngine.Quaternion.identity;
    }

    public bool HasRenderOffset(float positionEpsilon = 0.0001f, float yawEpsilonDegrees = 0.01f)
    {
        if (_visualRoot == null)
            return false;

        Transform t = _visualRoot.transform;
        if (t.localPosition.sqrMagnitude > positionEpsilon * positionEpsilon)
            return true;

        return UnityEngine.Mathf.Abs(UnityEngine.Mathf.DeltaAngle(t.localEulerAngles.y, 0f)) > yawEpsilonDegrees;
    }

    public void SmoothRenderOffsetTowardIdentity(float positionSharpness, float yawSharpness, float deltaTime)
    {
        if (_visualRoot == null)
            return;

        Transform t = _visualRoot.transform;
        float posT = 1f - UnityEngine.Mathf.Exp(-positionSharpness * deltaTime);
        float yawT = 1f - UnityEngine.Mathf.Exp(-yawSharpness * deltaTime);

        t.localPosition = UnityEngine.Vector3.Lerp(t.localPosition, UnityEngine.Vector3.zero, posT);

        float yaw = UnityEngine.Mathf.LerpAngle(t.localEulerAngles.y, 0f, yawT);
        t.localRotation = UnityEngine.Quaternion.Euler(0f, yaw, 0f);
    }

    public bool Robe
    {
        get => _robe;
        set => _robe = value;
    }

    public StatCollection Stats
    {
        get
        {
            if (_dynel == null)
                _dynel = GetComponent<Dynel>();
            if (_dynel != null)
                return _dynel.Stats;
            return _ownedStats ??= new StatCollection();
        }
    }

    /// <summary>
    /// Wire dependencies without Reflex (e.g. CatAnim test scene).
    /// </summary>
    public void Configure(
        CatMeshLoader catMeshLoader,
        ResourceDatabase resourceDatabase,
        SkinTextureResolver skinTextures,
        AoImageTextureCache imageTextures,
        AbiffLoader abiffLoader)
    {
        _catMeshLoader = catMeshLoader;
        _resourceDatabase = resourceDatabase;
        _skinTextures = skinTextures;
        _imageTextures = imageTextures;
        _abiffLoader = abiffLoader;
    }

    void OnDestroy()
    {
        _loadGeneration++;
        if (_loadRoutine != null)
        {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }

        ClearAttachedMeshes();
        ClearBakedSlotTextures();
    }

    /// <summary>
    /// Queue an appearance rebuild. Heavy CPU runs in the background; Unity objects apply later.
    /// Safe if the character appears a frame or two late.
    /// </summary>
    public void RequestUpdateAppearance(bool playIdle = true)
    {
        if (!isActiveAndEnabled || _catMeshLoader == null)
        {
            UpdateAppearance(playIdle);
            return;
        }

        _loadGeneration++;
        int generation = _loadGeneration;
        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(UpdateAppearanceAsync(generation, playIdle));
    }

    public void StoreTextures(AoTextureSlot[] textures)
    {
        _slotTextures.Clear();
        if (textures == null)
            return;
        for (int i = 0; i < textures.Length; i++)
        {
            AoTextureSlot slot = textures[i];
            if (slot == null || slot.Id <= 0)
                continue;
            if (!Enum.IsDefined(typeof(BodyPart), slot.Place))
                continue;
            _slotTextures[(BodyPart)slot.Place] = slot.Id;
        }
    }

    /// <summary>
    /// Snapshot of naked skin + armor overlay + baked result per body slot (for debug UI).
    /// </summary>
    public List<BodySlotTextureDebug> GetBodySlotTextureDebug()
    {
        var results = new List<BodySlotTextureDebug>(BodyParts.Length);
        StatCollection stats = Stats;
        var breed = (Breed)stats.Get(Stat.Breed);
        var gender = (Gender)stats.Get(Stat.Sex);
        int race = stats.Get(Stat.Race);

        for (int i = 0; i < BodyParts.Length; i++)
        {
            BodyPart part = BodyParts[i];
            int skinId = 0;
            string skinName = null;
            Texture2D skin = null;

            if (_skinTextures != null
                && _skinTextures.SupportsBreed(breed)
                && SkinTextureResolver.TryBuildNakedName(part, breed, gender, race, out skinName)
                && _skinTextures.TryResolveNakedId(part, breed, gender, race, out skinId)
                && skinId > 0
                && _imageTextures != null)
            {
                skin = _imageTextures.GetSkinTexture(skinId);
            }

            _slotTextures.TryGetValue(part, out int armorId);
            Texture2D armor = null;
            if (armorId > 0 && _imageTextures != null)
                armor = _imageTextures.GetAoTexture(armorId);

            if (armor == null)
                TryGetCatMeshFallbackDiffuse(part, null, out armor);

            _bakedByPart.TryGetValue(part, out Texture2D baked);

            results.Add(new BodySlotTextureDebug(part, skinId, skinName, skin, armorId, armor, baked));
        }

        return results;
    }

    public void StoreMeshes(AoMesh[] meshes)
    {
        _attachedMeshes.Clear();
        if (meshes == null)
            return;
        for (int i = 0; i < meshes.Length; i++)
        {
            AoMesh mesh = meshes[i];
            if (mesh == null || mesh.Id == 0)
                continue;
            _attachedMeshes.Add(mesh);
        }
    }

    public void UpdateAppearance(bool playIdle = true)
    {
        if (_catMeshLoader == null)
            return;

        var total = Stopwatch.StartNew();
        if (!TryResolveAppearanceIds(out int catMeshId, out int monsterDataId, out int animSet, out double resolveMs))
            return;

        var section = Stopwatch.StartNew();
        bool applied = ApplyResolvedCatMesh(catMeshId, monsterDataId, animSet, playIdle);
        double applyMs = section.Elapsed.TotalMilliseconds;

        Debug.Log(
            $"[VisualDynel] UpdateAppearance {FormatDynelLabel()} " +
            $"catMesh={catMeshId} monsterData={monsterDataId} applied={applied} " +
            $"resolve={resolveMs:F1}ms apply={applyMs:F1}ms total={total.Elapsed.TotalMilliseconds:F1}ms");
    }

    IEnumerator UpdateAppearanceAsync(int generation, bool playIdle)
    {
        var total = Stopwatch.StartNew();
        if (!TryResolveAppearanceIds(out int catMeshId, out int monsterDataId, out int animSet, out double resolveMs))
            yield break;

        if (generation != _loadGeneration)
            yield break;

        yield return WaitForApplySlot();
        if (generation != _loadGeneration)
            yield break;

        bool rebuiltCatMesh = catMeshId != _loadedCatMeshId;
        double catMeshMs;

        if (!rebuiltCatMesh)
        {
            var section = Stopwatch.StartNew();
            _loadedMonsterDataId = monsterDataId;
            if (TryGetAnimPlayer(out CatAnimPlayer existingPlayer))
                existingPlayer.SetAnimSet(animSet);
            catMeshMs = section.Elapsed.TotalMilliseconds;
        }
        else
        {
            CatMeshBuildRole role = _catMeshLoader.BeginVisualBuild(
                catMeshId,
                monsterDataId,
                animSet,
                out int restAnimId,
                out Task<bool> waitTask);

            if (role == CatMeshBuildRole.Waiter)
            {
                while (!waitTask.IsCompleted)
                    yield return null;

                if (generation != _loadGeneration || !waitTask.Result)
                    yield break;

                yield return WaitForApplySlot();
                if (generation != _loadGeneration)
                    yield break;

                var section = Stopwatch.StartNew();
                ClearAttachedMeshes();
                ClearBakedSlotTextures();
                ClearCatMeshDiffuseCache();
                if (!_catMeshLoader.ApplyCatMeshVisual(
                        transform,
                        catMeshId,
                        monsterDataId,
                        animSet,
                        ref _visualRoot,
                        playIdle))
                    yield break;
                _loadedCatMeshId = catMeshId;
                _loadedMonsterDataId = monsterDataId;
                catMeshMs = section.Elapsed.TotalMilliseconds;
            }
            else if (role == CatMeshBuildRole.CacheHit)
            {
                var section = Stopwatch.StartNew();
                ClearAttachedMeshes();
                ClearBakedSlotTextures();
                ClearCatMeshDiffuseCache();
                if (!_catMeshLoader.ApplyCatMeshVisual(
                        transform,
                        catMeshId,
                        monsterDataId,
                        animSet,
                        ref _visualRoot,
                        playIdle))
                    yield break;
                _loadedCatMeshId = catMeshId;
                _loadedMonsterDataId = monsterDataId;
                catMeshMs = section.Elapsed.TotalMilliseconds;
            }
            else
            {
                bool buildOk = false;
                if (!_catMeshLoader.TryFetchBuildSources(
                        catMeshId,
                        monsterDataId,
                        animSet,
                        out RDBCatMesh catMesh,
                        out restAnimId,
                        out CATAnim restAnim))
                {
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                if (generation != _loadGeneration)
                {
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                Task<CatMeshBuildData> prepTask = Task.Run(
                    () => CatMeshLoader.BuildDataFromSources(catMesh, catMeshId, restAnimId, restAnim));

                while (!prepTask.IsCompleted)
                    yield return null;

                if (generation != _loadGeneration)
                {
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                if (prepTask.IsFaulted)
                {
                    Debug.LogError($"[VisualDynel] Background CatMesh prep failed: {prepTask.Exception?.GetBaseException()}");
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                CatMeshBuildData build = prepTask.Result;
                if (build == null || build.Submeshes == null || build.Submeshes.Length == 0)
                {
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                yield return WaitForApplySlot();
                if (generation != _loadGeneration)
                {
                    _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, false);
                    yield break;
                }

                var section = Stopwatch.StartNew();
                ClearAttachedMeshes();
                ClearBakedSlotTextures();
                ClearCatMeshDiffuseCache();
                buildOk = _catMeshLoader.ApplyBuildData(
                    transform,
                    build,
                    monsterDataId,
                    animSet,
                    ref _visualRoot,
                    playIdle,
                    cachePrototype: true);
                _catMeshLoader.CompleteVisualBuild(catMeshId, restAnimId, buildOk);
                if (!buildOk)
                    yield break;
                _loadedCatMeshId = catMeshId;
                _loadedMonsterDataId = monsterDataId;
                catMeshMs = section.Elapsed.TotalMilliseconds;
            }
        }

        if (generation != _loadGeneration)
            yield break;

        var texSw = Stopwatch.StartNew();
        yield return ApplyBodySlotTexturesAsync(generation);
        double texturesMs = texSw.Elapsed.TotalMilliseconds;
        if (generation != _loadGeneration)
            yield break;

        var attachSw = Stopwatch.StartNew();
        ApplyAttachedMeshes();
        double attachedMs = attachSw.Elapsed.TotalMilliseconds;
        ApplyScale();

        Debug.Log(
            $"[VisualDynel] UpdateAppearanceAsync {FormatDynelLabel()} " +
            $"catMesh={catMeshId} rebuilt={rebuiltCatMesh} resolve={resolveMs:F1}ms " +
            $"catMeshApply={catMeshMs:F1}ms textures={texturesMs:F1}ms attached={attachedMs:F1}ms " +
            $"total={total.Elapsed.TotalMilliseconds:F1}ms");

        _loadRoutine = null;
    }

    static IEnumerator WaitForApplySlot()
    {
        while (true)
        {
            int frame = Time.frameCount;
            if (frame != _applyFrame)
            {
                _applyFrame = frame;
                _appliesThisFrame = 0;
            }

            if (_appliesThisFrame < MaxMainThreadAppliesPerFrame)
            {
                _appliesThisFrame++;
                yield break;
            }

            yield return null;
        }
    }

    bool TryResolveAppearanceIds(
        out int catMeshId,
        out int monsterDataId,
        out int animSet,
        out double resolveMs)
    {
        catMeshId = 0;
        monsterDataId = 0;
        animSet = 0;
        resolveMs = 0;

        var section = Stopwatch.StartNew();
        StatCollection stats = Stats;
        monsterDataId = stats.Get(Stat.MonsterData);
        animSet = stats.Get(Stat.AnimSet);

        if (monsterDataId != 0)
        {
            if (!MonsterDataResolver.TryResolveBodyCatMeshId(_resourceDatabase, monsterDataId, out catMeshId))
            {
                Debug.LogWarning($"VisualDynel: MonsterData {monsterDataId} has no BodyCatMesh stat.");
                return false;
            }
        }
        else
        {
            var breed = (Breed)stats.Get(Stat.Breed);
            var gender = (Gender)stats.Get(Stat.Sex);
            var fatness = (Fatness)stats.Get(Stat.Fatness);
            if (!_catMeshLoader.TryResolveAppearanceCatMeshId(
                    breed,
                    gender,
                    fatness,
                    _robe,
                    out catMeshId))
            {
                Debug.LogWarning(
                    $"VisualDynel: No CatMesh for appearance {FormatDynelLabel()} Breed={breed} Gender={gender} Fatness={fatness} Robe={_robe}.");
                return false;
            }

            if (!MonsterDataResolver.TryFindMonsterDataForCatMesh(
                    _resourceDatabase,
                    catMeshId,
                    out monsterDataId))
            {
                Debug.LogWarning(
                    $"VisualDynel: No MonsterData found for appearance CatMesh {catMeshId}; anims/attractors may be wrong.");
            }
        }

        resolveMs = section.Elapsed.TotalMilliseconds;
        return true;
    }

    /// <summary>
    /// Load a specific CatMesh id (anim-test / debug). Prefer <see cref="UpdateAppearance"/> for characters.
    /// </summary>
    public bool ApplyCatMeshId(int catMeshId, int monsterDataId, int animSet, bool playIdle = true)
    {
        if (_catMeshLoader == null || catMeshId <= 0)
            return false;

        if (monsterDataId <= 0)
            MonsterDataResolver.TryFindMonsterDataForCatMesh(_resourceDatabase, catMeshId, out monsterDataId);

        return ApplyResolvedCatMesh(catMeshId, monsterDataId, animSet, playIdle);
    }

    bool ApplyResolvedCatMesh(int catMeshId, int monsterDataId, int animSet, bool playIdle)
    {
        var section = Stopwatch.StartNew();
        double catMeshMs;
        bool rebuiltCatMesh;

        if (catMeshId == _loadedCatMeshId)
        {
            _loadedMonsterDataId = monsterDataId;
            if (TryGetAnimPlayer(out CatAnimPlayer existingPlayer))
                existingPlayer.SetAnimSet(animSet);
            catMeshMs = section.Elapsed.TotalMilliseconds;
            rebuiltCatMesh = false;
        }
        else
        {
            ClearAttachedMeshes();
            ClearBakedSlotTextures();
            ClearCatMeshDiffuseCache();
            if (!_catMeshLoader.ApplyCatMeshVisual(
                    transform,
                    catMeshId,
                    monsterDataId,
                    animSet,
                    ref _visualRoot,
                    playIdle))
                return false;
            _loadedCatMeshId = catMeshId;
            _loadedMonsterDataId = monsterDataId;
            catMeshMs = section.Elapsed.TotalMilliseconds;
            rebuiltCatMesh = true;
        }

        section.Restart();
        ApplyBodySlotTextures();
        double texturesMs = section.Elapsed.TotalMilliseconds;

        section.Restart();
        ApplyAttachedMeshes();
        double attachedMs = section.Elapsed.TotalMilliseconds;

        section.Restart();
        ApplyScale();
        double scaleMs = section.Elapsed.TotalMilliseconds;

        Debug.Log(
            $"[VisualDynel] ApplyResolved {FormatDynelLabel()} " +
            $"rebuiltCatMesh={rebuiltCatMesh} " +
            $"catMesh={catMeshMs:F1}ms textures={texturesMs:F1}ms " +
            $"attached={attachedMs:F1}ms scale={scaleMs:F1}ms");
        return true;
    }

    public void ApplyScale()
    {
        if (_visualRoot == null)
            return;

        int scaleStat = Stats.Get(Stat.Scale);
        float scale = scaleStat > 0 ? scaleStat / 100f : 1f;
        _visualRoot.transform.localScale = UnityEngine.Vector3.one * scale;
        InvalidateMeshHeightCache();
    }

    public void ApplyBodySlotTextures()
    {
        if (_visualRoot == null || _imageTextures == null)
            return;

        var total = Stopwatch.StartNew();
        CollectBodyTextureJobs(out List<BodyTextureJob> jobs, out List<Material> previousMaterials, out List<Texture2D> previousTextures);
        int bakedCount = 0;
        double bakeMs = 0;
        var bakeSw = Stopwatch.StartNew();

        for (int i = 0; i < jobs.Count; i++)
        {
            BodyTextureJob job = jobs[i];
            bakeSw.Restart();
            Texture2D baked = GetOrBakeBodyTexture(job);
            bakeMs += bakeSw.Elapsed.TotalMilliseconds;
            if (baked == null)
                continue;

            ApplyBakedMaterial(job, baked);
            bakedCount++;
        }

        DestroyPreviousBodyMaterials(previousMaterials, previousTextures);

        Debug.Log(
            $"[VisualDynel] BodyTextures {FormatDynelLabel()} parts={bakedCount} " +
            $"bake={bakeMs:F1}ms total={total.Elapsed.TotalMilliseconds:F1}ms");
    }

    IEnumerator ApplyBodySlotTexturesAsync(int generation)
    {
        if (_visualRoot == null || _imageTextures == null)
            yield break;

        var total = Stopwatch.StartNew();
        CollectBodyTextureJobs(out List<BodyTextureJob> jobs, out List<Material> previousMaterials, out List<Texture2D> previousTextures);
        if (jobs.Count == 0)
        {
            DestroyPreviousBodyMaterials(previousMaterials, previousTextures);
            yield break;
        }

        var bakeInputs = new List<BodyBakeInput>(jobs.Count);
        for (int i = 0; i < jobs.Count; i++)
        {
            BodyTextureJob job = jobs[i];
            var key = (job.SkinId, job.ArmorId);
            lock (SharedBakeCache)
            {
                if (SharedBakeCache.TryGetValue(key, out Texture2D cached) && cached != null)
                {
                    bakeInputs.Add(new BodyBakeInput { JobIndex = i, Cached = cached });
                    continue;
                }
            }

            Color32[] skinPixels = job.Skin.GetPixels32();
            Color32[] armorPixels = null;
            Texture2D mismatched = null;
            if (job.Armor != null)
            {
                if (job.Armor.width == job.Skin.width && job.Armor.height == job.Skin.height)
                    armorPixels = job.Armor.GetPixels32();
                else
                    mismatched = job.Armor;
            }

            // Mismatched sizes need main-thread bilinear; bake sync for those rare cases.
            if (mismatched != null)
            {
                Color32[] pixels = TextureCompositor.BakeGreenKeyPixels(
                    skinPixels, null, job.Skin.width, job.Skin.height, mismatched);
                bakeInputs.Add(new BodyBakeInput
                {
                    JobIndex = i,
                    Width = job.Skin.width,
                    Height = job.Skin.height,
                    Pixels = pixels,
                    CacheKey = key
                });
                continue;
            }

            bakeInputs.Add(new BodyBakeInput
            {
                JobIndex = i,
                Width = job.Skin.width,
                Height = job.Skin.height,
                SkinPixels = skinPixels,
                ArmorPixels = armorPixels,
                CacheKey = key,
                NeedsBackgroundBake = true
            });
        }

        bool needsBg = false;
        for (int i = 0; i < bakeInputs.Count; i++)
        {
            if (bakeInputs[i].NeedsBackgroundBake)
            {
                needsBg = true;
                break;
            }
        }

        if (needsBg)
        {
            Task bakeTask = Task.Run(() =>
            {
                for (int i = 0; i < bakeInputs.Count; i++)
                {
                    BodyBakeInput input = bakeInputs[i];
                    if (!input.NeedsBackgroundBake)
                        continue;

                    input.Pixels = TextureCompositor.BakeGreenKeyPixels(
                        input.SkinPixels,
                        input.ArmorPixels,
                        input.Width,
                        input.Height);
                    bakeInputs[i] = input;
                }
            });

            while (!bakeTask.IsCompleted)
                yield return null;

            if (bakeTask.IsFaulted)
            {
                Debug.LogError($"[VisualDynel] Background texture bake failed: {bakeTask.Exception?.GetBaseException()}");
                DestroyPreviousBodyMaterials(previousMaterials, previousTextures);
                yield break;
            }
        }

        if (generation != _loadGeneration)
        {
            DestroyPreviousBodyMaterials(previousMaterials, previousTextures);
            yield break;
        }

        int bakedCount = 0;
        for (int i = 0; i < bakeInputs.Count; i++)
        {
            BodyBakeInput input = bakeInputs[i];
            BodyTextureJob job = jobs[input.JobIndex];
            Texture2D baked = input.Cached;
            if (baked == null && input.Pixels != null)
            {
                lock (SharedBakeCache)
                {
                    if (!SharedBakeCache.TryGetValue(input.CacheKey, out baked) || baked == null)
                    {
                        baked = TextureCompositor.CreateTexture(
                            input.Pixels,
                            input.Width,
                            input.Height,
                            $"Baked_{BakeNamePrefix()}_{job.Part}");
                        if (baked != null)
                            SharedBakeCache[input.CacheKey] = baked;
                    }
                }
            }

            if (baked == null)
                continue;

            ApplyBakedMaterial(job, baked);
            bakedCount++;
        }

        DestroyPreviousBodyMaterials(previousMaterials, previousTextures);
        Debug.Log(
            $"[VisualDynel] BodyTexturesAsync {FormatDynelLabel()} parts={bakedCount} " +
            $"total={total.Elapsed.TotalMilliseconds:F1}ms");
    }

    void CollectBodyTextureJobs(
        out List<BodyTextureJob> jobs,
        out List<Material> previousMaterials,
        out List<Texture2D> previousTextures)
    {
        jobs = new List<BodyTextureJob>();
        previousMaterials = new List<Material>(_bakedSlotMaterials);
        previousTextures = new List<Texture2D>(_bakedSlotTextures);
        _bakedSlotMaterials.Clear();
        _bakedSlotTextures.Clear();
        _bakedByPart.Clear();

        StatCollection stats = Stats;
        var breed = (Breed)stats.Get(Stat.Breed);
        var gender = (Gender)stats.Get(Stat.Sex);
        int race = stats.Get(Stat.Race);
        bool supportsNakedSkin = _skinTextures != null && _skinTextures.SupportsBreed(breed);

        SkinnedMeshRenderer[] renderers = _visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer == null || renderer.sharedMaterial == null)
                continue;

            string matName = renderer.sharedMaterial.name;
            int instanceIdx = matName.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (instanceIdx >= 0)
                matName = matName.Substring(0, instanceIdx);

            if (!SkinTextureResolver.TryParseBodyPartName(matName, out BodyPart part))
                continue;

            _slotTextures.TryGetValue(part, out int armorId);
            Texture2D armor = armorId > 0 ? _imageTextures.GetAoTexture(armorId) : null;
            if (armor == null)
                TryGetCatMeshFallbackDiffuse(part, renderer.sharedMaterial, out armor);

            int skinId = 0;
            Texture2D nakedSkin = null;
            if (supportsNakedSkin && _skinTextures.TryResolveNakedId(part, breed, gender, race, out skinId))
                nakedSkin = _imageTextures.GetSkinTexture(skinId);

            if (nakedSkin == null)
                continue;

            if (armor != null && armorId <= 0)
                armorId = -armor.GetInstanceID();

            jobs.Add(new BodyTextureJob
            {
                Part = part,
                MatName = matName,
                Renderer = renderer,
                Skin = nakedSkin,
                Armor = armor,
                SkinId = skinId,
                ArmorId = armorId
            });
        }
    }

    Texture2D GetOrBakeBodyTexture(BodyTextureJob job)
    {
        var key = (job.SkinId, job.ArmorId);
        lock (SharedBakeCache)
        {
            if (SharedBakeCache.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;
        }

        Texture2D baked = TextureCompositor.BakeGreenKey(
            job.Skin,
            job.Armor,
            $"Baked_{BakeNamePrefix()}_{job.Part}");
        if (baked != null)
        {
            lock (SharedBakeCache)
                SharedBakeCache[key] = baked;
        }
        return baked;
    }

    void ApplyBakedMaterial(BodyTextureJob job, Texture2D baked)
    {
        _bakedSlotTextures.Add(baked);
        _bakedByPart[job.Part] = baked;
        Material instance = new Material(job.Renderer.sharedMaterial);
        instance.name = job.MatName;
        if (instance.HasProperty("_BaseColorMap"))
            instance.SetTexture("_BaseColorMap", baked);
        else if (instance.HasProperty("_MainTex"))
            instance.SetTexture("_MainTex", baked);
        job.Renderer.sharedMaterial = instance;
        _bakedSlotMaterials.Add(instance);
    }

    void DestroyPreviousBodyMaterials(List<Material> previousMaterials, List<Texture2D> previousTextures)
    {
        for (int i = 0; i < previousMaterials.Count; i++)
        {
            if (previousMaterials[i] != null)
                Destroy(previousMaterials[i]);
        }

        // SharedBakeCache owns baked textures; never destroy them here.
        _ = previousTextures;
    }

    struct BodyTextureJob
    {
        public BodyPart Part;
        public string MatName;
        public SkinnedMeshRenderer Renderer;
        public Texture2D Skin;
        public Texture2D Armor;
        public int SkinId;
        public int ArmorId;
    }

    struct BodyBakeInput
    {
        public int JobIndex;
        public int Width;
        public int Height;
        public Color32[] SkinPixels;
        public Color32[] ArmorPixels;
        public Color32[] Pixels;
        public Texture2D Cached;
        public (int skinId, int armorId) CacheKey;
        public bool NeedsBackgroundBake;
    }

    public void ApplyAttachedMeshes()
    {
        var total = Stopwatch.StartNew();
        ClearAttachedMeshes();
        if (_visualRoot == null || _abiffLoader == null)
            return;

        StatCollection stats = Stats;
        int visualFlags = stats.Get(Stat.VisualFlags);
        int headMeshStat = stats.Get(Stat.HeadMesh);
        bool attachedHead = false;
        int bestHeadLayer = int.MinValue;
        AoMesh bestHead = null;
        int attachedCount = 0;
        double attachMs = 0;
        var attachSw = new Stopwatch();

        for (int i = 0; i < _attachedMeshes.Count; i++)
        {
            AoMesh mesh = _attachedMeshes[i];
            int position = mesh.Position;
            if (!Enum.IsDefined(typeof(AttractorPlace), position))
                continue;
            if (!ShouldShowMesh(position, (int)mesh.Id, headMeshStat, visualFlags))
                continue;

            if (position == (int)AttractorPlace.Head)
            {
                if (bestHead == null || mesh.Layer > bestHeadLayer)
                {
                    bestHead = mesh;
                    bestHeadLayer = mesh.Layer;
                }
                continue;
            }

            attachSw.Restart();
            AttachMesh(mesh);
            attachMs += attachSw.Elapsed.TotalMilliseconds;
            attachedCount++;
        }

        if (bestHead != null)
        {
            attachSw.Restart();
            AttachMesh(bestHead);
            attachMs += attachSw.Elapsed.TotalMilliseconds;
            attachedCount++;
            attachedHead = true;
        }

        if (!attachedHead && headMeshStat > 0)
        {
            attachSw.Restart();
            AttachMesh(new AoMesh
            {
                Position = (byte)AttractorPlace.Head,
                Id = (uint)headMeshStat,
                OverrideTextureId = 0,
                Layer = 4
            });
            attachMs += attachSw.Elapsed.TotalMilliseconds;
            attachedCount++;
        }

        Debug.Log(
            $"[VisualDynel] AttachedMeshes {FormatDynelLabel()} count={attachedCount} " +
            $"attach={attachMs:F1}ms total={total.Elapsed.TotalMilliseconds:F1}ms");
        InvalidateMeshHeightCache();
    }

    public bool TryGetAttractor(AttractorPlace place, out Attractor attractor)
    {
        attractor = null;
        if (_visualRoot == null)
            return false;
        if (!_visualRoot.TryGetComponent(out AttractorCollection collection))
            return false;
        return collection.TryGet(place, out attractor);
    }

    public bool TryGetAnimPlayer(out CatAnimPlayer player)
    {
        player = null;
        if (_visualRoot == null)
            return false;
        if (_visualRoot.TryGetComponent(out CatMeshVisualHolder holder) && holder.Player != null)
        {
            player = holder.Player;
            return true;
        }
        return _visualRoot.TryGetComponent(out player);
    }

    public bool Play(string logicalName, float blendSeconds = CatAnimPlayer.DefaultBlendSeconds)
    {
        if (!TryGetAnimPlayer(out CatAnimPlayer player))
            return false;
        return player.Play(logicalName, blendSeconds);
    }

    public bool PlayOnce(string logicalName, float blendSeconds, Action onComplete)
    {
        if (!TryGetAnimPlayer(out CatAnimPlayer player))
        {
            onComplete?.Invoke();
            return false;
        }

        return player.PlayOnce(logicalName, blendSeconds, onComplete);
    }

    public bool PlayOverlayOnce(string logicalName, float blendSeconds, Action onComplete)
    {
        if (!TryGetAnimPlayer(out CatAnimPlayer player))
        {
            onComplete?.Invoke();
            return false;
        }

        return player.PlayOverlayOnce(logicalName, blendSeconds, onComplete);
    }

    bool ShouldShowMesh(int position, int meshId, int headMeshStat, int visualFlags)
    {
        if (position == (int)AttractorPlace.RightShoulder)
            return (visualFlags & VisualFlagRightShoulder) != 0;
        if (position == (int)AttractorPlace.LeftShoulder)
            return (visualFlags & VisualFlagLeftShoulder) != 0;
        if (position == (int)AttractorPlace.Head)
        {
            bool showHelmet = (visualFlags & VisualFlagShowHelmet) != 0;
            if (showHelmet)
                return true;
            return headMeshStat > 0 && meshId == headMeshStat;
        }
        return true;
    }

    void AttachMesh(AoMesh mesh)
    {
        if (mesh == null || mesh.Id == 0)
            return;

        var place = (AttractorPlace)mesh.Position;
        if (!TryGetAttractor(place, out Attractor attractor))
        {
            Debug.LogWarning($"VisualDynel: Mesh {mesh.Id} at {place} but no attractor.");
            return;
        }

        if (!_abiffLoader.TryCreateVisual(
                (int)mesh.Id,
                attractor.transform,
                mesh.OverrideTextureId,
                out GameObject root))
            return;

        root.name = $"Equip_{place}_{mesh.Id}_L{mesh.Layer}";
        _attachedMeshRoots.Add(root);
    }

    void ClearAttachedMeshes()
    {
        for (int i = 0; i < _attachedMeshRoots.Count; i++)
        {
            if (_attachedMeshRoots[i] != null)
                Destroy(_attachedMeshRoots[i]);
        }
        _attachedMeshRoots.Clear();
    }

    void ClearBakedSlotTextures()
    {
        for (int i = 0; i < _bakedSlotMaterials.Count; i++)
        {
            if (_bakedSlotMaterials[i] != null)
                Destroy(_bakedSlotMaterials[i]);
        }
        _bakedSlotMaterials.Clear();
        // Baked textures live in SharedBakeCache; only drop local refs.
        _bakedSlotTextures.Clear();
        _bakedByPart.Clear();
    }

    void ClearCatMeshDiffuseCache() => _catMeshDiffuseByPart.Clear();

    void EnsureDynel()
    {
        if (_dynel == null)
            _dynel = GetComponent<Dynel>();
    }

    string FormatDynelLabel()
    {
        EnsureDynel();
        if (_dynel != null)
            return $"{_dynel.Identity.Type}:{_dynel.Identity.Instance} \"{_dynel.Name}\"";
        return gameObject.name;
    }

    string BakeNamePrefix()
    {
        EnsureDynel();
        if (_dynel != null)
            return _dynel.Identity.Instance.ToString();
        return gameObject.name;
    }

    Texture2D TryGetNakedSkin(BodyPart part, Breed breed, Gender gender, int race, bool supportsNakedSkin)
    {
        if (!supportsNakedSkin || _skinTextures == null || _imageTextures == null)
            return null;

        if (!_skinTextures.TryResolveNakedId(part, breed, gender, race, out int skinId))
            return null;

        return _imageTextures.GetSkinTexture(skinId);
    }

    bool TryGetCatMeshFallbackDiffuse(BodyPart part, Material material, out Texture2D texture)
    {
        if (_catMeshDiffuseByPart.TryGetValue(part, out texture) && texture != null)
            return true;

        if (material != null && TryGetCatMeshDiffuse(material, out texture))
        {
            _catMeshDiffuseByPart[part] = texture;
            return true;
        }

        if (_visualRoot == null || _imageTextures == null)
            return false;

        SkinnedMeshRenderer[] renderers = _visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material candidate = renderers[i]?.sharedMaterial;
            if (candidate == null)
                continue;

            string matName = candidate.name;
            int instanceIdx = matName.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (instanceIdx >= 0)
                matName = matName.Substring(0, instanceIdx);

            if (!SkinTextureResolver.TryParseBodyPartName(matName, out BodyPart matPart) || matPart != part)
                continue;

            if (TryGetCatMeshDiffuse(candidate, out texture))
            {
                _catMeshDiffuseByPart[part] = texture;
                return true;
            }
        }

        texture = null;
        return false;
    }

    bool TryGetCatMeshDiffuse(Material material, out Texture2D texture)
    {
        texture = null;
        if (material == null)
            return false;

        Texture source = null;
        if (material.HasProperty("_BaseColorMap"))
            source = material.GetTexture("_BaseColorMap");
        else if (material.HasProperty("_MainTex"))
            source = material.GetTexture("_MainTex");

        if (source == null || string.IsNullOrEmpty(source.name))
            return false;

        return TryLoadReadableTexture(source.name, out texture);
    }

    bool TryLoadReadableTexture(string textureName, out Texture2D texture)
    {
        texture = null;
        if (string.IsNullOrEmpty(textureName))
            return false;

        const string aoPrefix = "AOTexture_";
        if (textureName.StartsWith(aoPrefix, StringComparison.Ordinal)
            && int.TryParse(textureName.Substring(aoPrefix.Length), out int aoId))
        {
            texture = _imageTextures.GetAoTexture(aoId);
            return texture != null;
        }

        const string skinPrefix = "SkinTexture_";
        if (textureName.StartsWith(skinPrefix, StringComparison.Ordinal)
            && int.TryParse(textureName.Substring(skinPrefix.Length), out int skinId))
        {
            texture = _imageTextures.GetSkinTexture(skinId);
            return texture != null;
        }

        return false;
    }
}

public readonly struct BodySlotTextureDebug
{
    public BodySlotTextureDebug(
        BodyPart part,
        int skinId,
        string skinName,
        Texture2D skin,
        int armorId,
        Texture2D armor,
        Texture2D baked)
    {
        Part = part;
        SkinId = skinId;
        SkinName = skinName;
        Skin = skin;
        ArmorId = armorId;
        Armor = armor;
        Baked = baked;
    }

    public BodyPart Part { get; }
    public int SkinId { get; }
    public string SkinName { get; }
    public Texture2D Skin { get; }
    public int ArmorId { get; }
    public Texture2D Armor { get; }
    public Texture2D Baked { get; }
}
