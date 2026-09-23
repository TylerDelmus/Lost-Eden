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
        bool stretchContentRoot = true,
        bool centerPanelRoot = true)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));

        string name = logName ?? uxmlResourcePath;

        var asset = Resources.Load<VisualTreeAsset>(uxmlResourcePath);
        if (asset == null)
        {
            Debug.LogError($"[UserInterface] Missing VisualTreeAsset at Resources/{uxmlResourcePath} ({name})");
            return null;
        }

        var renderer = host.GetComponent<PanelRenderer>();
        if (renderer == null)
            renderer = host.gameObject.AddComponent<PanelRenderer>();

        // Asset before panel settings: assigning panelSettings rebuilds the root, and the
        // tree is only cloned into it if the asset is already there.
        renderer.visualTreeAsset = asset;
        renderer.sortingOrder = sortOrder;
        EnsurePanelSettings(renderer, sortOrder);

        var menu = new UiMenu(host, renderer, uxmlResourcePath, name, stretchContentRoot, centerPanelRoot, startVisible);
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


    public static void EnsurePanelSettings(PanelRenderer renderer, int sortingOrder)
    {
        renderer.panelSettings = GetOrCreatePanelSettings(sortingOrder);
    }

    static PanelSettings GetOrCreatePanelSettings(int sortingOrder)
    {
        // The cache outlives a play session when Reload Domain is off, but the instances
        // themselves are destroyed on exit. Unity's == catches those; a plain TryGetValue
        // would hand back a dead object and every panel would render nothing.
        if (PanelSettingsBySortOrder.TryGetValue(sortingOrder, out PanelSettings existing) && existing != null)
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

    internal static void CenterContent(VisualElement panelRoot)
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

/// <summary>
/// A panel loaded through <see cref="UserInterface.Load"/>.
///
/// PanelRenderer exposes no public rootVisualElement: the root only arrives through
/// RegisterUIReloadCallback, which fires once the panel is attached rather than during
/// Awake. So <see cref="Root"/> is null to begin with, callers bind through
/// <see cref="WhenReady"/>, and the wanted visibility is held here and applied on arrival.
/// </summary>
public sealed class UiMenu : IDisposable
{
    readonly MonoBehaviour _host;
    readonly string _name;
    readonly PanelRenderer _renderer;
    readonly string _uxmlResourcePath;
    readonly bool _stretchContentRoot;
    readonly bool _centerPanelRoot;

    Action<VisualElement> _onReady;
    Coroutine _fadeRoutine;
    bool _visible;

    public PanelRenderer Renderer => _renderer;
    public VisualElement PanelRoot { get; private set; }
    public VisualElement Root { get; private set; }
    public string Name => _name;

    public bool IsReady => Root != null;
    public bool IsVisible => _visible;

    internal UiMenu(
        MonoBehaviour host,
        PanelRenderer renderer,
        string uxmlResourcePath,
        string name,
        bool stretchContentRoot,
        bool centerPanelRoot,
        bool startVisible)
    {
        _host = host;
        _renderer = renderer;
        _uxmlResourcePath = uxmlResourcePath;
        _name = name;
        _stretchContentRoot = stretchContentRoot;
        _centerPanelRoot = centerPanelRoot;
        _visible = startVisible;

        _renderer.RegisterUIReloadCallback(OnUiReload);
    }

    void OnUiReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        PanelRoot = root;
        Root = root.Q<VisualElement>("root") ?? root;

        UserInterface.StretchToScreen(PanelRoot);
        if (_centerPanelRoot)
            UserInterface.CenterContent(PanelRoot);

        if (_stretchContentRoot)
            UserInterface.StretchToScreen(Root);

        UserInterface.EnsureStylesheet(Root, _uxmlResourcePath);
        ApplyVisibility();

        Action<VisualElement> callbacks = _onReady;
        _onReady = null;
        callbacks?.Invoke(Root);
    }

    /// <summary>Runs <paramref name="onReady"/> now if the root already arrived, else when it does.</summary>
    public void WhenReady(Action<VisualElement> onReady)
    {
        if (onReady == null)
            return;

        if (IsReady)
            onReady(Root);
        else
            _onReady += onReady;
    }

    public T Q<T>(string elementName) where T : VisualElement
    {
        return Root?.Q<T>(elementName);
    }

    public VisualElement Q(string elementName)
    {
        return Root?.Q(elementName);
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
        _visible = true;
        ApplyVisibility();
    }

    public void Hide()
    {
        StopFade();
        _visible = false;
        ApplyVisibility();
    }

    public void HideFade(Action onComplete = null)
    {
        HideFade(1f, onComplete);
    }

    public void HideFade(float duration, Action onComplete = null)
    {
        if (!_visible || !IsReady)
        {
            Hide();
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
        _onReady = null;
        _visible = false;

        if (_renderer != null)
            _renderer.UnregisterUIReloadCallback(OnUiReload);

        if (Root != null)
            UserInterface.SetVisible(Root, false);
    }

    void ApplyVisibility()
    {
        if (Root == null)
            return;

        UserInterface.SetOpacity(Root, 1f);
        UserInterface.SetVisible(Root, _visible);
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

        _fadeRoutine = null;
        _visible = false;
        ApplyVisibility();
        onComplete?.Invoke();
    }
}
