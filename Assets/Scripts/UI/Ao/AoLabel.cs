using UnityEngine.UIElements;

/// <summary>
/// The text element inside the Ao* views. It is a plain <see cref="Label"/> plus the one text
/// property USS lacks: case. UI Toolkit has no <c>text-transform</c>, and a skin that wants
/// upper-case captions should not need code for it, so the skin sets a custom property on the
/// label instead:
///
///   .ao-button__label { --ao-text-transform: uppercase; }
///
/// Values are <c>uppercase</c>, <c>lowercase</c> and <c>none</c>. Callers set
/// <see cref="RawText"/>; <see cref="Label.text"/> is what is drawn.
/// </summary>
public class AoLabel : Label
{
    static readonly CustomStyleProperty<string> TextTransformProperty = new("--ao-text-transform");

    string _raw = string.Empty;
    string _transform;

    public AoLabel()
    {
        pickingMode = PickingMode.Ignore;
        RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
    }

    /// <summary>The text as the view holds it, before the skin's case is applied.</summary>
    public string RawText
    {
        get => _raw;
        set
        {
            _raw = value ?? string.Empty;
            Apply();
        }
    }

    void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
    {
        evt.customStyle.TryGetValue(TextTransformProperty, out string transform);
        if (transform == _transform)
            return;

        _transform = transform;
        Apply();
    }

    void Apply()
    {
        text = _transform switch
        {
            "uppercase" => _raw.ToUpperInvariant(),
            "lowercase" => _raw.ToLowerInvariant(),
            _ => _raw
        };
    }
}
