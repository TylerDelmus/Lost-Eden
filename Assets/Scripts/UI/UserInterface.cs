using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class UserInterface
{
    const string DefaultThemeResourcePath = "UI/UnityDefaultRuntimeTheme";
    const string DefaultPanelSettingsResourcePath = "UI/DefaultPanelSettings";
#if UNITY_EDITOR
    internal const string DefaultThemeAssetPath = "Assets/Resources/UI/UnityDefaultRuntimeTheme.tss";
    internal const string DefaultPanelSettingsAssetPath = "Assets/Resources/UI/DefaultPanelSettings.asset";
#endif

    public static readonly Color DefaultFieldBackground = new(0.15f, 0.17f, 0.22f);
    public static readonly Color DefaultTextColor = Color.white;

    static readonly List<UiMenu> ActiveMenus = new();
    static readonly Dictionary<int, PanelSettings> PanelSettingsBySortOrder = new();
    static ThemeStyleSheet _defaultTheme;

    public static IReadOnlyList<UiMenu> Menus => ActiveMenus;

    public static UiMenu Load(
        MonoBehaviour host,
        string uxmlResourcePath,
        int sortOrder,
        bool startVisible = false,
        string logName = null,
        bool stretchContentRoot = true)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));

        string name = logName ?? uxmlResourcePath;
        var document = host.GetComponent<UIDocument>();
        if (document == null)
            document = host.gameObject.AddComponent<UIDocument>();

        EnsurePanelSettings(document, sortOrder);

        var asset = Resources.Load<VisualTreeAsset>(uxmlResourcePath);
        if (asset == null)
        {
            Debug.LogError($"[UserInterface] Missing VisualTreeAsset at Resources/{uxmlResourcePath} ({name})");
            return null;
        }

        document.visualTreeAsset = asset;

        VisualElement panelRoot = document.rootVisualElement;
        VisualElement contentRoot = panelRoot.Q<VisualElement>("root") ?? panelRoot;

        StretchToScreen(panelRoot);
        CenterContent(panelRoot);

        if (stretchContentRoot)
            StretchToScreen(contentRoot);

        EnsureStylesheet(contentRoot, uxmlResourcePath);

        var menu = new UiMenu(host, document, contentRoot, name);
        menu.SetVisible(startVisible);
        ActiveMenus.Add(menu);
        return menu;
    }

    public static T FindOrCreateMenuView<T>(Transform parent, string childName) where T : Component
    {
        var view = parent.GetComponentInChildren<T>(true);
        if (view != null)
            return view;

        var child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        return child.AddComponent<T>();
    }

    public static VisualTreeAsset LoadTemplate(string resourcePath)
    {
        return Resources.Load<VisualTreeAsset>(resourcePath);
    }

    public static void Unregister(UiMenu menu)
    {
        if (menu == null)
            return;

        menu.Dispose();
        ActiveMenus.Remove(menu);
    }

    public static void EnsurePanelSettings(UIDocument document, int sortingOrder)
    {
        PanelSettings panelSettings = GetOrCreatePanelSettings(sortingOrder);
        document.panelSettings = panelSettings;
    }

    static PanelSettings GetOrCreatePanelSettings(int sortingOrder)
    {
        if (PanelSettingsBySortOrder.TryGetValue(sortingOrder, out PanelSettings existing))
            return existing;

        PanelSettings panelSettings = CreatePanelSettingsInstance();
        panelSettings.sortingOrder = sortingOrder;
        PanelSettingsBySortOrder[sortingOrder] = panelSettings;
        return panelSettings;
    }

    static PanelSettings CreatePanelSettingsInstance()
    {
        PanelSettings template = LoadPanelSettingsTemplate();
        if (template != null)
            return ScriptableObject.Instantiate(template);

        ThemeStyleSheet theme = LoadDefaultTheme();
        if (theme == null)
            Debug.LogError("[UserInterface] No Theme Style Sheet available. UI may not render properly.");

        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;
        panelSettings.themeStyleSheet = theme;
        panelSettings.textSettings = LoadDefaultTextSettings();
        return panelSettings;
    }

    static PanelSettings LoadPanelSettingsTemplate()
    {
#if UNITY_EDITOR
        PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(DefaultPanelSettingsAssetPath);
        if (asset != null)
            return asset;
#endif
        return Resources.Load<PanelSettings>(DefaultPanelSettingsResourcePath);
    }

    static PanelTextSettings LoadDefaultTextSettings()
    {
#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:PanelTextSettings");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<PanelTextSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        return null;
    }

    public static void StretchToScreen(VisualElement element)
    {
        element.style.flexGrow = 1;
        element.style.width = Length.Percent(100);
        element.style.height = Length.Percent(100);
    }

    static void CenterContent(VisualElement panelRoot)
    {
        panelRoot.style.alignItems = Align.Center;
        panelRoot.style.justifyContent = Justify.Center;
    }

    public static void EnsureStylesheet(VisualElement root, string resourcePath)
    {
        var styleSheet = Resources.Load<StyleSheet>(resourcePath);
        if (styleSheet == null || root.styleSheets.Contains(styleSheet))
            return;

        root.styleSheets.Add(styleSheet);
    }

    public static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null)
            return;

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        element.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
    }

    public static void SetOpacity(VisualElement element, float opacity)
    {
        if (element == null)
            return;

        element.style.opacity = opacity;
    }

    public static void StyleLabel(Label label, Color? color = null)
    {
        if (label == null)
            return;

        label.style.color = color ?? DefaultTextColor;
    }

    public static void StyleTextField(
        TextField field,
        Color? background = null,
        Color? textColor = null,
        int fontSize = 18)
    {
        if (field == null)
            return;

        Color bg = background ?? DefaultFieldBackground;
        Color text = textColor ?? DefaultTextColor;

        field.SetEnabled(true);
        field.style.backgroundColor = bg;
        field.style.color = text;
        field.style.fontSize = fontSize;

        TextElement textElement = field.Q<TextElement>();
        if (textElement != null)
            textElement.style.color = text;

        VisualElement input = field.Q(className: "unity-base-text-field__input");
        if (input != null)
        {
            input.style.backgroundColor = bg;
            input.style.color = text;
        }
    }

    public static void StyleDropdown(
        DropdownField dropdown,
        Color? background = null,
        Color? textColor = null,
        int fontSize = 18)
    {
        if (dropdown == null)
            return;

        Color bg = background ?? DefaultFieldBackground;
        Color text = textColor ?? DefaultTextColor;

        dropdown.SetEnabled(true);
        dropdown.style.backgroundColor = bg;
        dropdown.style.color = text;
        dropdown.style.fontSize = fontSize;

        Label popupText = dropdown.Q<Label>(className: "unity-base-popup-field__text");
        if (popupText != null)
            popupText.style.color = text;
    }

    public static void StyleButton(Button button, Color? textColor = null)
    {
        if (button == null)
            return;

        button.style.backgroundColor = new Color(51f / 255f, 115f / 255f, 191f / 255f);
        button.style.color = textColor ?? DefaultTextColor;
    }

    public static void StyleDisabledButton(Button button)
    {
        if (button == null)
            return;

        button.style.backgroundColor = new Color(40f / 255f, 70f / 255f, 110f / 255f);
        button.style.color = new Color(1f, 1f, 1f, 0.5f);
    }

    public static ThemeStyleSheet LoadDefaultTheme()
    {
        if (_defaultTheme != null)
            return _defaultTheme;

#if UNITY_EDITOR
        _defaultTheme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(DefaultThemeAssetPath);

        if (_defaultTheme == null)
            _defaultTheme = ImportEditorThemeAsset();

        if (_defaultTheme == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ThemeStyleSheet"))
            {
                _defaultTheme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                if (_defaultTheme != null)
                    break;
            }
        }
#endif

        if (_defaultTheme == null)
            _defaultTheme = Resources.Load<ThemeStyleSheet>(DefaultThemeResourcePath);

        return _defaultTheme;
    }

