using System;

/// <summary>
/// Reads a gfxtweak field's raw 32 bits.
///
/// Stock keeps a record's fields as untyped dwords and reinterprets each one per its type, so a field
/// that holds a packed ARGB or a count is never a float at all. The port keeps them in a
/// <c>float[]</c>, which is fine for every pattern but one: 71 of the 75,221 shipped fields are
/// *signalling* NaNs (exponent all ones, mantissa bit 22 clear, mantissa non-zero), and Unity's Mono
/// quiets a signalling NaN the moment the value is loaded into a float — it sets bit 22 and hands back
/// a different number. Reinterpreting <c>f[i]</c> through BitConverter therefore returns the wrong
/// dword, and so does passing it to a method that takes a float. .NET on the desktop keeps the bits,
/// which is why the unit tests never saw it and only a live probe did.
///
/// For a packed ARGB, bits 23..30 are the red byte's top bit plus the alpha byte's low seven, so the
/// pattern is exactly "alpha 0x7f or 0xff, red 0x80..0xbf" — and quieting turns that red into
/// 0xc0..0xff. GroundGrid's 0xff80c0ff came back as 0xffc0c0ff, a visibly wrong colour, in 11 record
/// types (GroundGrid, BParticle, Cord, Fire and 7 more).
///
/// <see cref="Buffer.BlockCopy"/> moves the bytes without ever putting them in a float register, so it
/// is the one way to get the dword back intact.
/// </summary>
public static class GfxBits
{
    [ThreadStatic] static int[] _scratch;

    /// <summary>The field's raw bits, or 0 when the index is out of range.</summary>
    public static int Of(float[] fields, int index)
    {
        if (fields == null || index < 0 || index >= fields.Length)
            return 0;
        int[] scratch = _scratch ??= new int[1];
        Buffer.BlockCopy(fields, index * 4, scratch, 0, 4);
        return scratch[0];
    }

    /// <summary>The field's raw bits as unsigned, for the packed colours.</summary>
    public static uint UOf(float[] fields, int index) => unchecked((uint)Of(fields, index));
}
