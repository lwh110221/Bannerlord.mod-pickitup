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
            var settings = Settings.McmSettings.Instance;

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
            if (!Settings.McmSettings.Instance.EnableWeaponPersistence) return;

            if (__instance?.WeaponCopy.Item == null || Mission.Current == null) return;

            if (!ShouldPersistWeapon(__instance.WeaponCopy.Item)) return;

            var manager = Mission.Current.GetMissionBehavior<DroppedItemManager>();
            if (manager == null) return;

            __instance.HasLifeTime = false;
            _pendingActions.Enqueue(() =>
            {
                manager.RegisterDroppedItem(__instance);
            });

            if (!_isProcessingActions)
            {
                Mission.Current.AddMissionBehavior(new SafeActionProcessor());
            }
        }

        [HarmonyPatch(typeof(SpawnedItemEntity), "HasLifeTime", MethodType.Setter)]
        [HarmonyPrefix]
        public static bool HasLifeTimeSetterPrefix(SpawnedItemEntity __instance, ref bool value)
        {

            if (!Settings.McmSettings.Instance.EnableWeaponPersistence) return true;

            if (__instance?.WeaponCopy.Item != null && ShouldPersistWeapon(__instance.WeaponCopy.Item))
            {
                value = false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Mission), "OnMissionObjectRemoved")]
        [HarmonyPrefix]
        public static bool OnMissionObjectRemovedPrefix(Mission __instance, MissionObject missionObject)
        {
            // 如果功能被禁用，允许正常移除物品
            if (!Settings.McmSettings.Instance.EnableWeaponPersistence) return true;

            if (missionObject is SpawnedItemEntity spawnedItem &&
                spawnedItem.WeaponCopy.Item != null &&
                ShouldPersistWeapon(spawnedItem.WeaponCopy.Item))
            {
                return false;  // 阻止执行原方法
            }
            return true;
        }
    }

    public class SafeActionProcessor : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnRemoveBehavior()
        {
            DroppedItemPatch._pendingActions.Clear();
            DroppedItemPatch._isProcessingActions = false;

            base.OnRemoveBehavior();

        }

        public override void OnMissionTick(float dt)
        {
            if (DroppedItemPatch._pendingActions.Count > 0 && !DroppedItemPatch._isProcessingActions)
            {
                DroppedItemPatch._isProcessingActions = true;

                int processedCount = 0;
                while (DroppedItemPatch._pendingActions.Count > 0 &&
                       processedCount < DroppedItemPatch.MAX_ACTIONS_PER_TICK)
                {
                    var action = DroppedItemPatch._pendingActions.Dequeue();
                    action?.Invoke();
                    processedCount++;
                }

                DroppedItemPatch._isProcessingActions = false;

                if (DroppedItemPatch._pendingActions.Count == 0)
                {
                    Mission.Current.RemoveMissionBehavior(this);
                }
            }
        }
    }
}