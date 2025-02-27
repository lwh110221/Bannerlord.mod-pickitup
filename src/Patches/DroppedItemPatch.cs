using HarmonyLib;
using TaleWorlds.MountAndBlade;
using PickItUp.Behaviors;
using TaleWorlds.Core;
using System;
using System.Collections.Generic;


namespace PickItUp.Patches
{
    [HarmonyPatch]
    public class DroppedItemPatch
    {
        internal static readonly Queue<Action> _pendingActions = new Queue<Action>();
        internal static bool _isProcessingActions = false;
        internal const int MAX_ACTIONS_PER_TICK = 10;  // 每帧最多处理的动作数量

        [HarmonyPatch(typeof(SpawnedItemEntity), MethodType.Constructor)]
        [HarmonyPostfix]
        public static void SpawnedItemEntityCtorPostfix(SpawnedItemEntity __instance)
        {
            try
            {
                // Mcm检查是否启用
                if (!Settings.Settings.Instance.EnableWeaponPersistence) return;

                if (__instance?.WeaponCopy.Item == null || Mission.Current == null) return;

                var manager = Mission.Current.GetMissionBehavior<DroppedItemManager>();
                if (manager == null) return;

                __instance.HasLifeTime = false;
                _pendingActions.Enqueue(() =>
                {
                    try
                    {
                        manager.RegisterDroppedItem(__instance);
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        DebugHelper.LogError("DroppedItemPatch", "物品注册错误", ex);
#endif
                    }
                });

                if (!_isProcessingActions)
                {
                    Mission.Current.AddMissionBehavior(new SafeActionProcessor());
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.LogError("DroppedItemPatch", "物品注册错误", ex);
#endif
            }
        }

        private static string GetItemType(ItemObject item)
        {
            if (item == null) return "未知";
            
            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Arrows:
                    return "箭矢";
                case ItemObject.ItemTypeEnum.Bolts:
                    return "弩箭";
                case ItemObject.ItemTypeEnum.Thrown:
                    return "投掷武器";
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                    return "单手武器";
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                    return "双手武器";
                case ItemObject.ItemTypeEnum.Polearm:
                    return "长杆武器";
                case ItemObject.ItemTypeEnum.Shield:
                    return "盾牌";
                case ItemObject.ItemTypeEnum.Bow:
                    return "弓";
                case ItemObject.ItemTypeEnum.Crossbow:
                    return "弩";
                case ItemObject.ItemTypeEnum.Pistol:
                    return "手枪";
                case ItemObject.ItemTypeEnum.Musket:
                    return "火枪";
                case ItemObject.ItemTypeEnum.Bullets:
                    return "子弹";
                case ItemObject.ItemTypeEnum.Banner:
                    return "旗帜";
                case ItemObject.ItemTypeEnum.Invalid:
                    return "无效物品";
                default:
                    return $"其他物品({item.ItemType})";
            }
        }

        [HarmonyPatch(typeof(SpawnedItemEntity), "HasLifeTime", MethodType.Setter)]
        [HarmonyPrefix]
        public static bool HasLifeTimeSetterPrefix(SpawnedItemEntity __instance, ref bool value)
        {
            try
            {
                if (!Settings.Settings.Instance.EnableWeaponPersistence) return true;

                if (__instance?.WeaponCopy.Item != null)
                {
                    value = false;  // false，使物品永久存在
                }
                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.LogError("DroppedItemPatch", "HasLifeTime设置错误", ex);
#endif
                return true;
            }
        }

        [HarmonyPatch(typeof(Mission), "OnMissionObjectRemoved")]
        [HarmonyPrefix]
        public static bool OnMissionObjectRemovedPrefix(Mission __instance, MissionObject missionObject)
        {
            try
            {
                // 如果功能被禁用，允许正常移除物品
                if (!Settings.Settings.Instance.EnableWeaponPersistence) return true;

                if (missionObject is SpawnedItemEntity spawnedItem && 
                    spawnedItem.WeaponCopy.Item != null)
                {
                    return false;  // 阻止执行原方法
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.LogError("DroppedItemPatch", "物品移除处理错误", ex);
#endif
            }
            return true;
        }
    }

    public class SafeActionProcessor : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnRemoveBehavior()
        {
            try
            {
#if DEBUG
                if (DroppedItemPatch._pendingActions.Count > 0)
                {
                    DebugHelper.Log("SafeActionProcessor", $"清理剩余的 {DroppedItemPatch._pendingActions.Count} 个待处理操作");
                }
#endif
                // 清理所有待处理的操作
                DroppedItemPatch._pendingActions.Clear();
                // 重置处理标志
                DroppedItemPatch._isProcessingActions = false;

                base.OnRemoveBehavior();
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.LogError("SafeActionProcessor", "清理时出错", ex);
#endif
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (DroppedItemPatch._pendingActions.Count > 0 && !DroppedItemPatch._isProcessingActions)
            {
                DroppedItemPatch._isProcessingActions = true;
                try
                {
                    int processedCount = 0;
                    while (DroppedItemPatch._pendingActions.Count > 0 && 
                           processedCount < DroppedItemPatch.MAX_ACTIONS_PER_TICK)
                    {
                        var action = DroppedItemPatch._pendingActions.Dequeue();
                        action?.Invoke();
                        processedCount++;
                    }

#if DEBUG
                    if (processedCount > 0)
                    {
                        DebugHelper.Log("SafeActionProcessor", $"本帧处理了 {processedCount} 个物品操作，剩余 {DroppedItemPatch._pendingActions.Count} 个待处理");
                    }
#endif
                }
                catch (Exception ex)
                {
#if DEBUG
                    DebugHelper.LogError("SafeActionProcessor", "安全处理器错误", ex);
#endif
                }
                finally
                {
                    DroppedItemPatch._isProcessingActions = false;
                }

                if (DroppedItemPatch._pendingActions.Count == 0)
                {
                    Mission.Current.RemoveMissionBehavior(this);
                }
            }
        }
    }
} 