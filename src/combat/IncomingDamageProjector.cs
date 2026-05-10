using MegaCrit.Sts2.Core.Entities.Creatures;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    public static DamageBreakdown Calculate(Creature creature)
    {
        if (creature.CombatState == null) return default;

        var player = creature.Player;
        if (player?.PlayerCombatState == null) return default;

        var projection = new DamageProjection(creature);

        ApplyBeforeTurnEndProjection(projection, player);
        ApplyOrbProjection(projection, player);
        ApplyTurnEndHandProjection(projection, player);
        ApplyAfterTurnEndProjection(projection, player);

        if (!projection.SkipEnemyTurn)
        {
            ApplyEnemyTurnStartProjection(projection, creature);
            ApplyEnemyTurnProjection(projection, creature);
        }

        return projection.GetDamageBreakdown();
    }

}
