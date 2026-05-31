namespace MintySpire2.combat;

internal enum ProjectedDamageSource
{
    None,
    Enemy,
    Self
}
internal readonly record struct DamageBreakdown(int EnemyDamage, int SelfDamage, bool IsLethal = false, bool IsRevived = false)
{
    public int Total => EnemyDamage + SelfDamage;
    public bool HasDamage => Total > 0;

    public string ToDisplayText()
    {
        if (!HasDamage)
            return string.Empty;

        return IsRevived switch
        {
            true => $"REVIVE\nA{EnemyDamage} S{SelfDamage}",
            false when IsLethal => $"KO\nA{EnemyDamage} S{SelfDamage}",
            _ => $"{Total}\nA{EnemyDamage} S{SelfDamage}"
        };
    }
}
