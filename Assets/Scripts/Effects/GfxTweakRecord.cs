/// <summary>
/// One gfxtweak.bin / CMSBlock template. Field meaning is per <see cref="TypeCode"/>.
/// </summary>
public sealed class GfxTweakRecord
{
    public const float InfiniteDuration = -1f;

    public int Id;
    public int TypeCode;
    public float[] Fields = System.Array.Empty<float>();

    /// <summary>Alias for <see cref="TypeCode"/> (legacy name).</summary>
    public int TypeTag
    {
        get => TypeCode;
        set => TypeCode = value;
    }

    public int FieldCount => Fields?.Length ?? 0;

    public float Field(int index, float fallback = 0f)
    {
        if (index < 0 || Fields == null || index >= Fields.Length)
            return fallback;
        return Fields[index];
    }

    public int FieldInt(int index, int fallback = 0)
    {
        if (index < 0 || Fields == null || index >= Fields.Length)
            return fallback;
        // Never through a float: see GfxBits for the signalling NaNs Mono would quiet.
        return GfxBits.Of(Fields, index);
    }

    /// <summary>Duration at index 8 (Nano0/Sprite family). −1 = infinite.</summary>
    public float Lifetime => Field(8, InfiniteDuration);

    public bool HasInfiniteDuration(int durationIndex = 8)
    {
        float d = Field(durationIndex, InfiniteDuration);
        return d < 0f;
    }
}
