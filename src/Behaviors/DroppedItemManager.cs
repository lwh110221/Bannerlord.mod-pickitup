using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Linq;
using System;

namespace PickItUp.Behaviors
{
    public class DroppedItemManager : MissionBehavior
    {
        private readonly MBBindingList<MissionObject> _droppedItems;
        private bool _lastPersistenceState;
        private float _settingCheckTimer = 0f;
        private const float SETTING_CHECK_INTERVAL = 8f;
        private bool _hasDisplayedMessage = false;

#if DEBUG
        private const bool _isDebugMode = true;

        private void LogDebug(string message)
        {
            if (_isDebugMode)
            {
                DebugHelper.Log("DroppedItemManager", message);
            }
        }

        private string GetItemDebugInfo(SpawnedItemEntity spawnedItem)
        {
            string itemName = spawnedItem.WeaponCopy.Item?.Name?.ToString() ?? "未知物品";
            string debugInfo = $"物品已注册: {itemName}";
            debugInfo += $"\n - 当前注册物品数量: {_droppedItems.Count}";
            debugInfo += $"\n - 有生命周期: {spawnedItem.HasLifeTime}";
            debugInfo += $"\n - 是否永久存在: {spawnedItem.IsDisabled}";

            if (spawnedItem.WeaponCopy.Item != null)
            {
                debugInfo += $"\n - 物品类型: {GetItemType(spawnedItem.WeaponCopy.Item)}";
            }

            return debugInfo;
        }
#endif

        public DroppedItemManager()
        {
            _droppedItems = new MBBindingList<MissionObject>();
            _lastPersistenceState = Settings.McmSettings.Instance.EnableWeaponPersistence;
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            _settingCheckTimer += dt;
            if (_settingCheckTimer >= SETTING_CHECK_INTERVAL)
            {
                _settingCheckTimer = 0f;
                bool currentState = Settings.McmSettings.Instance.EnableWeaponPersistence;
                if (_lastPersistenceState != currentState)
                {
                    OnPersistenceSettingChanged(currentState);
                    _lastPersistenceState = currentState;
                }
            }

            if (!_hasDisplayedMessage && Settings.McmSettings.Instance.ShowStatusMessage)
            {
                _hasDisplayedMessage = true;

                string statusEN = Settings.McmSettings.Instance.EnableWeaponPersistence ? "ON" : "OFF";

                InformationManager.DisplayMessage(new InformationMessage(
                    $"PIU: Weapon do not disappear-{statusEN}",
                    Colors.Yellow));
            }
        }

        private void OnPersistenceSettingChanged(bool newState)
        {
            if (!newState)
            {
                // 设置被禁用时，只处理已注册的物品
                foreach (var item in _droppedItems.OfType<SpawnedItemEntity>())
                {
                    if (item != null)
                    {
                        item.HasLifeTime = true;
                    }
                }
                _droppedItems.Clear();
            }
        }

        private string GetItemType(ItemObject item)
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

        public void RegisterDroppedItem(MissionObject item)
        {
            if (!Settings.McmSettings.Instance.EnableWeaponPersistence) return;

            if (item != null && !_droppedItems.Contains(item))
            {
                _droppedItems.Add(item);

                if (item is SpawnedItemEntity spawnedItem)
                {
                    spawnedItem.SetDisabled(false);
                    spawnedItem.HasLifeTime = false;

#if DEBUG
                    LogDebug(GetItemDebugInfo(spawnedItem));
#endif
                }
            }
        }

        public void UnregisterDroppedItem(MissionObject item)
        {
            if (item != null && _droppedItems.Contains(item))
            {
                _droppedItems.Remove(item);

#if DEBUG
                if (item is SpawnedItemEntity spawnedItem)
                {
                    string itemName = spawnedItem.WeaponCopy.Item?.Name?.ToString() ?? "未知物品";
                    LogDebug($"物品已移除: {itemName}\n当前注册物品数量: {_droppedItems.Count}");
                }
#endif
            }
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasDisplayedMessage = false;
        }

        public override void OnRemoveBehavior()
        {
            foreach (var item in _droppedItems.ToList())
            {
                if (item is SpawnedItemEntity spawnedItem)
                {
                    spawnedItem.HasLifeTime = true;
                    spawnedItem.SetDisabled(true);
                }
            }

            _droppedItems.Clear();
            base.OnRemoveBehavior();
        }
    }
}