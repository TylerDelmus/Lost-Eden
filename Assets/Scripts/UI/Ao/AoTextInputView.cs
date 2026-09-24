using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>TextInputView_c : View</c> (GUI.dll). Note it derives from View, not
/// GUIControl_c — it is not one of the factory-registered controls.
///
/// <c>capture_enter</c> is stock's flag for swallowing Return instead of letting it fall
/// through to the chat line; the login form relies on it.
/// </summary>
[UxmlElement]
public partial class AoTextInputView : AoView
{
    readonly TextField _field;

    public event Action<string> ValueChanged;
    public event Action Submitted;

    public AoTextInputView()
    {
        AddToClassList("ao-text-input-view");
        _field = new TextField { isDelayed = false };
        _field.AddToClassList("ao-text-input-view__field");
        _field.RegisterValueChangedCallback(evt => ValueChanged?.Invoke(evt.newValue));
        // TrickleDown so Return is seen before the field commits focus, matching stock's
        // capture_enter behaviour.
        _field.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        Add(_field);
    }

    void OnKeyDown(KeyDownEvent evt)
    {
        bool isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter
                       || evt.character == '\n' || evt.character == '\r';
        if (!isEnter)
            return;

        Submitted?.Invoke();
        if (CaptureEnter)
            evt.StopImmediatePropagation();
    }

    /// <summary>Stock <c>value</c>: the current text.</summary>
    [UxmlAttribute("value")]
    public string Value
    {
        get => _field?.value;
        set { if (_field != null) _field.value = value ?? string.Empty; }
    }

    /// <summary>Stock <c>label</c>: caption drawn beside the field.</summary>
    [UxmlAttribute("label")]
    public string Label
    {
        get => _field?.label;
        set { if (_field != null) _field.label = value; }
    }

    /// <summary>Stock <c>capture_enter</c>: swallow Return rather than letting it propagate.</summary>
    [UxmlAttribute("capture_enter")]
    public bool CaptureEnter { get; set; }

    /// <summary>Stock <c>feature_flags</c>, kept raw until the bits are recovered.</summary>
    [UxmlAttribute("feature_flags")]
    public int FeatureFlags { get; set; }

    public bool IsPassword
    {
        get => _field != null && _field.isPasswordField;
        set { if (_field != null) _field.isPasswordField = value; }
    }

    public TextField Field => _field;
}
