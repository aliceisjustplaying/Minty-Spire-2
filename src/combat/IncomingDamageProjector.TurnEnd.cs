using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using MintySpire2.util;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
    private static void ApplyBeforeTurnEndProjection(DamageProjection projection, Player player)
    {
        var playerCreature = projection.Player;

        var doom = playerCreature.GetPower<DoomPower>();
        if (doom != null && doom.IsOwnerDoomed())
        {
            projection.LoseProjectedHp(playerCreature, projection.GetProjectedHp(playerCreature), ProjectedDamageSource.Self);
            return;
        }

        var playerCombatState = player.PlayerCombatState!;

        if (playerCreature.GetPower<StampedePower>() is { Amount: > 0 } stampedePower)
            projection.AutoPlayProjectedAttackCards(stampedePower.Amount);

        if (player.GetRelic<PaelsEye>() is { } paelsEye && paelsEye.ShouldTakeExtraTurn(player))
            projection.ClearProjectedHand();

        var hand = projection.GetProjectedHandCards();

        var platingAmount = playerCreature.GetPowerInstances<PlatingPower>().Sum(power => power.Amount);
        if (platingAmount > 0)
            AddProjectedBlock(projection, playerCreature, platingAmount, ValueProp.Unpowered);

        if (playerCreature.GetPower<HailstormPower>() is { } hailstormPower)
        {
            var frostOrbCount = playerCombatState.OrbQueue.Orbs.Count(orb => orb is FrostOrb);
            if (frostOrbCount >= hailstormPower.DynamicVars["FrostOrbs"].IntValue)
                ApplyProjectedDamageToEnemies(projection, hailstormPower.Amount, ValueProp.Unpowered, playerCreature);
        }

        if (playerCreature.GetPower<TheBombPower>() is { Amount: <= 1 } theBombPower)
            ApplyProjectedDamageToEnemies(projection, theBombPower.DynamicVars.Damage.IntValue, ValueProp.Unpowered, playerCreature);

        var shouldTriggerOrichalcum = projection.GetProjectedBlock(playerCreature) <= 0;

        if (player.GetRelic<Orichalcum>() is { } orichalcum && shouldTriggerOrichalcum)
            AddProjectedBlock(projection, playerCreature, orichalcum.DynamicVars.Block.IntValue, ValueProp.Unpowered);

        if (player.GetRelic<FakeOrichalcum>() is { } fakeOrichalcum && shouldTriggerOrichalcum)
            AddProjectedBlock(projection, playerCreature, fakeOrichalcum.DynamicVars.Block.IntValue, ValueProp.Unpowered);

        if (player.GetRelic<CloakClasp>() is { } cloakClasp && hand.Count > 0)
            AddProjectedBlock(projection, playerCreature, hand.Count * cloakClasp.DynamicVars.Block.IntValue, ValueProp.Unpowered);

        if (player.GetRelic<RippleBasin>() is { } rippleBasin)
        {
            var playedAnAttack = CombatManager.Instance.History.CardPlaysFinished.Any(entry =>
                entry.HappenedThisTurn(playerCreature.CombatState) &&
                entry.CardPlay.Card.Type == CardType.Attack &&
                entry.CardPlay.Card.Owner == player);

            if (!playedAnAttack)
                AddProjectedBlock(projection, playerCreature, rippleBasin.DynamicVars.Block.IntValue, ValueProp.Unpowered);
        }

        if (player.GetRelic<DiamondDiadem>() is { } diamondDiadem &&
            diamondDiadem.CardsPlayedThisTurn <= diamondDiadem.DynamicVars["CardThreshold"].IntValue &&
            !playerCreature.HasPower<DiamondDiademPower>())
        {
            projection.ShouldApplyDiamondDiademProtection = true;
        }

        if (player.GetRelic<ScreamingFlagon>() is { } screamingFlagon && hand.Count == 0)
            ApplyProjectedDamageToEnemies(projection, screamingFlagon.DynamicVars.Damage.IntValue, ValueProp.Unpowered, playerCreature);

        if (player.GetRelic<StoneCalendar>() is { } stoneCalendar &&
            playerCreature.CombatState?.RoundNumber == stoneCalendar.DynamicVars["DamageTurn"].IntValue)
        {
            ApplyProjectedDamageToEnemies(projection, stoneCalendar.DynamicVars.Damage.IntValue, ValueProp.Unpowered, playerCreature);
        }
    }

    private static void ApplyAfterTurnEndProjection(DamageProjection projection, Player player)
    {
        var playerCreature = projection.Player;

        if (!projection.IsProjectedAlive(playerCreature))
            return;

        if (playerCreature.GetPower<ConstrictPower>() is { } constrictPower)
            ApplyProjectedDamage(
                projection,
                playerCreature,
                constrictPower.Amount,
                ValueProp.Unpowered,
                playerCreature,
                null,
                ProjectedDamageSource.Self
            );

        if (playerCreature.GetPower<DemisePower>() is { } demisePower)
            ApplyProjectedDamage(
                projection,
                playerCreature,
                demisePower.Amount,
                ValueProp.Unblockable | ValueProp.Unpowered,
                null,
                null,
                ProjectedDamageSource.Self
            );

        if (!projection.IsProjectedAlive(playerCreature))
            return;

        if (player.GetRelic<ParryingShield>() is { } parryingShield &&
            projection.GetProjectedBlock(playerCreature) >= parryingShield.DynamicVars.Block.IntValue)
        {
            var target = projection.GetRandomProjectedEnemy();
            if (target != null)
                ApplyProjectedDamage(
                    projection,
                    target,
                    parryingShield.DynamicVars.Damage.IntValue,
                    ValueProp.Unpowered,
                    playerCreature,
                    null,
                    ProjectedDamageSource.None
                );
        }

        if (!projection.IsProjectedAlive(playerCreature))
            return;

        if (playerCreature.GetPower<DisintegrationPower>() is { } disintegrationPower)
            ApplyProjectedDamage(
                projection,
                playerCreature,
                disintegrationPower.Amount,
                ValueProp.Unpowered,
                playerCreature,
                null,
                ProjectedDamageSource.Self
            );
    }


}
