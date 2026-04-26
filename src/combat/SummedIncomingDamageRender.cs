using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MintySpire2.util;

namespace MintySpire2.combat;

/// <summary>
///     Adds a small text label to the Right of the health bar when the health bar is visible
///     and the owner creature is the player.
/// </summary>
[HarmonyPatch(typeof(NHealthBar))]
public static class SummedIncomingDamageRender
{
    private const string RightTextNodeName = "MintyIncomingDamageText";
    private const float RightPadding = 8f;
    
    private static readonly WeakNodeRegistry<NHealthBar> ValidBars = new();

    /// <summary>
    ///     After a creature is assigned, create label node if it doesn't exist.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NHealthBar.SetCreature))]
    public static void CatchBarSet(NHealthBar __instance)
    {
        var player = LocalContext.GetMe(RunManager.Instance.State);
        if (player != null && __instance._creature?.Player == player)
        {
            ValidBars.Register(__instance);
            CreateLabelIfNotExist(__instance);
        }
    }
    
    /// <summary>
    ///     Whenever the bar is updated, update the text display (this is overkill)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NHealthBar.RefreshValues))]
    public static void CatchBarRefresh(NHealthBar __instance)
    {
        var player = LocalContext.GetMe(RunManager.Instance.State);
        if (player != null && __instance._creature?.Player == player)
        {
            ValidBars.Register(__instance);
            RefreshVisibilityAndText(__instance);
        }
    }

    /// <summary>
    ///     When the container size is about to change, reposition the label
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsets")]
    public static void CatchBarResize(NHealthBar __instance, Vector2 size)
    {
        var player = LocalContext.GetMe(RunManager.Instance.State);
        if (player != null && __instance._creature?.Player == player)
        {
            RepositionLabel(__instance, size);
        }
    }

    /// <summary>
    /// <summary>
    ///     Creates the label once and attaches it near the HP bar container.
    /// </summary>
    /// <returns>bool: Was label created</returns>
    private static bool CreateLabelIfNotExist(NHealthBar bar)
    {
        var parent = GetLabelParent(bar);
        if (parent.GetNodeOrNull<Label>(RightTextNodeName) != null)
            return false;

        var label = new Label
        {
            Name = RightTextNodeName,
            Text = "",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        
        var font = GD.Load<Font>("res://fonts/kreon_bold.ttf");
        if (font != null)
            label.AddThemeFontOverride((StringName)"font", font);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", Colors.Salmon);
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        label.ZIndex = 100;

        parent.AddChild(label);
        RepositionLabel(bar, bar.HpBarContainer.Size);
        return true;
    }

    /// <summary>
    ///     Positions the label to the right of the HP bar.
    /// </summary>
    private static void RepositionLabel(NHealthBar bar, Vector2 newSize)
    {
        var label = GetLabel(bar);
        if (label == null) return;

        var labelWidth = 90f;
        var labelHeight = Math.Max(newSize.Y, 28f);

        label.Size = new Vector2(labelWidth, labelHeight);

        var container = bar.HpBarContainer;
        label.Position = new Vector2(
            container.Position.X + newSize.X + RightPadding,
            container.Position.Y + (newSize.Y - labelHeight) / 2f
        );
    }

    /// <summary>
    ///     Shows/hides the label and sets its text.
    ///     Only visible when the health bar is visible, the creature is the player, and it's their turn.
    /// </summary>
    private static void RefreshVisibilityAndText(NHealthBar bar)
    {
        var label = GetLabel(bar);
        if (label == null || !bar.Visible)
            return;

        if (!Config.ShowIncomingDamage || IsCombatPastPlayablePlayerTurn())
        {
            label.Visible = false;
            return;
        }

        // Only show for the player-owned health bar.
        var creature = bar._creature;
        if (creature?.Player == null || creature.CombatState == null)
        {
            label.Visible = false;
            return;
        }

        var incomingDamage = CalculateIncomingDamage(creature);
        if (incomingDamage.HasDamage)
        {
            label.Text = incomingDamage.ToDisplayText();
            label.Visible = true;
            return;
        }

        label.Visible = false;
    }

    private static Control GetLabelParent(NHealthBar bar)
    {
        return bar.HpBarContainer.GetParent() as Control ?? bar;
    }

    private static Label? GetLabel(NHealthBar bar)
    {
        return GetLabelParent(bar).GetNodeOrNull<Label>(RightTextNodeName);
    }

    /// <summary>
    ///     Refresh labels when a creature death is fired to recalculate incoming damage immediately.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.InvokeDiedEvent))]
    public static void CatchMonsterDeath()
    {
        RefreshAllLabels();
    }
    
    /// <summary>
    ///     Refresh labels if the hand changes in case end turn damage cards are added
    /// </summary>
    /// <param name="__instance"></param>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardPile), "InvokeContentsChanged")]
    private static void CatchHandChange(CardPile __instance)
    {
        if (!Config.ShowIncomingDamage) return;
        if (__instance is { Type: PileType.Hand })
        {
            RefreshAllLabels();
        }
    }

    /// <summary>
    ///     Refresh labels when combat stats relevant to the projection change.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.GainBlockInternal))]
    [HarmonyPatch(typeof(Creature), nameof(Creature.LoseBlockInternal))]
    [HarmonyPatch(typeof(Creature), nameof(Creature.SetCurrentHpInternal))]
    public static void CatchCreatureCombatValueChanged()
    {
        RefreshAllLabels();
    }

    /// <summary>
    ///     Refresh labels when powers change, since they can affect turn-end block and damage math.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.ApplyPowerInternal))]
    [HarmonyPatch(typeof(Creature), nameof(Creature.InvokePowerModified))]
    [HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
    public static void CatchPowerChanged()
    {
        RefreshAllLabels();
    }

    /// <summary>
    ///     Refresh labels when orb state changes, since turn-end triggers can change projected damage taken.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.AddCapacity))]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.RemoveCapacity))]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.TryEnqueue))]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.Remove))]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.Insert))]
    [HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.Clear))]
    public static void CatchOrbStateChanged()
    {
        RefreshAllLabels();
    }

    private static void RefreshAllLabels()
    {
        ValidBars.ForEachLive(RefreshVisibilityAndText);
    }

    private static bool IsCombatPastPlayablePlayerTurn()
    {
        var combatManager = CombatManager.Instance;
        return combatManager.IsEnemyTurnStarted ||
               combatManager.EndingPlayerTurnPhaseOne ||
               combatManager.EndingPlayerTurnPhaseTwo;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndPlayerTurnPhaseOneInternal))]
    public static void CatchPlayerTurnEnding()
    {
        RefreshAllLabels();
    }

    /// <summary>
    ///     Calculate projected HP loss from deterministic end-of-turn effects and the upcoming enemy turn.
    /// </summary>
    /// <param name="creature">The Player creature that we'll calculate the incoming damage for.</param>
    private static DamageBreakdown CalculateIncomingDamage(Creature creature)
    {
        return IncomingDamageProjector.Calculate(creature);
    }
}
