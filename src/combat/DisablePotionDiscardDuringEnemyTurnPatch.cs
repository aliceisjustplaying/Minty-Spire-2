using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace MintySpire2.combat;

public static class DisablePotionDiscardDuringEnemyTurnPatch
{
    private static readonly AccessTools.FieldRef<NPotionPopup, NPotionPopupButton> DiscardButton =
        AccessTools.FieldRefAccess<NPotionPopup, NPotionPopupButton>("_discardButton");

    private static bool ShouldDisableDiscard()
    {
        return Config.DisablePotionDiscardDuringEnemyTurn &&
               CombatManager.Instance is { IsInProgress: true, IsEnemyTurnStarted: true };
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NPotionPopup), "RefreshButtons")]
    public static void DisableButtonDuringEnemyTurn(NPotionPopup __instance)
    {
        if (ShouldDisableDiscard())
        {
            DiscardButton(__instance).Disable();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NPotionPopup), "OnDiscardButtonPressed")]
    public static bool PreventDiscardDuringEnemyTurn(NButton _)
    {
        return !ShouldDisableDiscard();
    }
}
