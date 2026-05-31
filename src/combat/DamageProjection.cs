using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;

namespace MintySpire2.combat;

internal sealed partial class DamageProjection(Creature player)
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
    private bool playerProjectedFatal;
    private bool playerProjectedRevive;
    private bool projectedLizardTailUsed;
    private bool projectedFairyInABottleUsed;
    private decimal remainingBeatingRemnantProtection = GetInitialBeatingRemnantProtection(player);

    public Creature Player { get; } = player;

    public bool ShouldApplyDiamondDiademProtection { get; set; }

    public bool SkipEnemyTurn { get; set; }

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

        var nextHp = currentHp - actualHpLoss;
        projectedHpByCreature[creature] = nextHp;

        if (creature == Player && nextHp <= 0)
            ApplyProjectedPlayerDeath();

        return overkillDamage;
    }

    public void TrackProjectedUnblockedDamage(Creature creature, int unblockedDamage)
    {
        if (unblockedDamage <= 0)
            return;

        if (remainingHardenedShellProtectionByCreature.TryGetValue(creature, out var hardenedShellProtection) &&
            hardenedShellProtection != decimal.MaxValue)
        {
            remainingHardenedShellProtectionByCreature[creature] = Math.Max(0m, hardenedShellProtection - unblockedDamage);
        }

        if (creature != Player)
            return;

        if (remainingBeatingRemnantProtection != decimal.MaxValue)
            remainingBeatingRemnantProtection = Math.Max(0m, remainingBeatingRemnantProtection - unblockedDamage);
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
        return new DamageBreakdown(
            playerEnemyDamageThreat,
            playerSelfDamageThreat,
            playerProjectedFatal,
            playerProjectedRevive && !playerProjectedFatal
        );
    }

    private void ApplyProjectedPlayerDeath()
    {
        if (!TryApplyProjectedLizardTail() && !TryApplyProjectedFairyInABottle())
        {
            playerProjectedFatal = true;
            return;
        }

        playerProjectedRevive = true;
    }

    private bool TryApplyProjectedLizardTail()
    {
        var lizardTail = Player.Player?.GetRelic<LizardTail>();
        if (lizardTail == null || lizardTail.IsUsedUp || lizardTail.WasUsed || projectedLizardTailUsed)
            return false;

        projectedLizardTailUsed = true;
        projectedHpByCreature[Player] = GetHalfMaxHp();
        return true;
    }

    private bool TryApplyProjectedFairyInABottle()
    {
        var player = Player.Player;
        if (player == null || projectedFairyInABottleUsed || player.Potions.All(potion => potion is not FairyInABottle))
            return false;

        projectedFairyInABottleUsed = true;
        projectedHpByCreature[Player] = GetThirdMaxHp();
        return true;
    }

    private int GetHalfMaxHp()
    {
        return Math.Max(1, (int)Math.Ceiling(Player.MaxHp / 2m));
    }

    private int GetThirdMaxHp()
    {
        return Math.Max(1, (int)Math.Ceiling(Player.MaxHp * 0.3m));
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
