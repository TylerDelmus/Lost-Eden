public readonly struct HitIndicatorInfo
{
    public readonly int Damage;
    public readonly bool IsCrit;

    public HitIndicatorInfo(int damage, bool isCrit)
    {
        Damage = damage;
        IsCrit = isCrit;
    }
}
