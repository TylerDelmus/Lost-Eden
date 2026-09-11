using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

public enum CursorState
{
    Default,
    Combat,
    Pickup
}

public enum CursorColor
{
    Standard,
    Blue,
    Gray,
    Green,
    Purple,
    Red,
    Yellow
}

public class CursorController : MonoBehaviour
{
    const string PointerStandard = "GFX_GUI_POINTER_STANDARD";

    [Header("Cursor")]
    [SerializeField] CursorColor color = CursorColor.Standard;
    [SerializeField] Vector2 hotspot = Vector2.zero;

    [Inject] UvgaTextureCache _uvgaTextures;

    readonly Dictionary<CursorColor, Texture2D> _cursorTextures = new Dictionary<CursorColor, Texture2D>();

    CursorState _currentState;
    CursorColor _appliedColor;

    public CursorColor Color
    {
        get => color;
        set => SetColor(value);
    }

    void OnEnable()
    {
        UvgaTextureSource.Changed += OnUvgaTexturesChanged;
        ApplyCursor();
    }

    void Start()
    {
        // Reflex inject can land after the first OnEnable.
        ApplyCursor();
    }

    void OnDisable()
    {
        UvgaTextureSource.Changed -= OnUvgaTexturesChanged;
    }

    void OnDestroy()
    {
        ClearCursorTextures();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
            return;

        ApplyCursor();
    }
#endif

    public void SetColor(CursorColor cursorColor)
    {
        if (color == cursorColor && _appliedColor == cursorColor)
            return;

        color = cursorColor;
        ApplyCursor();
    }

    public void SetCursor(CursorState state, bool force = false)
    {
        if (!force && state == _currentState && color == _appliedColor)
            return;

        _currentState = state;
        ApplyCursor();
    }

    void OnUvgaTexturesChanged()
    {
        ClearCursorTextures();
        ApplyCursor();
    }

    void ApplyCursor()
    {
        _appliedColor = color;

        if (_currentState == CursorState.Pickup)
        {
            Debug.LogWarning($"Cursor texture for state {_currentState} is not mapped to a UVGA pointer yet.");
            Cursor.SetCursor(null, hotspot, CursorMode.Auto);
            return;
        }

        CursorColor pointerColor = _currentState == CursorState.Combat ? CursorColor.Red : color;
        if (TryGetPointerTexture(pointerColor, out Texture2D cursorTexture))
            Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
        else
            Cursor.SetCursor(null, hotspot, CursorMode.Auto);
    }

    bool TryGetPointerTexture(CursorColor cursorColor, out Texture2D texture)
    {
        if (_cursorTextures.TryGetValue(cursorColor, out texture) && texture != null)
            return true;

        if (_uvgaTextures == null || !_uvgaTextures.TryGet(GetUvgaName(cursorColor), out Texture2D source) || source == null)
        {
            texture = null;
            return false;
        }

        texture = CreateCursorTexture(source);
        _cursorTextures[cursorColor] = texture;
        return texture != null;
    }

    static string GetUvgaName(CursorColor cursorColor)
    {
        switch (cursorColor)
        {
            case CursorColor.Blue: return PointerStandard + "_BLUE";
            case CursorColor.Gray: return PointerStandard + "_GRAY";
            case CursorColor.Green: return PointerStandard + "_GREEN";
            case CursorColor.Purple: return PointerStandard + "_PURPLE";
            case CursorColor.Red: return PointerStandard + "_RED";
            case CursorColor.Yellow: return PointerStandard + "_YELLOW";
            default: return PointerStandard;
        }
    }

    static Texture2D CreateCursorTexture(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            // AO UVGA pointers are RGB PNGs keyed with #00FF00.
            if (pixel.r == 0 && pixel.g == 255 && pixel.b == 0)
                pixels[i] = new Color32(0, 0, 0, 0);
            else
            {
                pixel.a = 255;
                pixels[i] = pixel;
            }
        }

        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false);
        texture.name = source.name + "_Cursor";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return texture;
    }

    void ClearCursorTextures()
    {
        foreach (var pair in _cursorTextures)
            UvgaTextureDecoder.DestroyTexture(pair.Value);

        _cursorTextures.Clear();
    }
}
