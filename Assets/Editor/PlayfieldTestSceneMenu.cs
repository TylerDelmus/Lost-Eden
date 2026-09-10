using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class PlayfieldTestSceneMenu
{
    const string ScenePath = "Assets/Scenes/PlayfieldTest.unity";

    [MenuItem("Lost Eden/Create Playfield Test Scene")]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var tester = new GameObject("PlayfieldTest");
        var host = tester.AddComponent<PlayfieldTest_DEV>();

        RenderConfig config = AssetDatabase.LoadAssetAtPath<RenderConfig>("Assets/Resources/RenderConfig.asset");
        if (config == null)
            config = Resources.Load<RenderConfig>("RenderConfig");

        if (config != null)
        {
            SerializedObject so = new SerializedObject(host);
            so.FindProperty("_renderConfig").objectReferenceValue = config;
            so.FindProperty("_playfieldId").intValue = 4310;
            so.FindProperty("_loadOnStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 40f, -80f);
            cam.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            if (cam.GetComponent<HDAdditionalCameraData>() == null)
                cam.gameObject.AddComponent<HDAdditionalCameraData>();
        }

        Light light = Object.FindAnyObjectByType<Light>();
        if (light != null && light.GetComponent<HDAdditionalLightData>() == null)
            light.gameObject.AddComponent<HDAdditionalLightData>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"Saved Playfield test scene to {ScenePath}");
        EditorSceneManager.OpenScene(ScenePath);
    }
}
