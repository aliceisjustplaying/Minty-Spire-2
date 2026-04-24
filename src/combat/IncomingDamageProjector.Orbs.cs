using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MintySpire2.combat;

internal static partial class IncomingDamageProjector
{
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


}
