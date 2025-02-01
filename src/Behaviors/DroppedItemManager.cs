using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using Debug = TaleWorlds.Library.Debug;
using PickItUp.Settings;

namespace PickItUp.Behaviors
{
    public class DroppedItemManager : MissionBehavior
    {
        private MBBindingList<MissionObject> _droppedItems;
        private bool _lastPersistenceState;
        private float _settingCheckTimer = 0f;
        private const float SETTING_CHECK_INTERVAL = 5f; // 每5秒检查一次设置

#if DEBUG
        private const bool _isDebugMode = true;

        private void LogDebug(string message)
        {
            if (_isDebugMode)
            {
                Debug.Print($"PickItUp: {message}");
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
            _lastPersistenceState = Settings.Settings.Instance.EnableWeaponPersistence;
#if DEBUG
            LogDebug("DroppedItemManager已初始化");
#endif
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            _settingCheckTimer += dt;
            if (_settingCheckTimer >= SETTING_CHECK_INTERVAL)
            {
                _settingCheckTimer = 0f;
                bool currentState = Settings.Settings.Instance.EnableWeaponPersistence;
                if (_lastPersistenceState != currentState)
                {
                    OnPersistenceSettingChanged(currentState);
                    _lastPersistenceState = currentState;
                }
            }
        }

        private void OnPersistenceSettingChanged(bool newState)
        {
#if DEBUG
            LogDebug($"武器持久化设置已更改为: {(newState ? "开启" : "关闭")}");
#endif
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
            // 当设置开启时，不做任何操作，让新的掉落物品自然进入系统
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
            if (!Settings.Settings.Instance.EnableWeaponPersistence) return;

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
#if DEBUG
            LogDebug("DroppedItemManager行为已初始化");
#endif
        }

        public override void OnRemoveBehavior()
        {
#if DEBUG
            LogDebug($"DroppedItemManager行为已移除\n最终注册物品数量: {_droppedItems.Count}");
#endif
            base.OnRemoveBehavior();
            _droppedItems.Clear();
        }
    }
} 