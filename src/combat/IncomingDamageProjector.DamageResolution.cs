using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static void AddProjectedBlock(DamageProjection projection, Creature creature, decimal amount, ValueProp props)
    {
        if (creature.CombatState == null || amount <= 0)
            return;

        var modifiedBlock = Hook.ModifyBlock(creature.CombatState, creature, amount, props, null, null, out _);
        projection.AddProjectedBlock(creature, Math.Max(0, (int)modifiedBlock));
    }

    private static void ApplyProjectedDamageToEnemies(DamageProjection projection, decimal amount, ValueProp props, Creature dealer)
    {
        var targets = projection.GetProjectedHittableEnemies();
        foreach (var target in targets)
            ApplyProjectedDamage(projection, target, amount, props, dealer, null, ProjectedDamageSource.None);
    }

    private static void ApplyProjectedAttackHit(DamageProjection projection, Creature target, AttackIntent attackIntent, Creature dealer)
    {
        var damagePerHit = attackIntent.GetSingleDamage([target], dealer);
        if (projection.ShouldApplyDiamondDiademProtection)
            damagePerHit = (int)(damagePerHit * 0.5m);

        ApplyProjectedResolvedDamage(projection, target, damagePerHit, ValueProp.Move, dealer, null, ProjectedDamageSource.Enemy);
    }

    private static void ApplyProjectedDamage(
        DamageProjection projection,
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        ProjectedDamageSource source
    )
    {
        if (target.CombatState == null || !projection.IsProjectedAlive(target) || amount <= 0)
            return;

        var runState = IRunState.GetFrom(new[] { target, dealer }.OfType<Creature>());
        var modifiedDamage = Hook.ModifyDamage(
            runState,
            target.CombatState,
            target,
            dealer,
            amount,
            props,
            cardSource,
            ModifyDamageHookType.All,
            CardPreviewMode.None,
            out _
        );

        ApplyProjectedResolvedDamage(projection, target, modifiedDamage, props, dealer, cardSource, source);
    }

    private static void ApplyProjectedResolvedDamage(
        DamageProjection projection,
        Creature target,
        decimal modifiedDamage,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        ProjectedDamageSource source
    )
    {
        if (target.CombatState == null || !projection.IsProjectedAlive(target) || modifiedDamage <= 0)
            return;

        ApplyProjectedBeforeDamageReceivedReactions(projection, target, modifiedDamage, props, dealer, cardSource, source);

        var blockTarget = target.PetOwner?.Creature ?? target;
        var blockedDamage = props.HasFlag(ValueProp.Unblockable)
            ? 0m
            : Math.Min(projection.GetProjectedBlock(blockTarget), modifiedDamage);

        projection.SpendProjectedBlock(blockTarget, (int)blockedDamage);

        var unblockedDamage = Math.Max(modifiedDamage - blockedDamage, 0m);
        var hpLoss = ApplyProjectedHpLossModifiersBeforeOsty(projection, target, unblockedDamage, props, dealer, cardSource);

        var hpLossTarget = GetProjectedUnblockedDamageTarget(projection, target, hpLoss, props);
        var resolvedUnblockedDamage = ClampProjectedHpLoss(unblockedDamage);

        if (hpLoss <= 0)
        {
            projection.TrackProjectedUnblockedDamage(hpLossTarget, resolvedUnblockedDamage);
            if (projection.IsProjectedAlive(target))
                ApplyProjectedAfterDamageReceivedReactions(projection, target, (int)blockedDamage, props, dealer, source);
            return;
        }

        hpLoss = ApplyProjectedHpLossModifiersAfterOsty(projection, hpLossTarget, hpLoss, props, dealer, cardSource);

        var resolvedHpLoss = ClampProjectedHpLoss(hpLoss);
        projection.TrackProjectedThreat(hpLossTarget, resolvedHpLoss, source);
        projection.TrackProjectedUnblockedDamage(hpLossTarget, resolvedUnblockedDamage);
        var overkillDamage = projection.ApplyProjectedHpLoss(hpLossTarget, resolvedHpLoss, source);
        if (hpLossTarget == target || overkillDamage <= 0)
        {
            if (projection.IsProjectedAlive(target))
                ApplyProjectedAfterDamageReceivedReactions(projection, target, (int)blockedDamage, props, dealer, source);
            return;
        }

        var overkillHpLoss = ApplyProjectedHpLossModifiersAfterOsty(projection, target, overkillDamage, props, dealer, cardSource);
        var resolvedOverkillHpLoss = ClampProjectedHpLoss(overkillHpLoss);
        projection.TrackProjectedThreat(target, resolvedOverkillHpLoss, source);
        projection.TrackProjectedUnblockedDamage(target, ClampProjectedHpLoss(overkillDamage));
        projection.ApplyProjectedHpLoss(target, resolvedOverkillHpLoss, source);
        if (projection.IsProjectedAlive(target))
            ApplyProjectedAfterDamageReceivedReactions(projection, target, (int)blockedDamage, props, dealer, source);
    }


}
