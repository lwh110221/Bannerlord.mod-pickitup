using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;


namespace PickItUp.Util
{
    public static class WeaponCheck
    {
        /// <summary>
        /// 检查武器是否为盾牌
        /// </summary>
        /// <param name="weaponClass">武器类型</param>
        /// <returns>是否为盾牌</returns>
        public static bool IsShield(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.SmallShield || weaponClass == WeaponClass.LargeShield;
        }

        /// <summary>
        /// 检查武器是否为单手武器
        /// </summary>
        /// <param name="weaponClass">武器类型</param>
        /// <returns>是否为单手武器</returns>
        public static bool IsOneHandedWeapon(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.OneHandedSword ||
                   weaponClass == WeaponClass.OneHandedAxe ||
                   weaponClass == WeaponClass.Mace ||
                   weaponClass == WeaponClass.OneHandedPolearm ||
                   weaponClass == WeaponClass.Dagger ||
                   weaponClass == WeaponClass.ThrowingAxe ||
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
            try
            {
                if (spawnedItem == null || spawnedItem.WeaponCopy.IsEmpty || spawnedItem.WeaponCopy.Item == null || spawnedItem.WeaponCopy.Item.WeaponComponent == null) return false;
                var weaponClass = spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass;
                return IsShield(weaponClass);
            }
            catch
            {
                return false;
            }
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

            var settings = Settings.Settings.Instance;

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
            try
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

                if (isThrowingWeapon && spawnedItem.WeaponCopy.Amount <= 0)
                {
                    return false;
                }

                // 判断是否为盾牌
                bool isShield = IsShield(weaponClass);

                // 如果是盾牌，允许拾取
                if (isShield)
                {
                    return true;
                }

                return IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"检查武器有效性时出错: {ex.Message}");
#endif
                return false;
            }
        }
    }
}