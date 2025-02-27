using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;

namespace PickItUp.Patches
{
    /// <summary>
    /// 处理Agent武器掉落事件
    /// </summary>
    public class AgentWeaponDropPatch
    {
        // 记录被缴械的Agent和时间
        private static readonly Dictionary<Agent, float> _disarmedAgentTimes = new Dictionary<Agent, float>();

        private static float PickupDelay => Settings.Settings.Instance?.PickupDelay ?? 1.5f;

        /// <summary>
        /// 检查Agent是否在缴械冷却时间内
        /// </summary>
        public static bool HasDisarmCooldown(Agent agent)
        {
            try
            {
                if (agent == null) return false;

                if (_disarmedAgentTimes.TryGetValue(agent, out float disarmedTime))
                {
                    float currentTime = Mission.Current.CurrentTime;
                    bool hasCooldown = (currentTime - disarmedTime) < PickupDelay;                    
                    return hasCooldown;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasMeleeWeapon(Agent agent)
        {
            try
            {
                if (agent?.Equipment == null) return false;

                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    var equipment = agent.Equipment[i];
                    if (!equipment.IsEmpty && equipment.Item?.WeaponComponent != null)
                    {
                        var weaponClass = equipment.Item.WeaponComponent.PrimaryWeapon.WeaponClass;
                        // 检查是否是近战武器
                        if (IsMeleeWeaponClass(weaponClass))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsMeleeWeaponClass(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.OneHandedSword ||
                   weaponClass == WeaponClass.TwoHandedSword ||
                   weaponClass == WeaponClass.OneHandedAxe ||
                   weaponClass == WeaponClass.TwoHandedAxe ||
                   weaponClass == WeaponClass.Mace ||
                   weaponClass == WeaponClass.TwoHandedMace ||
                   weaponClass == WeaponClass.OneHandedPolearm ||
                   weaponClass == WeaponClass.TwoHandedPolearm ||
                   weaponClass == WeaponClass.Dagger ||
                   weaponClass == WeaponClass.ThrowingAxe ||
                   weaponClass == WeaponClass.ThrowingKnife ||
                   weaponClass == WeaponClass.Javelin;
        }

        [HarmonyPatch(typeof(Agent))]
        public static class AgentPatch
        {
            [HarmonyPatch("DropItem")]
            [HarmonyPatch(new Type[] { typeof(EquipmentIndex), typeof(WeaponClass) })]
            [HarmonyPostfix]
            public static void DropItemPostfix(Agent __instance)
            {
                try
                {
                    if (__instance == null || !__instance.IsAIControlled)
                    {
                        return;
                    }

                    // 直接检查是否还有近战武器
                    if (!HasMeleeWeapon(__instance))
                    {
                        _disarmedAgentTimes[__instance] = Mission.Current.CurrentTime;
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        public static void RemoveDisarmCooldown(Agent agent)
        {
            try
            {
                if (agent != null)
                {
                    if (_disarmedAgentTimes.Remove(agent))
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static void ClearAllCooldowns()
        {
            try
            {
                int count = _disarmedAgentTimes.Count;
                _disarmedAgentTimes.Clear();
            }
            catch (Exception)
            {
            }
        }
    }
} 