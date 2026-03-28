using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

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
    private static readonly HashSet<NCard> TrackedCards = [];

    private static readonly List<Texture2D> _icons = new List<Texture2D>(4);
    
    [HarmonyPatch(typeof(CombatRoom), "StartCombat")]
    [HarmonyPostfix]
    static void CatchCombatStart(IRunState? runState)
    {
        TrackedCards.Clear();
        var me = LocalContext.GetMe(runState);
        if (me == null) return;
        foreach (var relic in me.Relics)
        {
            IdentifyThresholdRelic(relic);
        }

        me.RelicObtained += IdentifyThresholdRelic;
    }

    [HarmonyPatch(typeof(CombatRoom), "OnCombatEnded")]
    [HarmonyPostfix]
    static void CatchCombatEnd()
    {
        TrackedCards.Clear();
        var me = LocalContext.GetMe(RunManager.Instance?.State);
        if (me != null) me.RelicObtained -= IdentifyThresholdRelic;
    }

    [HarmonyPatch(typeof(NCard), "UpdateVisuals")]
    [HarmonyPostfix]
    private static void UpdateVisuals_Postfix(NCard __instance, PileType pileType, CardPreviewMode previewMode)
    {
        TrackedCards.Add(__instance);

        if (pileType != PileType.Hand)
        {
            HideIcons(__instance);
        }
    }

    [HarmonyPatch(typeof(CardPile), "InvokeContentsChanged")]
    [HarmonyPostfix]
    private static void CatchHandChange(CardPile __instance)
    {
        if (__instance.Type == PileType.Hand)
        {
            RefreshTrackedCardOverlays();
        }
    }

    [HarmonyPatch]
    private static class CatchRefreshEvents
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() =>
        [
            typeof(PenNib).Method(nameof(PenNib.AfterCardPlayed)),
            typeof(Nunchaku).Method(nameof(Nunchaku.AfterCardPlayed)),
            typeof(TuningFork).Method(nameof(TuningFork.AfterCardPlayed)),
            typeof(GalacticDust).Method(nameof(GalacticDust.AfterStarsSpent)),
        ];

        [HarmonyPostfix]
        static void CatchAfterCardPlayed() => RefreshTrackedCardOverlays();
    }

    [HarmonyPatch(typeof(CardModel), "ShouldGlowGoldInternal", MethodType.Getter)]
    [HarmonyPostfix]
    public static void ShouldGlowGoldInternal_Postfix(CardModel __instance, ref bool __result)
    {
        if (!__result)
            __result = HasAnyActiveThresholdIcon(__instance);
    }


    private static void RefreshTrackedCardOverlays()
    {
        foreach (var card in TrackedCards)
        {
            RefreshCardOverlay(card);
        }
    }

    private static void RefreshCardOverlay(NCard card)
    {
        var model = card.Model;
        if (model == null || !HasAny() || !IsInHand(model))
        {
            HideIcons(card);
            return;
        }
        
        CollectActiveIcons(model, _icons);

        if (_icons.Count == 0)
        {
            HideIcons(card);
            return;
        }

        var container = EnsureIconContainer(card, _icons.Count);
        if (container == null) return;

        for (var i = 0; i < _icons.Count; i++)
            SetIcon(container.GetChild<TextureRect>(i), _icons[i]);

        for (var i = _icons.Count; i < container.GetChildCount(); i++)
            SetIcon(container.GetChild<TextureRect>(i), null);

        container.Visible = true;
    }

    private static bool IsInHand(CardModel? card)
    {
        if (card == null) return false;
        var me = LocalContext.GetMe(RunManager.Instance?.State);
        if (me == null) return false;

        return PileType.Hand.GetPile(me).Cards.Contains(card);
    }

    private static bool HasAnyActiveThresholdIcon(CardModel card)
    {
        if (!HasAny()) return false;
        
        CollectActiveIcons(card, _icons);
        return _icons.Count > 0;
    }

    private static void CollectActiveIcons(CardModel card, List<Texture2D> icons)
    {
        icons.Clear();

        if (!HasAny())
            return;
        
        if (card.Type == CardType.Attack && _penNib?.Status == RelicStatus.Active) icons.Add(_penNib.Icon);

        if (card.Type == CardType.Attack && _nunchaku?.Status == RelicStatus.Active) icons.Add(_nunchaku.Icon);

        if (card.Type == CardType.Skill && _tuningFork?.Status == RelicStatus.Active) icons.Add(_tuningFork.Icon);

        if (ShouldShowGalacticDust(card)) icons.Add(_galacticDust!.Icon);
    }

    private static bool ShouldShowGalacticDust(CardModel card)
    {
        if (_galacticDust == null) return false;

        var threshold = _galacticDust.DynamicVars.Stars.IntValue;
        if (threshold <= 0 || card.CurrentStarCost <= 0) return false;

        return (_galacticDust.StarsSpent % threshold) + card.CurrentStarCost >= threshold;
    }

    // Icon container management
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
        if (!GodotObject.IsInstanceValid(card)) return;
        var container = card.Body?.GetNodeOrNull<Control>(IconContainerNodeName);
        if (container == null)
            return;

        container.Visible = false;

        for (var i = 0; i < container.GetChildCount(); i++)
            SetIcon(container.GetChild<TextureRect>(i), null);
    }

    
    // Relic management
    private static PenNib? _penNib;
    private static Nunchaku? _nunchaku;
    private static TuningFork? _tuningFork;
    private static GalacticDust? _galacticDust;
    
    private static bool HasAny()
    {
        return _penNib != null || _galacticDust != null || _nunchaku != null || _tuningFork != null;
    }
    
    private static void IdentifyThresholdRelic(RelicModel relic)
    {
        switch (relic)
        {
            case PenNib pn:
                _penNib = pn;
                break;
            case Nunchaku n:
                _nunchaku = n;
                break;
            case TuningFork tf:
                _tuningFork = tf;
                break;
            case GalacticDust gd:
                _galacticDust = gd;
                break;
        }
    }
}