using AODB.Common.RDBObjects;

/// <summary>
/// A nano program loaded from a single RDB NanoObject id (never interpolated).
/// </summary>
public sealed class NanoSpell
{
    public int Id { get; }
    public ItemBase Template { get; }

    public NanoSpell(int id, ItemBase template)
    {
        Id = id;
        Template = template;
    }
}
