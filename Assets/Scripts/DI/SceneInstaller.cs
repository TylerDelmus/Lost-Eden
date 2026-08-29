using Reflex.Core;
using UnityEngine;

public class SceneInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] PlayerController _playerController;
    [SerializeField] PlayfieldFactory _playfieldFactory;
    [SerializeField] LoadingScreenView _loadingScreenView;

    public void InstallBindings(ContainerBuilder containerBuilder)
    {
        var resourceDatabase = new ResourceDatabase();

        string aoPath = LoginPreferences.GetAoPath();
        if (AoInstallPath.IsValid(aoPath))
            resourceDatabase.Initialize(AoInstallPath.Normalize(aoPath));

        var abiffMaterials = new AbiffMaterialFactory(resourceDatabase);
        var catMeshMaterials = new CatMeshMaterialFactory(abiffMaterials);
        var imageTextures = new AoImageTextureCache(resourceDatabase);
        var skinTextures = new SkinTextureResolver(resourceDatabase);
        containerBuilder.RegisterValue(resourceDatabase);
        containerBuilder.RegisterValue(abiffMaterials);
        containerBuilder.RegisterValue(imageTextures);
        containerBuilder.RegisterValue(skinTextures);
        containerBuilder.RegisterValue(new AbiffLoader(resourceDatabase, abiffMaterials, imageTextures));
        containerBuilder.RegisterValue(new CatMeshLoader(resourceDatabase, catMeshMaterials));
        containerBuilder.RegisterValue(_playfieldFactory);
        containerBuilder.RegisterValue(new NetworkClient(new NetworkConfig { AutoReconnect = false }));
        containerBuilder.RegisterValue(_playerController);

        _loadingScreenView ??= GetComponentInChildren<LoadingScreenView>(true);
        containerBuilder.RegisterValue(new LoadingScreen(_loadingScreenView, resourceDatabase));

        containerBuilder.RegisterValue(new UIInteractionManager(), new System.Type[] { typeof(IUINotifyService) });
    }
}
