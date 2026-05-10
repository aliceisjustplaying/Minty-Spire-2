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
                case TheBoot theBoot
                    when dealer == theBoot.Owner.Creature && target != dealer && props.IsPoweredAttack():
                    var damageMinimum = theBoot.DynamicVars["DamageMinimum"].BaseValue;
                    if (hpLoss >= 1m && hpLoss < damageMinimum)
                        hpLoss = damageMinimum;
                    break;
            }
        }

        foreach (var listener in target.CombatState.IterateHookListeners())
        {
            switch (listener)
            {
                case HardenedShellPower hardenedShell when target == hardenedShell.Owner:
                    hpLoss = Math.Min(hpLoss, projection.GetRemainingHardenedShellProtection(target));
                    if (dealer is { IsPlayer: true } && target != dealer &&
                        dealer.Player?.GetRelic<TheBoot>() is { } theBootForShell)
                    {
                        hpLoss = Math.Max(hpLoss, theBootForShell.DynamicVars["DamageMinimum"].BaseValue);
                    }
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

        // Iterate listeners in IterateHookListeners order to match Hook.ModifyHpLostAfterOsty,
        // which applies modifiers in player.Relics order. A static order would diverge whenever
        // the player picked up TungstenRod / BeatingRemnant in a different sequence.
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
