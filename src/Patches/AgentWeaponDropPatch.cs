using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace PickItUp.Patches
{
    /// <summary>
    /// 处理Agent武器掉落事件
    /// </summary>
    public class AgentWeaponDropPatch
    {
        #region Main
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
        #endregion

        #region 补丁
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
                    if (__instance == null || !__instance.IsAIControlled || __instance.Health <= 0f)
                    {
                        return;
                    }

                    if (!__instance.HasMeleeWeaponCached && !__instance.HasSpearCached)
                    {
                        _disarmedAgentTimes[__instance] = Mission.Current.CurrentTime;
                    }
                }
                catch (Exception)
                {
                }
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
        // <summary>
        // 处理Agent武器弹药耗尽事件
        // </summary>
        [HarmonyPatch(typeof(Agent))]
        public static class AgentAmmoEmptyPatch
        {
            [HarmonyPatch("OnWeaponAmountChange")]
            [HarmonyPostfix]
            public static void OnWeaponAmountChangePostfix(Agent __instance, EquipmentIndex slotIndex, short amount)
            {
                if (__instance == null || __instance.Health <= 0)
                {
                    return;
                }
                if (amount == 0)
                {
                    MissionWeapon weapon = __instance.Equipment[slotIndex];
                    if (weapon.CurrentUsageItem?.WeaponClass == WeaponClass.Bolt ||
                        weapon.CurrentUsageItem?.WeaponClass == WeaponClass.Arrow)
                    {
                        if (__instance.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("Ammo out!", Colors.Red));
                        }
                        return;
                    }
                    if (Settings.Settings.Instance?.DropBag == true)
                    {
                        __instance.DropItem(slotIndex);
                    }
                    else
                    {
                        __instance.RemoveEquippedWeapon(slotIndex);
                    }
                    if (__instance.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Ammo out!", Colors.Red));
                    }
                }
            }
        }
        #endregion

        #region 辅助方法
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
        #endregion
    }
}
