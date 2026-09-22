/// <summary>
/// A <c>_GfxControlStars_t</c> case replayed call for call from stock, as <see cref="GfxControlStars"/>
/// sees it: the DiaBill sprite records to draw, drawn between the last two replayed steps, and the
/// terminate flag (+0x1648) that stops the spawns. No Unity dependency.
/// </summary>
public interface IStarsStockCase
{
    StarsCase3.Sprite[] Sprites { get; }

    /// <summary>Stock +0x1648.</summary>
    bool Terminating { get; set; }

    /// <summary>Terminating and the last step found nothing spawned or alive (<c>100fc2f0</c>).</summary>
    bool Drained { get; }

    /// <summary>Port-only: sprite <paramref name="i"/> a fraction <paramref name="t"/> of the way from the previous step.</summary>
    StarsCase3.Sprite Blend(int i, float t);
}
