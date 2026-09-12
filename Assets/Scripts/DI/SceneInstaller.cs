using Reflex.Core;
using UnityEngine;

public class SceneInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] PlayerController _playerController;
    [SerializeField] PlayfieldFactory _playfieldFactory;
    [SerializeField] LoadingScreenView _loadingScreenView;
    [SerializeField] WorldOverlayController _worldOverlayController;
    [SerializeField] InventoryView _inventoryWindowView;
    [SerializeField] CursorController _cursorController;

    public void InstallBindings(ContainerBuilder containerBuilder)
    {
        var resourceDatabase = new ResourceDatabase();

        string aoPath = LoginPreferences.GetAoPath();
        if (AoInstallPath.IsValid(aoPath))
            resourceDatabase.Initialize(AoInstallPath.Normalize(aoPath));

        var abiffMaterials = new AbiffMaterialFactory(resourceDatabase);
        var catMeshMaterials = new CatMeshMaterialFactory(abiffMaterials);
        var imageTextures = new AoImageTextureCache(resourceDatabase);
        var iconTextures = new IconTextureCache(resourceDatabase);
        var skinTextures = new SkinTextureResolver(resourceDatabase);
        var itemTemplates = new ItemTemplateCache(resourceDatabase);
        var effectTextures = new EffectTextureNames(resourceDatabase);
        var effectCatalog = new GfxTweakCatalog(resourceDatabase);
        var effectHandler = new EffectHandler(effectCatalog, imageTextures, effectTextures);
        containerBuilder.RegisterValue(resourceDatabase);
        containerBuilder.RegisterValue(abiffMaterials);
        containerBuilder.RegisterValue(imageTextures);
        containerBuilder.RegisterValue(iconTextures);
        containerBuilder.RegisterValue(skinTextures);
        containerBuilder.RegisterValue(itemTemplates);
        containerBuilder.RegisterValue(effectHandler);
        containerBuilder.RegisterValue(new AbiffLoader(resourceDatabase, abiffMaterials, imageTextures));
        containerBuilder.RegisterValue(new CatMeshLoader(resourceDatabase, catMeshMaterials));
        containerBuilder.RegisterValue(_playfieldFactory);
        containerBuilder.RegisterValue(new NetworkClient(new NetworkConfig { AutoReconnect = false }));
        containerBuilder.RegisterValue(_playerController);

        _loadingScreenView ??= GetComponentInChildren<LoadingScreenView>(true);
        containerBuilder.RegisterValue(new LoadingScreen(_loadingScreenView, resourceDatabase));
        var uvgaTextures = new UvgaTextureCache(resourceDatabase);
        UvgaTextureSource.BindRuntime(uvgaTextures);
        containerBuilder.RegisterValue(uvgaTextures);

        _cursorController ??= FindCursorController();
        if (_cursorController == null)
        {
            var cursorGo = new GameObject("CursorController");
            cursorGo.transform.SetParent(transform, false);
            _cursorController = cursorGo.AddComponent<CursorController>();
        }

        containerBuilder.RegisterValue(_cursorController);

        _worldOverlayController ??= GetComponentInChildren<WorldOverlayController>(true);
        if (_worldOverlayController == null)
        {
            var overlayGo = new GameObject("WorldOverlay");
            overlayGo.transform.SetParent(transform, false);
            _worldOverlayController = overlayGo.AddComponent<WorldOverlayController>();
        }

        containerBuilder.RegisterValue(_worldOverlayController);

        _inventoryWindowView ??= GetComponentInChildren<InventoryView>(true);
        if (_inventoryWindowView == null)
        {
            var inventoryGo = new GameObject("InventoryWindow");
            inventoryGo.transform.SetParent(transform, false);
            _inventoryWindowView = inventoryGo.AddComponent<InventoryView>();
        }

        containerBuilder.RegisterValue(new GameHud(_inventoryWindowView), new System.Type[] { typeof(IGameHud) });
        containerBuilder.RegisterValue(new UIInteractionManager(), new System.Type[] { typeof(IUINotifyService) });

        EffectRuntimeHost fxHost = GetComponentInChildren<EffectRuntimeHost>(true);
        if (fxHost == null)
        {
            var fxGo = new GameObject("EffectRuntime");
            fxGo.transform.SetParent(transform, false);
            fxHost = fxGo.AddComponent<EffectRuntimeHost>();
        }

        fxHost.Init(effectHandler, _playerController);
        effectHandler.SetLightParent(fxHost.transform);
    }

    CursorController FindCursorController()
    {
        if (_playerController == null)
            return GetComponentInChildren<CursorController>(true);

        Transform root = _playerController.transform.parent;
        if (root != null)
        {
            CursorController fromControllers = root.GetComponentInChildren<CursorController>(true);
            if (fromControllers != null)
                return fromControllers;
        }

        return _playerController.GetComponentInChildren<CursorController>(true);
    }
}
