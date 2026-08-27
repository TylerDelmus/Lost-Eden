using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CatAnimTestSceneMenu
{
    const string ScenePath = "Assets/Scenes/CatAnimTest.unity";

    [MenuItem("Lost Eden/Create CatAnim Test Scene")]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Default scene already has Main Camera + Directional Light.
        var tester = new GameObject("CatAnimTest");
        tester.AddComponent<CatAnimTest_DEV>();

        // Floor for scale reference.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.6f, -3.5f);
            cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"Saved CatAnim test scene to {ScenePath}");
        EditorSceneManager.OpenScene(ScenePath);
    }
}
