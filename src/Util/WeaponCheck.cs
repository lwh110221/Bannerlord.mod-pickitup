using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace PickItUp.Util
{
    public static class WeaponCheck
    {
        /// <summary>
        /// 检查是否允许盾牌拾取
        /// </summary>
        /// <returns>是否允许盾牌拾取</returns>
        public static bool IsShieldPickupEnabled()
        {
            return Settings.McmSettings.Instance?.EnableShieldPickup ?? false;
        }

        /// <summary>
        /// 检查武器是否为盾牌
        /// </summary>
        /// <param name="spawnedItem">物品</param>
        /// <returns>是否为盾牌</returns>
        public static bool IsShield(SpawnedItemEntity spawnedItem)
        {
            if (spawnedItem == null || 
                spawnedItem.WeaponCopy.IsEmpty || 
                spawnedItem.WeaponCopy.Item == null || 
                spawnedItem.WeaponCopy.Item.WeaponComponent == null ||
                spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon == null)
                return false;
                
            return spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.IsShield;
        }

        /// <summary>
        /// 检查武器是否可用作近战的投掷武器
        /// </summary>
        /// <param name="weaponClass">武器类型</param>
        /// <returns>是否为双用投掷武器</returns>
        public static bool IsThrowableMeleeWeapon(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.ThrowingAxe ||
                   weaponClass == WeaponClass.ThrowingKnife ||
                   weaponClass == WeaponClass.Javelin;
        }

        /// <summary>
        /// 检查物品是否为盾牌
        /// </summary>
        /// <param name="spawnedItem">物品</param>
        /// <returns>是否为盾牌</returns>
        public static bool IsItemShield(SpawnedItemEntity spawnedItem)
        {
            if (!IsShieldPickupEnabled())
            {
                return false;
            }

            if (spawnedItem == null || 
                spawnedItem.WeaponCopy.IsEmpty || 
                spawnedItem.WeaponCopy.Item == null || 
                spawnedItem.WeaponCopy.Item.WeaponComponent == null || 
                spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon == null) 
                return false;
                
            return IsShield(spawnedItem);
        }

        /// <summary>
        /// 检查武器是否可以作为近战武器使用
        /// </summary>
        /// <param name="weaponClass">武器类型</param>
        /// <returns>是否可以作为近战武器使用</returns>
        public static bool CanBeUsedAsMeleeWeapon(WeaponClass weaponClass)
        {
            if (weaponClass == WeaponClass.Arrow ||
                weaponClass == WeaponClass.Bolt ||
                weaponClass == WeaponClass.Stone ||
                weaponClass == WeaponClass.Bow ||
                weaponClass == WeaponClass.Crossbow ||
                weaponClass == WeaponClass.SmallShield ||
                weaponClass == WeaponClass.LargeShield)
            {
                return false;
            }

            var settings = Settings.McmSettings.Instance;

            // 根据设置检查每种武器类型
            switch (weaponClass)
            {
                case WeaponClass.OneHandedSword:
                    return settings.PickupOneHandedSword;
                case WeaponClass.TwoHandedSword:
                    return settings.PickupTwoHandedSword;
                case WeaponClass.OneHandedAxe:
                    return settings.PickupOneHandedAxe;
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.Pick:
                    return settings.PickupTwoHandedAxe;
                case WeaponClass.Mace:
                    return settings.PickupMace;
                case WeaponClass.TwoHandedMace:
                    return settings.PickupTwoHandedMace;
                case WeaponClass.OneHandedPolearm:
                    return settings.PickupOneHandedPolearm;
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return settings.PickupTwoHandedPolearm;
                case WeaponClass.Dagger:
                    return settings.PickupDagger;
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Javelin:
                    return settings.PickupThrowingWeapons;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 检查武器是否为近战武器
        /// </summary>
        /// <param name="weaponComponent">武器组件</param>
        /// <returns>是否为近战武器</returns>
        public static bool IsMeleeWeapon(WeaponComponent weaponComponent)
        {
            if (weaponComponent?.PrimaryWeapon == null) return false;
            return CanBeUsedAsMeleeWeapon(weaponComponent.PrimaryWeapon.WeaponClass);
        }

        /// <summary>
        /// 检查武器有效性
        /// </summary>
        /// <param name="spawnedItem">物品</param>
        /// <returns>是否有效</returns>
        public static bool IsValidWeapon(SpawnedItemEntity spawnedItem)
        {
            if (spawnedItem?.GameEntity == null ||
                !spawnedItem.GameEntity.IsVisibleIncludeParents() ||
                spawnedItem.WeaponCopy.IsEmpty ||
                spawnedItem.WeaponCopy.Item?.WeaponComponent == null)
                return false;

            var weaponClass = spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass;

            // 如果是投掷武器，检查是否为空袋子
            bool isThrowingWeapon = weaponClass == WeaponClass.ThrowingAxe ||
                                  weaponClass == WeaponClass.ThrowingKnife ||
                                  weaponClass == WeaponClass.Javelin;
            bool isQuiverAndNotEmpty = spawnedItem.IsQuiverAndNotEmpty();

            if (isThrowingWeapon && !isQuiverAndNotEmpty)
            {
                return false;
            }

            // 判断是否为盾牌，并检查是否允许盾牌拾取
            bool isShield = IsShield(spawnedItem);
            if (isShield)
            {
                return IsShieldPickupEnabled();
            }

            return IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent);
        }

        /// <summary>
        /// 判断武器是否可以在马上使用（不考虑填装限制）
        /// </summary>
        /// <param name="weaponComponent">武器组件</param>
        /// <returns>如果可以在马上使用返回true，否则返回false</returns>
        public static bool CanUseWeaponOnHorseback(WeaponComponent weaponComponent)
        {
            if (weaponComponent?.PrimaryWeapon == null)
            {
                return false;
            }
            
            // 添加对WeaponDescriptionId的空检查
            if (string.IsNullOrEmpty(weaponComponent.PrimaryWeapon.WeaponDescriptionId))
            {
                return true; // 如果描述ID为空,假设可以在马上使用
            }
            
            bool isPickWeapon = weaponComponent.PrimaryWeapon.WeaponDescriptionId.Contains("_Pike");
            return !isPickWeapon;
        }
    }
}