using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class GfxTestSceneMenu
{
    const string ScenePath = "Assets/Scenes/GfxTest.unity";

    [MenuItem("Lost Eden/Open GFX Test Scene")]
    public static void OpenScene()
    {
        if (!File.Exists(ScenePath))
            CreateScene();
        else
            EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Lost Eden/Create GFX Test Scene")]
    public static void CreateScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var tester = new GameObject("GfxTest");
        tester.AddComponent<GfxTest_DEV>();

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "SpawnMarker";
        marker.transform.position = new Vector3(0f, 1.2f, 0f);
        marker.transform.localScale = Vector3.one * 0.15f;
        Object.DestroyImmediate(marker.GetComponent<Collider>());

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.6f, -4f);
            cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            if (cam.GetComponent<HDAdditionalCameraData>() == null)
                cam.gameObject.AddComponent<HDAdditionalCameraData>();
        }

        Light light = Object.FindAnyObjectByType<Light>();
        if (light != null && light.GetComponent<HDAdditionalLightData>() == null)
            light.gameObject.AddComponent<HDAdditionalLightData>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"Saved GFX test scene to {ScenePath}");
        EditorSceneManager.OpenScene(ScenePath);
    }
}
