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
                   weaponClass == WeaponClass.LowGripPolearm ||
                   weaponClass == WeaponClass.Pick ||
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
#if DEBUG
                        DebugHelper.Log("AgentWeaponDrop", $"移除Agent {agent.Name} 的缴械冷却");
#endif
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("AgentWeaponDrop", $"移除缴械冷却时出错: {ex.Message}");
#endif
            }
        }

        public static void ClearAllCooldowns()
        {
            try
            {
                int count = _disarmedAgentTimes.Count;
                _disarmedAgentTimes.Clear();
#if DEBUG
                DebugHelper.Log("AgentWeaponDrop", $"清理所有缴械冷却状态，共 {count} 个");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("AgentWeaponDrop", $"清理所有缴械冷却时出错: {ex.Message}");
#endif
            }
        }

        [HarmonyPatch(typeof(Mission))]
        public static class MissionPatch
        {
            [HarmonyPatch("OnAgentDeleted")]
            [HarmonyPostfix]
            public static void OnAgentDeletedPostfix(Agent affectedAgent)
            {
                try
                {
                    if (affectedAgent != null)
                    {
#if DEBUG
                        DebugHelper.Log("AgentWeaponDrop", $"Agent {affectedAgent.Name} 被删除，清理其缴械状态");
#endif
                        RemoveDisarmCooldown(affectedAgent);
                    }
                }
                catch (Exception ex)
                {
#if DEBUG
                    DebugHelper.Log("AgentWeaponDrop", $"处理Agent删除时出错: {ex.Message}");
#endif
                }
            }

            [HarmonyPatch("EndMission")]
            [HarmonyPostfix]
            public static void EndMissionPostfix()
            {
                try
                {
#if DEBUG
                    DebugHelper.Log("AgentWeaponDrop", "任务结束，清理所有缴械状态");
#endif
                    ClearAllCooldowns();
                }
                catch (Exception ex)
                {
#if DEBUG
                    DebugHelper.Log("AgentWeaponDrop", $"处理任务结束时出错: {ex.Message}");
#endif
                }
            }
        }
    }
} 