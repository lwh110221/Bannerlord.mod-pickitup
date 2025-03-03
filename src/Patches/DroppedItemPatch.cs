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
        internal static bool _isProcessingActions = false; // 是否正在处理动作
        internal const int MAX_ACTIONS_PER_TICK = 8;      // 每帧最多处理的动作数

        private static bool ShouldPersistWeapon(ItemObject item)
        {
            if (item == null) return false;
            var settings = Settings.Settings.Instance;

            // 检查武器类型
            switch (item.ItemType)
            {
                // 近战武器
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    return settings.PersistMeleeWeapons;

                // 远程武器
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Pistol:
                case ItemObject.ItemTypeEnum.Musket:
                    return settings.PersistRangedWeapons;

                // 投掷武器
                case ItemObject.ItemTypeEnum.Thrown:
                    return settings.PersistThrownWeapons;

                // 弹药
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.Bullets:
                    return settings.PersistAmmunition;

                // 盾牌
                case ItemObject.ItemTypeEnum.Shield:
                    return settings.PersistShields;

                default:
                    return false;
            }
        }

        [HarmonyPatch(typeof(SpawnedItemEntity), MethodType.Constructor)]
        [HarmonyPostfix]
        public static void SpawnedItemEntityCtorPostfix(SpawnedItemEntity __instance)
        {
            try
            {
                // Mcm检查是否启用
                if (!Settings.Settings.Instance.EnableWeaponPersistence) return;

                if (__instance?.WeaponCopy.Item == null || Mission.Current == null) return;

                // 检查武器类型是否应该持久化
                if (!ShouldPersistWeapon(__instance.WeaponCopy.Item)) return;

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

        [HarmonyPatch(typeof(SpawnedItemEntity), "HasLifeTime", MethodType.Setter)]
        [HarmonyPrefix]
        public static bool HasLifeTimeSetterPrefix(SpawnedItemEntity __instance, ref bool value)
        {
            try
            {
                if (!Settings.Settings.Instance.EnableWeaponPersistence) return true;

                if (__instance?.WeaponCopy.Item != null && ShouldPersistWeapon(__instance.WeaponCopy.Item))
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
                    spawnedItem.WeaponCopy.Item != null &&
                    ShouldPersistWeapon(spawnedItem.WeaponCopy.Item))
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