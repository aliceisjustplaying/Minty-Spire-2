using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using MintySpire2.util;

namespace MintySpire2.relicreminders;

/// <summary>
///     Credits to Book and erasels.
///     Adds relic reminder icons to cards in hand when threshold relics are primed.
///     Also makes affected cards glow gold.
/// </summary>
[HarmonyPatch]
public static class ThresholdRelicCardOverlay
{
    private const string IconContainerNodeName = "MintyThresholdRelicIcons";
    private static readonly WeakNodeRegistry<NCard> TrackedCards = new();

    [ThreadStatic]
    private static List<Texture2D>? _iconBuffer;

    [HarmonyPatch(typeof(NCard), "UpdateVisuals")]
    [HarmonyPostfix]
    private static void UpdateVisuals_Postfix(NCard __instance, PileType pileType, CardPreviewMode previewMode)
    {
        TrackedCards.Register(__instance);

        if (pileType != PileType.Hand)
        {
            HideIcons(__instance);
            TrackedCards.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(CardPile), "InvokeContentsChanged")]
    [HarmonyPostfix]
    private static void CatchHandChange(CardPile __instance)
    {
        if (__instance.Type == PileType.Hand)
            RefreshTrackedCardOverlays();
    }

    [HarmonyPatch]
    private static class CatchRefreshEvents
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PenNib), nameof(PenNib.AfterCardPlayed));
            yield return AccessTools.Method(typeof(TuningFork), nameof(TuningFork.AfterCardPlayed));
            yield return AccessTools.Method(typeof(GalacticDust), nameof(GalacticDust.AfterStarsSpent));
        }

        [HarmonyPostfix]
        static void CatchAfterCardPlayed() => RefreshTrackedCardOverlays();
    }

    [HarmonyPatch(typeof(CardModel), "ShouldGlowGoldInternal", MethodType.Getter)]
    [HarmonyPostfix]
    public static void ShouldGlowGoldInternal_Postfix(CardModel __instance, ref bool __result)
    {
        if (!__result)
            __result = HasAnyActiveThresholdIcon(__instance, GetThresholdRelics());
    }

    private static void RefreshTrackedCardOverlays()
    {
        var relics = GetThresholdRelics();
        TrackedCards.ForEachLive(card => RefreshCardOverlay(card, relics));
    }

    private static void RefreshCardOverlay(NCard card, in ThresholdRelics relics)
    {
        var model = card.Model;
        if (model == null || !IsInHand(model))
        {
            HideIcons(card);
            return;
        }

        var icons = GetIconBuffer();
        CollectActiveIcons(model, relics, icons);

        if (icons.Count == 0)
        {
            HideIcons(card);
            return;
        }

        var container = EnsureIconContainer(card, icons.Count);
        if (container == null)
            return;

        for (var i = 0; i < icons.Count; i++)
            SetIcon(container.GetChild<TextureRect>(i), icons[i]);

        for (var i = icons.Count; i < container.GetChildCount(); i++)
            SetIcon(container.GetChild<TextureRect>(i), null);

        container.Visible = true;
    }

    private static List<Texture2D> GetIconBuffer()
    {
        return _iconBuffer ??= new List<Texture2D>(4);
    }

    private static bool IsInHand(CardModel? card)
    {
        if (card == null)
            return false;

        var me = LocalContext.GetMe(RunManager.Instance?.State);
        if (me == null)
            return false;

        return PileType.Hand.GetPile(me).Cards.Contains(card);
    }

    private static bool HasAnyActiveThresholdIcon(CardModel card, in ThresholdRelics relics)
    {
        if (!relics.HasAny)
            return false;

        var icons = GetIconBuffer();
        CollectActiveIcons(card, relics, icons);
        return icons.Count > 0;
    }

    private static void CollectActiveIcons(CardModel card, in ThresholdRelics relics, List<Texture2D> icons)
    {
        icons.Clear();

        if (!relics.HasAny)
            return;

        var penNib = relics.PenNib;
        if (card.Type == CardType.Attack && penNib?.Status == RelicStatus.Active)
        {
            icons.Add(penNib.Icon);
        }

        var tuningFork = relics.TuningFork;
        if (card.Type == CardType.Skill && tuningFork?.Status == RelicStatus.Active)
        {
            icons.Add(tuningFork.Icon);
        }

        var galacticDust = relics.GalacticDust;
        if (ShouldShowGalacticDust(card, galacticDust))
        {
            icons.Add(galacticDust!.Icon);
        }
    }

    private static bool ShouldShowGalacticDust(CardModel card, GalacticDust? galacticDust)
    {
        if (galacticDust == null)
            return false;

        var threshold = galacticDust.DynamicVars.Stars.IntValue;
        if (threshold <= 0 || card.CurrentStarCost <= 0)
            return false;

        return (galacticDust.StarsSpent % threshold) + card.CurrentStarCost >= threshold;
    }

    private static Control? EnsureIconContainer(NCard card, int requiredIconSlots)
    {
        var body = card.Body;

        var container = body.GetNodeOrNull<Control>(IconContainerNodeName);
        if (container == null)
        {
            container = new Control
            {
                Name = IconContainerNodeName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 1f,
                AnchorRight = 1f,
                AnchorTop = 0f,
                AnchorBottom = 0f,
                Visible = false,
            };

            body.AddChild(container);
        }

        while (container.GetChildCount() < requiredIconSlots)
            container.AddChild(MakeIconSlot(container.GetChildCount()));

        return container;
    }

    private static TextureRect MakeIconSlot(int index)
    {
        const float horizontalSpacing = 32f;
        var horizontalOffset = index * horizontalSpacing;

        return new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 112f - horizontalOffset,
            OffsetRight = 160f - horizontalOffset,
            OffsetTop = -218f,
            OffsetBottom = -170f,
            Visible = false,
        };
    }

    private static void SetIcon(TextureRect iconRect, Texture2D? texture)
    {
        iconRect.Texture = texture;
        iconRect.Visible = texture != null;
    }

    private static void HideIcons(NCard card)
    {
        var container = card.Body?.GetNodeOrNull<Control>(IconContainerNodeName);
        if (container == null)
            return;

        container.Visible = false;

        for (var i = 0; i < container.GetChildCount(); i++)
            SetIcon(container.GetChild<TextureRect>(i), null);
    }

    private static ThresholdRelics GetThresholdRelics()
    {
        var me = LocalContext.GetMe(RunManager.Instance?.State);
        if (me == null)
            return default;

        PenNib? penNib = null;
        TuningFork? tuningFork = null;
        GalacticDust? galacticDust = null;

        foreach (var relic in me.Relics)
        {
            switch (relic)
            {
                case PenNib pn:
                    penNib = pn;
                    break;
                case TuningFork tf:
                    tuningFork = tf;
                    break;
                case GalacticDust gd:
                    galacticDust = gd;
                    break;
            }
        }

        return new ThresholdRelics(penNib, tuningFork, galacticDust);
    }

    private readonly record struct ThresholdRelics(PenNib? PenNib, TuningFork? TuningFork, GalacticDust? GalacticDust)
    {
        public bool HasAny => PenNib != null || TuningFork != null || GalacticDust != null;
    }
}