using System;
using System.Collections.Generic;
using AODB.Common.Enums;
using AOSharp.Common.GameData;
using Reflex.Attributes;
using UnityEngine;
using AoMesh = SmokeLounge.AOtomation.Messaging.GameData.Mesh;
using AoTextureSlot = SmokeLounge.AOtomation.Messaging.GameData.Texture;

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

    static readonly BodyPart[] BodyParts =
    {
        BodyPart.Hands,
        BodyPart.Arms,
        BodyPart.Body,
        BodyPart.Legs,
        BodyPart.Feet
    };

    readonly Dictionary<BodyPart, int> _slotTextures = new Dictionary<BodyPart, int>();
    readonly Dictionary<BodyPart, Texture2D> _bakedByPart = new Dictionary<BodyPart, Texture2D>();
    readonly List<AoMesh> _attachedMeshes = new List<AoMesh>();
    readonly List<GameObject> _attachedMeshRoots = new List<GameObject>();
    readonly List<Texture2D> _bakedSlotTextures = new List<Texture2D>();
    readonly List<Material> _bakedSlotMaterials = new List<Material>();

    public GameObject VisualRoot => _visualRoot;
    public int LoadedCatMeshId => _loadedCatMeshId;
    public int LoadedMonsterDataId => _loadedMonsterDataId;
    public IReadOnlyDictionary<BodyPart, int> ArmorSlotTextureIds => _slotTextures;

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
        ClearAttachedMeshes();
        ClearBakedSlotTextures();
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

            if (armor == null && skin == null && _visualRoot != null && _imageTextures != null)
            {
                SkinnedMeshRenderer[] renderers = _visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int r = 0; r < renderers.Length && skin == null; r++)
                {
                    Material mat = renderers[r]?.sharedMaterial;
                    if (mat == null)
                        continue;

                    string matName = mat.name;
                    int instanceIdx = matName.IndexOf(" (Instance)", StringComparison.Ordinal);
                    if (instanceIdx >= 0)
                        matName = matName.Substring(0, instanceIdx);

                    if (!SkinTextureResolver.TryParseBodyPartName(matName, out BodyPart matPart) || matPart != part)
                        continue;

                    TryGetCatMeshDiffuse(mat, out skin);
                }
            }

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

        StatCollection stats = Stats;
        int monsterDataId = stats.Get(Stat.MonsterData);
        int animSet = stats.Get(Stat.AnimSet);
        int catMeshId;

        if (monsterDataId != 0)
        {
            if (!MonsterDataResolver.TryResolveBodyCatMeshId(_resourceDatabase, monsterDataId, out catMeshId))
            {
                Debug.LogWarning($"VisualDynel: MonsterData {monsterDataId} has no BodyCatMesh stat.");
                return;
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
                return;
            }

            // Appearance path has no SCFU MonsterData; look up one that uses this body
            // CatMesh so rest-pose skinning + idle/run anims still work.
            if (!MonsterDataResolver.TryFindMonsterDataForCatMesh(
                    _resourceDatabase,
                    catMeshId,
                    out monsterDataId))
            {
                Debug.LogWarning(
                    $"VisualDynel: No MonsterData found for appearance CatMesh {catMeshId}; anims/attractors may be wrong.");
            }
        }

        ApplyResolvedCatMesh(catMeshId, monsterDataId, animSet, playIdle);
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
        if (catMeshId == _loadedCatMeshId)
        {
            _loadedMonsterDataId = monsterDataId;
            if (TryGetAnimPlayer(out CatAnimPlayer existingPlayer))
                existingPlayer.SetAnimSet(animSet);
        }
        else
        {
            ClearAttachedMeshes();
            ClearBakedSlotTextures();
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
        }

        ApplyBodySlotTextures();
        ApplyAttachedMeshes();
        ApplyScale();
        return true;
    }

    public void ApplyScale()
    {
        if (_visualRoot == null)
            return;

        int scaleStat = Stats.Get(Stat.Scale);
        float scale = scaleStat > 0 ? scaleStat / 100f : 1f;
        _visualRoot.transform.localScale = UnityEngine.Vector3.one * scale;
    }

    public void ApplyBodySlotTextures()
    {
        if (_visualRoot == null || _imageTextures == null)
            return;

        StatCollection stats = Stats;
        var breed = (Breed)stats.Get(Stat.Breed);
        var gender = (Gender)stats.Get(Stat.Sex);
        int race = stats.Get(Stat.Race);
        bool supportsNakedSkin = _skinTextures != null && _skinTextures.SupportsBreed(breed);
        string bakePrefix = BakeNamePrefix();

        var previousMaterials = new List<Material>(_bakedSlotMaterials);
        var previousTextures = new List<Texture2D>(_bakedSlotTextures);
        _bakedSlotMaterials.Clear();
        _bakedSlotTextures.Clear();
        _bakedByPart.Clear();

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
            if (armor == null && TryGetCatMeshDiffuse(renderer.sharedMaterial, out Texture2D catMeshDiffuse))
                armor = catMeshDiffuse;

            Texture2D nakedSkin = TryGetNakedSkin(part, breed, gender, race, supportsNakedSkin);
            if (nakedSkin == null)
                continue;

            Texture2D baked = TextureCompositor.BakeGreenKey(
                nakedSkin,
                armor,
                $"Baked_{bakePrefix}_{part}");

            if (baked == null)
                continue;

            _bakedSlotTextures.Add(baked);
            _bakedByPart[part] = baked;
            Material instance = new Material(renderer.sharedMaterial);
            instance.name = matName;
            if (instance.HasProperty("_BaseColorMap"))
                instance.SetTexture("_BaseColorMap", baked);
            else if (instance.HasProperty("_MainTex"))
                instance.SetTexture("_MainTex", baked);
            renderer.sharedMaterial = instance;
            _bakedSlotMaterials.Add(instance);
        }

        for (int i = 0; i < previousMaterials.Count; i++)
        {
            if (previousMaterials[i] != null)
                Destroy(previousMaterials[i]);
        }
        for (int i = 0; i < previousTextures.Count; i++)
        {
            if (previousTextures[i] != null)
                Destroy(previousTextures[i]);
        }
    }

    public void ApplyAttachedMeshes()
    {
        ClearAttachedMeshes();
        if (_visualRoot == null || _abiffLoader == null)
            return;

        StatCollection stats = Stats;
        int visualFlags = stats.Get(Stat.VisualFlags);
        int headMeshStat = stats.Get(Stat.HeadMesh);
        bool attachedHead = false;
        int bestHeadLayer = int.MinValue;
        AoMesh bestHead = null;

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

            AttachMesh(mesh);
        }

        if (bestHead != null)
        {
            AttachMesh(bestHead);
            attachedHead = true;
        }

        if (!attachedHead && headMeshStat > 0)
        {
            AttachMesh(new AoMesh
            {
                Position = (byte)AttractorPlace.Head,
                Id = (uint)headMeshStat,
                OverrideTextureId = 0,
                Layer = 4
            });
        }
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
        for (int i = 0; i < _bakedSlotTextures.Count; i++)
        {
            if (_bakedSlotTextures[i] != null)
                Destroy(_bakedSlotTextures[i]);
        }
        _bakedSlotTextures.Clear();
        _bakedByPart.Clear();
    }

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
