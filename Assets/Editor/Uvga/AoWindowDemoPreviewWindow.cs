using Reflex.Core;
using Reflex.Extensions;
using Reflex.Injectors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Editor preview of the AO window shell + item container grid.
/// Prefer this over relying on UI Builder alone for UVGA-backed chrome.
/// </summary>
public sealed class AoWindowDemoPreviewWindow : EditorWindow
{
    const string ShellUxmlAssetPath = "Assets/Resources/UI/AoWindow.uxml";
    const string ContentUxmlAssetPath = "Assets/Resources/UI/ItemContainerGrid.uxml";
    const string CellUxmlAssetPath = "Assets/Resources/UI/ItemCell.uxml";
    const int PreviewCapacity = 30;

    [MenuItem("Lost Eden/Preview Ao Window Demo")]
    static void Open()
    {
        var window = GetWindow<AoWindowDemoPreviewWindow>("Ao Window Demo");
        window.minSize = new UnityEngine.Vector2(480f, 360f);
        window.Show();
    }

    [MenuItem("Lost Eden/Toggle Ao Window Demo (Play Mode)")]
    static void TogglePlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Ao Window Demo",
                "Enter Play Mode first, then use this menu to show/hide the demo window.",
                "OK");
            return;
        }

        InventoryView view = Object.FindFirstObjectByType<InventoryView>();
        if (view == null)
        {
            var go = new GameObject("InventoryWindow");
            go.SetActive(false);
            view = go.AddComponent<InventoryView>();
            try
            {
                Container container = go.scene.GetSceneContainer();
                GameObjectInjector.InjectObject(go, container);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Inventory] Could not inject dependencies: {ex.Message}");
            }

            go.SetActive(true);
        }

        view.Toggle();
    }

    void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexGrow = 1;

        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.paddingLeft = 6;
        toolbar.style.paddingRight = 6;
        toolbar.style.paddingTop = 4;
        toolbar.style.paddingBottom = 4;
        toolbar.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        var reloadBtn = new Button(Rebuild) { text = "Reload" };
        var pathLabel = new Label($"AO: {UvgaEditorPaths.ResolveAoPath()}");
        pathLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        pathLabel.style.flexGrow = 1;
        pathLabel.style.marginLeft = 8;
        toolbar.Add(reloadBtn);
        toolbar.Add(pathLabel);
        rootVisualElement.Add(toolbar);

        var host = new VisualElement();
        host.name = "preview-host";
        host.style.flexGrow = 1;
        rootVisualElement.Add(host);

        Rebuild();
    }

    void Rebuild()
    {
        VisualElement host = rootVisualElement.Q("preview-host");
        if (host == null)
            return;

        host.Clear();

        UvgaEditorBootstrap.ReloadFromPrefs();

        var shell = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShellUxmlAssetPath);
        if (shell == null)
        {
            host.Add(new Label($"Missing UXML at {ShellUxmlAssetPath}"));
            return;
        }

        TemplateContainer tree = shell.CloneTree();
        tree.style.flexGrow = 1;
        tree.style.width = Length.Percent(100);
        tree.style.height = Length.Percent(100);
        host.Add(tree);

        Label title = tree.Q<Label>("window-title");
        if (title != null)
            title.text = "Inventory";

        VisualElement body = tree.Q("window-body");
        var content = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ContentUxmlAssetPath);
        var cell = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CellUxmlAssetPath);
        if (body != null && content != null)
        {
            body.Clear();
            TemplateContainer contentTree = content.CloneTree();
            contentTree.style.flexGrow = 1;
            contentTree.style.width = Length.Percent(100);
            body.Add(contentTree);

            VisualElement grid = contentTree.Q("cell-grid") ?? contentTree;
            if (cell != null)
            {
                for (int i = 0; i < PreviewCapacity; i++)
                {
                    TemplateContainer cellTree = cell.CloneTree();
                    VisualElement cellRoot = cellTree.Q("cell") ?? cellTree;
                    grid.Add(cellRoot);
                }
            }
        }

        // Force UVGA elements to rebind after the tree is attached.
        host.schedule.Execute(() => UvgaTextureSource.RaiseChanged()).StartingIn(0);
    }
}
