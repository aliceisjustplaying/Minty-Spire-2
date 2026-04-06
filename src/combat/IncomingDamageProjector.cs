using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MintySpire2.util;

namespace MintySpire2.combat;

internal static class IncomingDamageProjector
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
        ApplyEnemyTurnProjection(projection, creature);

        return projection.GetDamageBreakdown();
    }

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
            playerCreature.CombatState.RoundNumber == stoneCalendar.DynamicVars["DamageTurn"].IntValue)
        {
            ApplyProjectedDamageToEnemies(projection, stoneCalendar.DynamicVars.Damage.IntValue, ValueProp.Unpowered, playerCreature);
        }
    }

    private static void ApplyOrbProjection(DamageProjection projection, Player player)
    {
        var orbQueue = player.PlayerCombatState.OrbQueue;
        var glassPassiveByOrb = new Dictionary<OrbModel, int>();

        foreach (var orb in orbQueue.Orbs.ToList())
        {
            var triggerCount = Hook.ModifyOrbPassiveTriggerCount(player.Creature.CombatState, orb, 1, out _);
            for (var triggerIndex = 0; triggerIndex < triggerCount; triggerIndex++)
            {
                if (!projection.IsProjectedAlive(projection.Player))
                    return;

                switch (orb)
                {
                    case FrostOrb frostOrb:
                        AddProjectedBlock(projection, projection.Player, frostOrb.PassiveVal, ValueProp.Unpowered);
                        break;
                    case LightningOrb lightningOrb:
                    {
                        var target = projection.GetRandomProjectedEnemy();
                        if (target != null)
                            ApplyProjectedDamage(
                                projection,
                                target,
                                lightningOrb.PassiveVal,
                                ValueProp.Unpowered,
                                projection.Player,
                                null,
                                ProjectedDamageSource.None
                            );
                        break;
                    }
                    case GlassOrb glassOrb:
                    {
                        if (!glassPassiveByOrb.TryGetValue(glassOrb, out var passiveValue))
                            passiveValue = Math.Max(0, (int)glassOrb.PassiveVal);

                        if (passiveValue > 0)
                        {
                            ApplyProjectedDamageToEnemies(projection, passiveValue, ValueProp.Unpowered, projection.Player);
                            glassPassiveByOrb[glassOrb] = Math.Max(0, passiveValue - 1);
                        }

                        break;
                    }
                }
            }
        }
    }

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

            if (!CardTurnEndInspector.DoesTurnEndInHandCauseHpLoss(card))
                continue;

            foreach (var hpLossVar in card.CanonicalVars.OfType<HpLossVar>())
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

            foreach (var damageVar in card.CanonicalVars.OfType<DamageVar>())
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

    private static void ApplyEnemyTurnProjection(DamageProjection projection, Creature playerCreature)
    {
        foreach (var enemy in playerCreature.CombatState!.HittableEnemies)
        {
            if (!projection.IsProjectedAlive(enemy))
                continue;

            foreach (var intent in enemy.Monster.NextMove.Intents)
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

        var blockedDamage = props.HasFlag(ValueProp.Unblockable)
            ? 0m
            : Math.Min(projection.GetProjectedBlock(target), modifiedDamage);

        projection.SpendProjectedBlock(target, (int)blockedDamage);

        var hpLoss = Math.Max(modifiedDamage - blockedDamage, 0m);
        hpLoss = ApplyProjectedHpLossModifiersBeforeOsty(projection, target, hpLoss, props, dealer, cardSource);
        if (hpLoss <= 0)
            return;

        var hpLossTarget = GetProjectedUnblockedDamageTarget(projection, target, hpLoss, props);
        hpLoss = ApplyProjectedHpLossModifiersAfterOsty(projection, hpLossTarget, hpLoss, props, dealer, cardSource);

        var resolvedHpLoss = ClampProjectedHpLoss(hpLoss);
        projection.TrackProjectedThreat(hpLossTarget, resolvedHpLoss, source);
        var overkillDamage = projection.ApplyProjectedHpLoss(hpLossTarget, resolvedHpLoss, source);
        if (hpLossTarget == target || overkillDamage <= 0)
            return;

        var overkillHpLoss = ApplyProjectedHpLossModifiersAfterOsty(projection, target, overkillDamage, props, dealer, cardSource);
        var resolvedOverkillHpLoss = ClampProjectedHpLoss(overkillHpLoss);
        projection.TrackProjectedThreat(target, resolvedOverkillHpLoss, source);
        projection.ApplyProjectedHpLoss(target, resolvedOverkillHpLoss, source);
    }

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

internal sealed class DamageProjection(Creature player)
{
    private static readonly System.Reflection.FieldInfo? BeatingRemnantDamageReceivedField =
        AccessTools.Field(typeof(BeatingRemnant), "_damageReceivedThisTurn");

    private readonly Dictionary<Creature, int> projectedBlockByCreature = player.CombatState!.Creatures.ToDictionary(
        creature => creature,
        creature => creature.Block
    );

    private readonly Dictionary<Creature, int> projectedHpByCreature = player.CombatState!.Creatures.ToDictionary(
        creature => creature,
        creature => creature.CurrentHp
    );

    private readonly List<CardModel> projectedHandCards = player.Player?.PlayerCombatState?.Hand.Cards.ToList() ?? [];
    private readonly Rng combatTargetRng = new(
        player.CombatState!.RunState.Rng.CombatTargets.Seed,
        player.CombatState.RunState.Rng.CombatTargets.Counter
    );
    private readonly Rng handShuffleRng = new(
        player.CombatState.RunState.Rng.Shuffle.Seed,
        player.CombatState.RunState.Rng.Shuffle.Counter
    );

    private readonly Dictionary<Creature, int> projectedBufferChargesByCreature = player.CombatState!.Creatures.ToDictionary(
        creature => creature,
        creature => creature.GetPower<BufferPower>()?.Amount ?? 0
    );
    private readonly Dictionary<Creature, decimal> remainingHardenedShellProtectionByCreature = player.CombatState!.Creatures.ToDictionary(
        creature => creature,
        creature => creature.GetPower<HardenedShellPower>()?.DisplayAmount ?? decimal.MaxValue
    );

    private int playerEnemyDamageThreat;
    private int playerSelfDamageThreat;
    private decimal remainingBeatingRemnantProtection = GetInitialBeatingRemnantProtection(player);

    public Creature Player { get; } = player;

    public bool ShouldApplyDiamondDiademProtection { get; set; }

    public int GetProjectedBlock(Creature creature)
    {
        return projectedBlockByCreature.GetValueOrDefault(creature, 0);
    }

    public int GetProjectedHp(Creature creature)
    {
        return projectedHpByCreature.GetValueOrDefault(creature, 0);
    }

    public bool IsProjectedAlive(Creature creature)
    {
        return GetProjectedHp(creature) > 0;
    }

    public void AddProjectedBlock(Creature creature, int amount)
    {
        if (amount <= 0 || !IsProjectedAlive(creature))
            return;

        projectedBlockByCreature[creature] = GetProjectedBlock(creature) + amount;
    }

    public void SpendProjectedBlock(Creature creature, int amount)
    {
        if (amount <= 0)
            return;

        projectedBlockByCreature[creature] = Math.Max(0, GetProjectedBlock(creature) - amount);
    }

    public void LoseProjectedHp(Creature creature, int amount)
    {
        LoseProjectedHp(creature, amount, ProjectedDamageSource.None);
    }

    public void LoseProjectedHp(Creature creature, int amount, ProjectedDamageSource source)
    {
        TrackProjectedThreat(creature, amount, source);
        ApplyProjectedHpLoss(creature, amount, source);
    }

    public void TrackProjectedThreat(Creature creature, int amount, ProjectedDamageSource source)
    {
        if (amount <= 0 || creature != Player)
            return;

        switch (source)
        {
            case ProjectedDamageSource.Enemy:
                playerEnemyDamageThreat += amount;
                break;
            case ProjectedDamageSource.Self:
                playerSelfDamageThreat += amount;
                break;
        }
    }

    public int ApplyProjectedHpLoss(Creature creature, int amount, ProjectedDamageSource source)
    {
        if (amount <= 0)
            return 0;

        var currentHp = GetProjectedHp(creature);
        if (currentHp <= 0)
            return amount;

        var actualHpLoss = Math.Min(currentHp, amount);
        var overkillDamage = Math.Max(0, amount - actualHpLoss);

        projectedHpByCreature[creature] = currentHp - actualHpLoss;
        TrackProjectedHpLoss(creature, actualHpLoss, source);
        return overkillDamage;
    }

    private void TrackProjectedHpLoss(Creature creature, int amount, ProjectedDamageSource source)
    {
        if (amount <= 0)
            return;

        if (remainingHardenedShellProtectionByCreature.TryGetValue(creature, out var hardenedShellProtection) &&
            hardenedShellProtection != decimal.MaxValue)
        {
            remainingHardenedShellProtectionByCreature[creature] = Math.Max(0m, hardenedShellProtection - amount);
        }

        if (creature != Player)
            return;

        if (remainingBeatingRemnantProtection != decimal.MaxValue)
            remainingBeatingRemnantProtection = Math.Max(0m, remainingBeatingRemnantProtection - amount);
    }

    public IReadOnlyList<Creature> GetProjectedHittableEnemies()
    {
        return Player.CombatState!.HittableEnemies.Where(IsProjectedAlive).ToList();
    }

    public Creature? GetRandomProjectedEnemy()
    {
        return combatTargetRng.NextItem(GetProjectedHittableEnemies());
    }

    public IReadOnlyList<CardModel> GetProjectedHandCards()
    {
        return projectedHandCards;
    }

    public void ClearProjectedHand()
    {
        projectedHandCards.Clear();
    }

    public void AutoPlayProjectedAttackCards(int amount)
    {
        for (var index = 0; index < amount; index++)
        {
            var attackCards = projectedHandCards
                .Where(card => card.Type == CardType.Attack && !card.Keywords.Contains(CardKeyword.Unplayable))
                .ToList();
            var selectedCard = handShuffleRng.NextItem(attackCards);
            if (selectedCard == null)
                return;

            projectedHandCards.Remove(selectedCard);
        }
    }

    public int GetProjectedBufferCharges(Creature creature)
    {
        return projectedBufferChargesByCreature.GetValueOrDefault(creature, 0);
    }

    public void ConsumeProjectedBufferCharge(Creature creature)
    {
        if (GetProjectedBufferCharges(creature) > 0)
            projectedBufferChargesByCreature[creature] = GetProjectedBufferCharges(creature) - 1;
    }

    public decimal GetRemainingHardenedShellProtection(Creature creature)
    {
        return remainingHardenedShellProtectionByCreature.GetValueOrDefault(creature, decimal.MaxValue);
    }

    public decimal GetRemainingBeatingRemnantProtection()
    {
        return remainingBeatingRemnantProtection;
    }

    public DamageBreakdown GetDamageBreakdown()
    {
        return new DamageBreakdown(playerEnemyDamageThreat, playerSelfDamageThreat);
    }

    private static decimal GetInitialBeatingRemnantProtection(Creature playerCreature)
    {
        var beatingRemnant = playerCreature.Player?.GetRelic<BeatingRemnant>();
        if (beatingRemnant == null)
            return decimal.MaxValue;

        var maxHpLoss = beatingRemnant.DynamicVars["MaxHpLoss"].BaseValue;
        var damageReceivedThisTurn = BeatingRemnantDamageReceivedField?.GetValue(beatingRemnant) is decimal receivedDamage
            ? receivedDamage
            : 0m;
        return Math.Max(0m, maxHpLoss - damageReceivedThisTurn);
    }
}
