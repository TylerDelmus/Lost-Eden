using Reflex.Core;
using UnityEngine;

public class SceneInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] PlayerController _playerController;
    [SerializeField] PlayfieldFactory _playfieldFactory;
    [SerializeField] LoadingScreenView _loadingScreenView;
    [SerializeField] WorldOverlayController _worldOverlayController;
    [SerializeField] CursorController _cursorController;

    [Header("Worlds")]
    [Tooltip("Optional. Left empty, a bare LoginWorld GameObject is created instead.")]
    [SerializeField] GameObject _loginWorldPrefab;
    [SerializeField] GameObject _gameWorldPrefab;

    public void InstallBindings(ContainerBuilder containerBuilder)
    {
        var resourceDatabase = new ResourceDatabase();

        string aoPath = AoInstall.Path;
        if (AoInstallPath.IsValid(aoPath))
            resourceDatabase.Initialize(AoInstallPath.Normalize(aoPath));

        var abiffMaterials = new AbiffMaterialFactory(resourceDatabase);
        var catMeshMaterials = new CatMeshMaterialFactory(abiffMaterials);
        var imageTextures = new AoImageTextureCache(resourceDatabase);
        var iconTextures = new IconTextureCache(resourceDatabase);
        var skinTextures = new SkinTextureResolver(resourceDatabase);
        var itemTemplates = new ItemTemplateCache(resourceDatabase);
        var effectTextures = new EffectTextureNames(resourceDatabase);
        var meshNames = new AbiffMeshNames(resourceDatabase);
        var effectCatalog = new GfxTweakCatalog(resourceDatabase);
        var effectHandler = new EffectHandler(effectCatalog, imageTextures, effectTextures);
        containerBuilder.RegisterValue(resourceDatabase);
        containerBuilder.RegisterValue(abiffMaterials);
        containerBuilder.RegisterValue(imageTextures);
        containerBuilder.RegisterValue(iconTextures);
        containerBuilder.RegisterValue(skinTextures);
        containerBuilder.RegisterValue(itemTemplates);
        containerBuilder.RegisterValue(meshNames);
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

        // The old inventory window is gone; IGameHud stays as the seam so InputController keeps
        // resolving, and the rebuilt window will supply the real implementation.
        containerBuilder.RegisterValue(new NullGameHud(), new System.Type[] { typeof(IGameHud) });
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

        // Worlds are spawned after the container exists, so the router is handed the container
        // through OnContainerBuilt rather than resolving it.
        var worldRouter = new WorldRouter(transform, _loginWorldPrefab, _gameWorldPrefab);
        containerBuilder.RegisterValue(worldRouter);
        containerBuilder.OnContainerBuilt += container =>
        {
            worldRouter.Bind(container);
            worldRouter.EnterLogin();
        };
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
