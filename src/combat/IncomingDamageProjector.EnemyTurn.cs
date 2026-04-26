using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static void ApplyEnemyTurnProjection(DamageProjection projection, Creature playerCreature)
    {
        var combatState = playerCreature.CombatState;
        if (combatState == null)
            return;

        foreach (var enemy in combatState.HittableEnemies)
        {
            if (!projection.IsProjectedAlive(enemy))
                continue;

            var nextMove = enemy.Monster?.NextMove;
            if (nextMove == null)
                continue;

            foreach (var intent in nextMove.Intents)
            {
                if (intent.IntentType is not (IntentType.Attack or IntentType.DeathBlow))
                    continue;

                var attackIntent = (AttackIntent)intent;
                var repeats = Math.Max(1, attackIntent.Repeats);

                for (var hitIndex = 0; hitIndex < repeats; hitIndex++)
                {
                    if (!projection.IsProjectedAlive(playerCreature))
                        return;

                    ApplyProjectedAttackHit(projection, playerCreature, attackIntent, enemy);
                }
            }
        }
    }

    private static void ApplyEnemyTurnStartProjection(DamageProjection projection, Creature playerCreature)
    {
        var combatState = playerCreature.CombatState;
        if (combatState == null)
            return;

        foreach (var enemy in combatState.HittableEnemies)
        {
            if (!projection.IsProjectedAlive(enemy))
                continue;

            ApplyEnemyPoisonProjection(projection, enemy);
        }
    }

    private static void ApplyEnemyPoisonProjection(DamageProjection projection, Creature enemy)
    {
        var poison = enemy.GetPower<PoisonPower>();
        if (poison is not { Amount: > 0 })
            return;

        var combatState = enemy.CombatState;
        if (combatState == null)
            return;

        var triggerCount = 1 + combatState.GetOpponentsOf(enemy)
            .Where(projection.IsProjectedAlive)
            .Sum(opponent => opponent.GetPowerAmount<AccelerantPower>());
        var iterations = Math.Min(poison.Amount, triggerCount);

        for (var triggerIndex = 0; triggerIndex < iterations; triggerIndex++)
        {
            if (!projection.IsProjectedAlive(enemy))
                return;

            var damage = poison.Amount - triggerIndex;
            if (damage <= 0)
                return;

            ApplyProjectedDamage(
                projection,
                enemy,
                damage,
                ValueProp.Unblockable | ValueProp.Unpowered,
                null,
                null,
                ProjectedDamageSource.None
            );
        }
    }

}