#if UNITY_EDITOR
    static ThemeStyleSheet ImportEditorThemeAsset()
    {
        if (!File.Exists(DefaultThemeAssetPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultThemeAssetPath)!);
            File.WriteAllText(DefaultThemeAssetPath, "@import url(\"unity-theme://default\");\n");
        }

        AssetDatabase.ImportAsset(DefaultThemeAssetPath, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(DefaultThemeAssetPath);
    }
#endif
}

public sealed class UiMenu : IDisposable
{
    readonly MonoBehaviour _host;
    readonly string _name;

    Coroutine _fadeRoutine;

    public UIDocument Document { get; }
    public VisualElement Root { get; }
    public string Name => _name;

    public bool IsVisible => Root.style.display != DisplayStyle.None;

    internal UiMenu(MonoBehaviour host, UIDocument document, VisualElement contentRoot, string name)
    {
        _host = host;
        Document = document;
        Root = contentRoot;
        _name = name;
    }

    public T Q<T>(string elementName) where T : VisualElement
    {
        return Root.Q<T>(elementName);
    }

    public VisualElement Q(string elementName)
    {
        return Root.Q(elementName);
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            Show();
        else
            Hide();
    }

    public void Show()
    {
        StopFade();
        UserInterface.SetOpacity(Root, 1f);
        UserInterface.SetVisible(Root, true);
    }

    public void Hide()
    {
        StopFade();
        UserInterface.SetVisible(Root, false);
        UserInterface.SetOpacity(Root, 1f);
    }

    public void HideFade(Action onComplete = null)
    {
        HideFade(1f, onComplete);
    }

    public void HideFade(float duration, Action onComplete = null)
    {
        if (!IsVisible)
        {
            onComplete?.Invoke();
            return;
        }

        StopFade();
        _fadeRoutine = _host.StartCoroutine(FadeOut(duration, onComplete));
    }

    public void StopFade()
    {
        if (_fadeRoutine == null)
            return;

        _host.StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
    }

    public void Dispose()
    {
        StopFade();
        UserInterface.SetVisible(Root, false);
    }

    IEnumerator FadeOut(float duration, Action onComplete)
    {
        float elapsed = 0f;
        Root.pickingMode = PickingMode.Ignore;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            UserInterface.SetOpacity(Root, Mathf.Lerp(1f, 0f, elapsed / duration));
            yield return null;
        }

        Hide();
        _fadeRoutine = null;
        onComplete?.Invoke();
    }
}
