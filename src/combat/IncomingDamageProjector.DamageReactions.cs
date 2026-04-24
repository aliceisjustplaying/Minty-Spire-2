using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static void ApplyProjectedBeforeDamageReceivedReactions(
        DamageProjection projection,
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        ProjectedDamageSource source
    )
    {
        if (amount <= 0 || dealer == null || !projection.IsProjectedAlive(dealer))
            return;

        if (target.GetPower<ThornsPower>() is { Amount: > 0 } thornsPower &&
            (props.IsPoweredAttack() || cardSource is Omnislice))
        {
            ApplyProjectedDamage(
                projection,
                dealer,
                thornsPower.Amount,
                ValueProp.Unpowered | ValueProp.SkipHurtAnim,
                target,
                null,
                GetProjectedReactionDamageSource(projection, dealer, source)
            );
        }
    }

    private static void ApplyProjectedAfterDamageReceivedReactions(
        DamageProjection projection,
        Creature target,
        int blockedDamage,
        ValueProp props,
        Creature? dealer,
        ProjectedDamageSource source
    )
    {
        if (dealer == null || !props.IsPoweredAttack() || !projection.IsProjectedAlive(dealer))
            return;

        var reactionSource = GetProjectedReactionDamageSource(projection, dealer, source);

        if (target.GetPower<FlameBarrierPower>() is { Amount: > 0 } flameBarrierPower)
        {
            ApplyProjectedDamage(
                projection,
                dealer,
                flameBarrierPower.Amount,
                ValueProp.Unpowered,
                target,
                null,
                reactionSource
            );
        }

        if (blockedDamage > 0 && target.HasPower<ReflectPower>())
        {
            ApplyProjectedDamage(
                projection,
                dealer,
                blockedDamage,
                ValueProp.Unpowered,
                target,
                null,
                reactionSource
            );
        }
    }

    private static ProjectedDamageSource GetProjectedReactionDamageSource(
        DamageProjection projection,
        Creature reactionTarget,
        ProjectedDamageSource source
    )
    {
        return reactionTarget == projection.Player && source == ProjectedDamageSource.None
            ? ProjectedDamageSource.Self
            : source;
    }


}
