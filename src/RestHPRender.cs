using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MintySpire2.util;

namespace MintySpire2;

/// <summary>
///     Adds an extra label above the button visuals, and for HealRestSiteOption shows:
///     current HP -> HP after healing (including relic modifiers).
/// </summary>
[HarmonyPatch]
public static class RestHPRender
{
    private const string HealLabelNodeName = "ModHealPreviewLabel";
    private const string FrozenHealPreviewMetaKey = "MintyFrozenHealPreview";

    private static readonly WeakNodeRegistry<NRestSiteButton> ValidButtons = new();

    /// <summary>
    ///     After NRestSiteButton is ready, inject the label and immediately populate it.
    /// </summary>
    [HarmonyPatch(typeof(NRestSiteButton), nameof(NRestSiteButton._Ready))]
    [HarmonyPostfix]
    public static void Ready_Postfix(NRestSiteButton __instance)
    {
        if (__instance.Option is not HealRestSiteOption) return;

        ClearFrozenPreview(__instance);
        CreateLabelIfNotExists(__instance);
        UpdateExtraLabel(__instance);
    }

    /// <summary>
    ///     After the button reloads (typically when Option is assigned), refresh the preview text.
    /// </summary>
    [HarmonyPatch(typeof(NRestSiteButton), "Reload")]
    [HarmonyPostfix]
    public static void Reload_Postfix(NRestSiteButton __instance)
    {
        if (__instance.Option is not HealRestSiteOption) return;

        ClearFrozenPreview(__instance);
        CreateLabelIfNotExists(__instance);
        UpdateExtraLabel(__instance);
    }

    [HarmonyPatch(typeof(NRestSiteButton), "SelectOption")]
    [HarmonyPrefix]
    public static void FreezePreviewOnSelect(NRestSiteButton __instance, RestSiteOption option)
    {
        if (option is not HealRestSiteOption)
            return;

        var extra = __instance.FindChild(HealLabelNodeName, true, false) as Label;
        if (extra == null || string.IsNullOrEmpty(extra.Text))
            return;

        __instance.SetMeta(FrozenHealPreviewMetaKey, extra.Text);
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.HealInternal))]
    [HarmonyPostfix]
    public static void CatchOutOfCombatHeal(Creature __instance)
    {
        CatchHPChange(__instance);
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.SetCurrentHpInternal))]
    [HarmonyPostfix]
    public static void CatchOutOfCombatHpSet(Creature __instance)
    {
        CatchHPChange(__instance);
    }

    /// <summary>
    ///     Creates the label and applies layout/styling. Uses %Visuals as the parent when available.
    /// </summary>
    private static void CreateLabelIfNotExists(NRestSiteButton button)
    {
        // Avoid adding duplicates if the node is reused or _Ready is run more than once.
        if (button.HasNode(HealLabelNodeName))
            return;

        // Attach to %Visuals so it follows the button's visuals scaling/layout.
        var parent = button.GetNodeOrNull<Control>("%Visuals") ?? button;

        var label = new Label
        {
            Name = HealLabelNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        
        var font = GD.Load<Font>("res://fonts/kreon_bold.ttf");
        if (font != null)
            label.AddThemeFontOverride((StringName)"font", font);
        label.AddThemeColorOverride("font_color", Colors.LightGreen);
        label.AddThemeFontSizeOverride("font_size", 16);

        // full width strip anchored at top, slightly above button.
        label.AnchorLeft = 0;
        label.AnchorRight = 1;
        label.AnchorTop = 0;
        label.AnchorBottom = 0;

        label.OffsetLeft = 12;
        label.OffsetRight = -12;
        label.OffsetTop = -18;
        label.OffsetBottom = -4;
        label.HorizontalAlignment = HorizontalAlignment.Left;

        parent.AddChild(label);
    }

    /// <summary>
    ///     Updates the label text/visibility depending on the current RestSiteOption.
    /// </summary>
    private static void UpdateExtraLabel(NRestSiteButton button)
    {
        var extra = button.FindChild(HealLabelNodeName, true, false) as Label;
        if (extra == null) return;

        if (TryGetFrozenPreview(button, out var frozenPreview))
        {
            extra.Text = frozenPreview;
            extra.Visible = true;
            ValidButtons.Register(button);
            return;
        }

        var player = button.Option.Owner;
        if (!LocalContext.IsMe(player))
        {
            extra.Visible = false;
            return;
        }

        // Calculate current HP and the projected HP after healing.
        var currentHp = player.Creature.CurrentHp;
        var maxHp = player.Creature.MaxHp;

        var healAmount = HealRestSiteOption.GetHealAmount(player);
        var healInt = (int)Math.Floor(healAmount);
        var healedHp = Math.Min(maxHp, currentHp + Math.Max(0, healInt));

        extra.Text = $"HP: {currentHp} → {healedHp}";
        extra.Visible = true;

        ValidButtons.Register(button);
    }

    private static bool TryGetFrozenPreview(NRestSiteButton button, out string frozenPreview)
    {
        frozenPreview = string.Empty;
        if (!button.HasMeta(FrozenHealPreviewMetaKey))
            return false;

        frozenPreview = button.GetMeta(FrozenHealPreviewMetaKey).AsString();
        return !string.IsNullOrEmpty(frozenPreview);
    }

    private static void ClearFrozenPreview(NRestSiteButton button)
    {
        if (button.HasMeta(FrozenHealPreviewMetaKey))
            button.RemoveMeta(FrozenHealPreviewMetaKey);
    }

    /// <summary>
    ///     Catch HP changes to dynamically update the label (in case of Eternal Feather)
    /// </summary>
    public static void CatchHPChange(Creature creature)
    {
        if (!LocalContext.IsMe(creature) || Wiz.IsInCombat()) return;
        ValidButtons.ForEachLive(UpdateExtraLabel);
    }
}
