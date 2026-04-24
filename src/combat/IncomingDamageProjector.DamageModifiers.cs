using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static decimal ApplyProjectedHpLossModifiersBeforeOsty(
        DamageProjection projection,
        Creature target,
        decimal hpLoss,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource
    )
    {
        if (hpLoss <= 0 || target.CombatState == null)
            return hpLoss;

        foreach (var listener in target.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case TheBoot theBoot when dealer == theBoot.Owner.Creature && props.IsPoweredAttack():
                    if (hpLoss is > 0m and < 5m)
                        hpLoss = theBoot.DynamicVars["DamageMinimum"].BaseValue;
                    break;
            }
        }

        foreach (var listener in target.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case HardenedShellPower hardenedShell when target == hardenedShell.Owner:
                    hpLoss = Math.Min(hpLoss, projection.GetRemainingHardenedShellProtection(target));
                    break;
            }
        }

        return Math.Max(0m, hpLoss);
    }

    private static decimal ApplyProjectedHpLossModifiersAfterOsty(
        DamageProjection projection,
        Creature target,
        decimal hpLoss,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource
    )
    {
        if (hpLoss <= 0 || target.CombatState == null)
            return hpLoss;

        foreach (var listener in target.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case IntangiblePower intangible when target == intangible.Owner:
                    hpLoss = Math.Min(hpLoss, GetProjectedIntangibleDamageCap(dealer));
                    break;
                case TungstenRod tungstenRod when target == tungstenRod.Owner.Creature:
                    hpLoss = Math.Max(0m, hpLoss - tungstenRod.DynamicVars["HpLossReduction"].BaseValue);
                    break;
                case BeatingRemnant beatingRemnant when target == beatingRemnant.Owner.Creature:
                    hpLoss = Math.Min(hpLoss, projection.GetRemainingBeatingRemnantProtection());
                    break;
            }
        }

        foreach (var listener in target.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case BufferPower bufferPower when target == bufferPower.Owner && projection.GetProjectedBufferCharges(target) > 0:
                    hpLoss = 0m;
                    projection.ConsumeProjectedBufferCharge(target);
                    break;
            }
        }

        return Math.Max(0m, hpLoss);
    }

    private static Creature GetProjectedUnblockedDamageTarget(
        DamageProjection projection,
        Creature originalTarget,
        decimal hpLoss,
        ValueProp props
    )
    {
        if (originalTarget.CombatState == null || hpLoss <= 0)
            return originalTarget;

        var target = originalTarget;
        foreach (var listener in originalTarget.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case DieForYouPower dieForYou
                    when target == dieForYou.Owner.PetOwner?.Creature &&
                         projection.IsProjectedAlive(dieForYou.Owner) &&
                         props.IsPoweredAttack():
                    target = dieForYou.Owner;
                    break;
            }
        }

        return target;
    }

    private static int ClampProjectedHpLoss(decimal hpLoss)
    {
        return Math.Max(0, (int)Math.Min(hpLoss, 999999999m));
    }

    private static int GetProjectedIntangibleDamageCap(Creature? dealer)
    {
        var player = dealer?.Player ?? dealer?.PetOwner;
        return player?.GetRelic<TheBoot>() != null ? 5 : 1;
    }
}
