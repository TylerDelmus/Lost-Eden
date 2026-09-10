using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Image backed by a UVGA entry name (not a project Texture asset).
/// Uses <see cref="Image"/> so the texture renders reliably in UI Builder and at runtime
/// (unlike style.backgroundImage, which the Builder often clears).
/// </summary>
[UxmlElement]
public partial class UvgaImage : Image
{
    string _textureName = string.Empty;
    bool _sizeToTexture = true;
    IVisualElementScheduledItem _applySchedule;

    public UvgaImage()
    {
        AddToClassList("uvga-image");
        style.flexShrink = 0;
        scaleMode = ScaleMode.ScaleToFit;
        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
        RegisterCallback<CustomStyleResolvedEvent>(_ => ApplyTexture());
    }

    [UxmlAttribute("texture-name")]
    public string textureName
    {
        get => _textureName;
        set
        {
            _textureName = value ?? string.Empty;
            ApplyTexture();
        }
    }

    [UxmlAttribute("scale-mode")]
    public ScaleMode TextureScaleMode
    {
        get => scaleMode;
        set => scaleMode = value;
    }

    [UxmlAttribute("size-to-texture")]
    public bool sizeToTexture
    {
        get => _sizeToTexture;
        set
        {
            _sizeToTexture = value;
            ApplyTexture();
        }
    }

    void OnAttach(AttachToPanelEvent evt)
    {
        UvgaTextureSource.Changed += ApplyTexture;
        ApplyTexture();
        _applySchedule?.Pause();
        _applySchedule = schedule.Execute(ApplyTexture).StartingIn(0);
    }

    void OnDetach(DetachFromPanelEvent evt)
    {
        UvgaTextureSource.Changed -= ApplyTexture;
        _applySchedule?.Pause();
        _applySchedule = null;
    }

    void ApplyTexture()
    {
        if (string.IsNullOrEmpty(_textureName))
        {
            image = null;
            return;
        }

        if (!UvgaTextureSource.TryGet(_textureName, out Texture2D texture) || texture == null)
        {
            image = null;
            return;
        }

        image = texture;

        if (!_sizeToTexture)
            return;

        float width = texture.width;
        float height = texture.height;
        style.width = width;
        style.height = height;
        style.minWidth = width;
        style.minHeight = height;
        style.flexShrink = 0;
        MarkDirtyRepaint();
    }
}
