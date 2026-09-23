using UnityEngine;

public struct AbiffUvKey
{
    public Vector2 Offset;
    public Vector2 Tiling;
    public float Time;

    /// <summary>
    /// The key's fourth word (AODB's <c>UVKey.Unk2</c>): when set, randy31's <c>FAFAnim_t</c> evaluate
    /// (<c>10028fde</c>) lerps the offset towards the next key; when clear the offset holds.
    /// </summary>
    public bool Lerp;
}
