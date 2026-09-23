using System;
using Xunit;

/// <summary>
/// Locks <see cref="GfxBits"/> to reading a field's raw dword.
///
/// These pass either way on desktop .NET, which keeps a signalling NaN's bits through a float. Unity's
/// Mono does not: it quiets one on the load, which is how GroundGrid 71230's 0xff80c0ff reached the
/// screen as 0xffc0c0ff. The point of the tests is that the value never goes through a float at all,
/// so they are written against arrays rather than floats to keep it that way.
/// </summary>
public class GfxBitsTests
{
    /// <summary>The bit patterns that get quieted: exponent all ones, bit 22 clear, mantissa non-zero.</summary>
    static bool IsSignalling(uint bits)
        => (bits & 0x7f800000u) == 0x7f800000u && (bits & 0x007fffffu) != 0 && (bits & 0x00400000u) == 0;

    static float[] FromBits(params uint[] words)
    {
        var f = new float[words.Length];
        var bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
            Buffer.BlockCopy(BitConverter.GetBytes(words[i]), 0, bytes, i * 4, 4);
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    [Fact]
    public void ReadsAFieldsRawDword()
    {
        float[] f = FromBits(0x00000014u, 0xff80c0ffu, 0x3f800000u);
        Assert.Equal(0x14, GfxBits.Of(f, 0));
        Assert.Equal(unchecked((int)0xff80c0ff), GfxBits.Of(f, 1));
        Assert.Equal(0xff80c0ffu, GfxBits.UOf(f, 1));
        Assert.Equal(0x3f800000, GfxBits.Of(f, 2));
    }

    [Fact]
    public void TheColoursThatBreakAreTheOnesMonoWouldQuiet()
    {
        // Bits 23..30 are red's top bit and alpha's low seven, so the pattern is exactly
        // "alpha 0x7f or 0xff, red 0x80..0xbf" — which quieting would push to 0xc0..0xff.
        Assert.True(IsSignalling(0xff80c0ffu));      // GroundGrid 71230
        Assert.True(IsSignalling(0x7f800001u));
        Assert.False(IsSignalling(0xffffffffu));     // already quiet: the white the mode 0 grids use
        Assert.False(IsSignalling(0xffc0c0ffu));     // what 71230 became
        Assert.False(IsSignalling(0xff7fc0ffu));     // red under 0x80 is an ordinary float
        Assert.False(IsSignalling(0xfe80c0ffu));     // alpha 0xfe breaks the exponent
        Assert.False(IsSignalling(0xff800000u));     // infinity, not a NaN
    }

    [Fact]
    public void OutOfRangeReadsAreZero()
    {
        float[] f = FromBits(0xff80c0ffu);
        Assert.Equal(0, GfxBits.Of(f, -1));
        Assert.Equal(0, GfxBits.Of(f, 1));
        Assert.Equal(0, GfxBits.Of(null, 0));
    }

    [Fact]
    public void TheRecordReadsItsFieldsTheSameWay()
    {
        var record = new GfxTweakRecord { Id = 71230, TypeCode = 0xbd6, Fields = FromBits(0xff80c0ffu) };
        Assert.Equal(unchecked((int)0xff80c0ff), record.FieldInt(0, 0));
        Assert.Equal(7, record.FieldInt(9, 7));
    }
}
