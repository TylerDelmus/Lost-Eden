using Reflex.Core;
using UnityEngine;

public class SceneInstaller : MonoBehaviour, IInstaller
{
    const string DefaultAoBasePath = @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";

    [SerializeField] PlayerController _playerController;
    [SerializeField] PlayfieldFactory _playfieldFactory;
    [SerializeField] LoadingScreenView _loadingScreenView;
    [SerializeField] string _aoBasePath = DefaultAoBasePath;

    public void InstallBindings(ContainerBuilder containerBuilder)
    {
        var resourceDatabase = new ResourceDatabase();
        resourceDatabase.Initialize(_aoBasePath);

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
