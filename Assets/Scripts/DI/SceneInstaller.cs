using Reflex.Core;
using UnityEngine;

public class SceneInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] PlayerController _playerController;
    [SerializeField] PlayfieldFactory _playfieldFactory;
    [SerializeField] LoadingScreenView _loadingScreenView;
    [SerializeField] WorldOverlayController _worldOverlayController;
    [SerializeField] AoWindowDemoView _inventoryWindowView;

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
        containerBuilder.RegisterValue(resourceDatabase);
        containerBuilder.RegisterValue(abiffMaterials);
        containerBuilder.RegisterValue(imageTextures);
        containerBuilder.RegisterValue(iconTextures);
        containerBuilder.RegisterValue(skinTextures);
        containerBuilder.RegisterValue(itemTemplates);
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

        _worldOverlayController ??= GetComponentInChildren<WorldOverlayController>(true);
        if (_worldOverlayController == null)
        {
            var overlayGo = new GameObject("WorldOverlay");
            overlayGo.transform.SetParent(transform, false);
            _worldOverlayController = overlayGo.AddComponent<WorldOverlayController>();
        }

        containerBuilder.RegisterValue(_worldOverlayController);

        _inventoryWindowView ??= GetComponentInChildren<AoWindowDemoView>(true);
        if (_inventoryWindowView == null)
        {
            var inventoryGo = new GameObject("InventoryWindow");
            inventoryGo.transform.SetParent(transform, false);
            _inventoryWindowView = inventoryGo.AddComponent<AoWindowDemoView>();
        }

        containerBuilder.RegisterValue(_inventoryWindowView);
        containerBuilder.RegisterValue(new UIInteractionManager(), new System.Type[] { typeof(IUINotifyService) });
    }
}
