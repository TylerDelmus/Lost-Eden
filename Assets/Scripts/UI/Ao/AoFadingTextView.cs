using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Port of stock <c>FadingTextView_c : TimerObjectBase_t, TextView_c</c> (GUI.dll) — a text
/// view whose content fades out after a delay. Stock gets the timing from TimerObjectBase_t;
/// here the tick is driven by UI Toolkit's scheduler, which is the equivalent seam.
///
/// <c>InventoryView_c</c> constructs one for its transient status line.
/// </summary>
[UxmlElement]
public partial class AoFadingTextView : AoTextView
{
    IVisualElementScheduledItem _fade;

    /// <summary>Seconds the text stays fully opaque before the fade starts.</summary>
    [UxmlAttribute("hold_seconds")]
    public float HoldSeconds { get; set; } = 3f;

    /// <summary>Seconds the fade itself takes.</summary>
    [UxmlAttribute("fade_seconds")]
    public float FadeSeconds { get; set; } = 1f;

    /// <summary>Sets the text and restarts the hold-then-fade cycle.</summary>
    public void Show(string text)
    {
        Value = text;
        style.opacity = 1f;
        Restart();
    }

    void Restart()
    {
        _fade?.Pause();

        float hold = Mathf.Max(0f, HoldSeconds);
        float fade = Mathf.Max(0.001f, FadeSeconds);
        float startedAt = Time.unscaledTime;

        // Driven by real elapsed time, not by a tick count: the 16 ms here is only the polling
        // cadence, so the fade takes the same wall-clock time at any frame rate. See the
        // project's unify-frame-rate rule.
        _fade = schedule.Execute(() =>
        {
            float elapsed = Time.unscaledTime - startedAt;
            if (elapsed < hold)
                return;

            float t = (elapsed - hold) / fade;
            style.opacity = Mathf.Clamp01(1f - t);

            if (t >= 1f)
                _fade.Pause();
        }).Every(16);
    }
}
