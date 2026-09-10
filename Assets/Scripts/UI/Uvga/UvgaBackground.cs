using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// VisualElement whose background comes from a UVGA entry name (not a project Texture asset).
/// Paints via generateVisualContent so UI Builder cannot strip the texture.
/// </summary>
[UxmlElement]
public partial class UvgaBackground : VisualElement
{
    string _textureName = string.Empty;
    ScaleMode _scaleMode = ScaleMode.StretchToFill;
    Texture2D _texture;
    IVisualElementScheduledItem _applySchedule;

    public UvgaBackground()
    {
        AddToClassList("uvga-background");
        generateVisualContent += OnGenerateVisualContent;
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
    public ScaleMode scaleMode
    {
        get => _scaleMode;
        set
        {
            _scaleMode = value;
            MarkDirtyRepaint();
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
        _texture = null;

        if (!string.IsNullOrEmpty(_textureName)
            && UvgaTextureSource.TryGet(_textureName, out Texture2D texture)
            && texture != null)
        {
            _texture = texture;
        }

        // Keep style path for runtime panels; mesh paint covers UI Builder.
        if (_texture != null)
        {
            style.backgroundImage = new StyleBackground(_texture);
            style.unityBackgroundScaleMode = _scaleMode;
        }
        else
        {
            style.backgroundImage = StyleKeyword.None;
        }

        MarkDirtyRepaint();
    }

    void OnGenerateVisualContent(MeshGenerationContext mgc)
    {
        if (_texture == null)
            return;

        Rect rect = GetScaledContentRect(contentRect, _texture.width, _texture.height, _scaleMode);
        if (rect.width < 0.5f || rect.height < 0.5f)
            return;

        MeshWriteData mesh = mgc.Allocate(4, 6, _texture);
        Color32 tint = Color.white;

        mesh.SetNextVertex(new Vertex
        {
            position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ),
            tint = tint,
            uv = new Vector2(0f, 0f)
        });
        mesh.SetNextVertex(new Vertex
        {
            position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ),
            tint = tint,
            uv = new Vector2(1f, 0f)
        });
        mesh.SetNextVertex(new Vertex
        {
            position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ),
            tint = tint,
            uv = new Vector2(1f, 1f)
        });
        mesh.SetNextVertex(new Vertex
        {
            position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ),
            tint = tint,
            uv = new Vector2(0f, 1f)
        });

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    static Rect GetScaledContentRect(Rect content, float texW, float texH, ScaleMode mode)
    {
        if (texW <= 0f || texH <= 0f)
            return content;

        switch (mode)
        {
            case ScaleMode.ScaleToFit:
            {
                float scale = Mathf.Min(content.width / texW, content.height / texH);
                float w = texW * scale;
                float h = texH * scale;
                return new Rect(
                    content.x + (content.width - w) * 0.5f,
                    content.y + (content.height - h) * 0.5f,
                    w,
                    h);
            }
            case ScaleMode.ScaleAndCrop:
            {
                float scale = Mathf.Max(content.width / texW, content.height / texH);
                float w = texW * scale;
                float h = texH * scale;
                return new Rect(
                    content.x + (content.width - w) * 0.5f,
                    content.y + (content.height - h) * 0.5f,
                    w,
                    h);
            }
            default:
                return content;
        }
    }
}
