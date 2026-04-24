using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using MintySpire2.util;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static void ApplyTurnEndHandProjection(DamageProjection projection, Player player)
    {
        var hand = projection.GetProjectedHandCards();
        var initialHandCount = hand.Count;

        foreach (var card in hand.Where(card => card.HasTurnEndInHandEffect))
        {
            if (!projection.IsProjectedAlive(projection.Player))
                return;

            if (card is Regret)
            {
                ApplyProjectedDamage(
                    projection,
                    projection.Player,
                    initialHandCount,
                    ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                    projection.Player,
                    card,
                    ProjectedDamageSource.Self
                );
                continue;
            }

            var hpLossVars = card.CanonicalVars.OfType<HpLossVar>().ToList();
            var damageVars = card.CanonicalVars.OfType<DamageVar>().ToList();
            if (hpLossVars.Count == 0 && damageVars.Count == 0 &&
                !CardTurnEndInspector.DoesTurnEndInHandCauseHpLoss(card))
            {
                continue;
            }

            foreach (var hpLossVar in hpLossVars)
            {
                ApplyProjectedDamage(
                    projection,
                    projection.Player,
                    hpLossVar.IntValue,
                    ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                    projection.Player,
                    card,
                    ProjectedDamageSource.Self
                );
            }

            foreach (var damageVar in damageVars)
            {
                ApplyProjectedDamage(
                    projection,
                    projection.Player,
                    damageVar.IntValue,
                    damageVar.Props,
                    projection.Player,
                    card,
                    ProjectedDamageSource.Self
                );
            }
        }
    }


}
