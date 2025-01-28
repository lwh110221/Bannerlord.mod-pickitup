using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using PickItUp.Behaviors;

namespace PickItUp.Patches
{
    [HarmonyPatch(typeof(Agent))]
    public class AgentPatch
    {
        [HarmonyPatch("OnWeaponDrop")]
        public static void Postfix(Agent __instance, EquipmentIndex equipmentSlot)
        {
            if (__instance == null) return;

            var behavior = Mission.Current?.GetMissionBehavior<PickUpWeaponBehavior>();
            if (behavior != null)
            {
                var weapon = __instance.Equipment[equipmentSlot];
                behavior.OnAgentDropWeapon(__instance, weapon, equipmentSlot);
            }
        }
    }
} 