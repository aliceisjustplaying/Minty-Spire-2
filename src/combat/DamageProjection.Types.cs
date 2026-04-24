namespace MintySpire2.combat;

internal enum ProjectedDamageSource
{
    None,
    Enemy,
    Self
}
internal readonly record struct DamageBreakdown(int EnemyDamage, int SelfDamage)
{
    public int Total => EnemyDamage + SelfDamage;
    public bool HasDamage => Total > 0;

    public string ToDisplayText()
    {
        return HasDamage ? $"{Total}\nA{EnemyDamage} S{SelfDamage}" : string.Empty;
    }
}
