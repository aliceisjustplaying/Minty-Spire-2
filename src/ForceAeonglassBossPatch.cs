using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Random;

namespace MintySpire2;

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
internal static class ForceAeonglassBossPatch
{
    private static void Postfix(ActModel __instance, Rng rng)
    {
        if (!Config.ForceAeonglassAct3Boss)
            return;

        EncounterModel? aeonglass = __instance.AllBossEncounters.FirstOrDefault(encounter => encounter is AeonglassBoss);
        if (aeonglass == null)
            return;

        if (!__instance.HasSecondBoss)
        {
            __instance.SetBossEncounter(aeonglass);
            return;
        }

        switch (Config.AeonglassBossPlacement)
        {
            case AeonglassBossPlacement.First:
                __instance.SetBossEncounter(aeonglass);
                break;
            case AeonglassBossPlacement.Second:
                __instance.SetSecondBossEncounter(aeonglass);
                break;
            case AeonglassBossPlacement.Random:
                if (rng.NextBool())
                    __instance.SetBossEncounter(aeonglass);
                else
                    __instance.SetSecondBossEncounter(aeonglass);
                break;
        }
    }
}
