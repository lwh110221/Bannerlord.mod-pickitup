using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using Debug = TaleWorlds.Library.Debug;
using PickItUp.Settings;
using System;

namespace PickItUp.Behaviors
{
    public class DroppedItemManager : MissionBehavior
    {
        private readonly MBBindingList<MissionObject> _droppedItems;
        private bool _lastPersistenceState;
        private float _settingCheckTimer = 0f;
        private const float SETTING_CHECK_INTERVAL = 5f; // 每5秒检查一次设置
        private bool _hasDisplayedMessage = false;

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
            LogDebug("武器持久化已初始化");
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

            if (!_hasDisplayedMessage && Settings.Settings.Instance.ShowStatusMessage)
            {
                _hasDisplayedMessage = true;
                
                string statusEN = Settings.Settings.Instance.EnableWeaponPersistence ? "ON" : "OFF";
                string statusCN = Settings.Settings.Instance.EnableWeaponPersistence ? "已开启" : "已关闭";
                
                InformationManager.DisplayMessage(new InformationMessage(
                    $"PIU: Weapon do not disappear-{statusEN}",
                    Colors.Yellow));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"PIU：武器不消失-{statusCN}",
                    Colors.Yellow));
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
            // 在每次进入新场景时重新获取设置
            _hasDisplayedMessage = false;

#if DEBUG
            LogDebug($"武器持久化已初始化");
#endif
        }

        public override void OnRemoveBehavior()
        {
            try
            {
#if DEBUG
                LogDebug($"=== 开始场景退出清理 ===");
                LogDebug($"当前注册物品数量: {_droppedItems.Count}");
#endif
                // 先清理所有物品的引用
                foreach (var item in _droppedItems.ToList())
                {
                    try
                    {
                        if (item is SpawnedItemEntity spawnedItem)
                        {
                            // 确保物品可以被游戏正常清理
                            spawnedItem.HasLifeTime = true;
                            spawnedItem.SetDisabled(true);
#if DEBUG
                            string itemName = spawnedItem.WeaponCopy.Item?.Name?.ToString() ?? "未知物品";
                            LogDebug($"清理物品: {itemName}");
#endif
                        }
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        LogDebug($"清理单个物品时出错: {ex.Message}");
#endif
                    }
                }

                // 清空列表
                _droppedItems.Clear();
                
                // 调用基类的清理
                base.OnRemoveBehavior();

#if DEBUG
                LogDebug("=== 场景退出清理完成 ===");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                LogDebug($"场景退出清理时出错: {ex.Message}");
#endif
            }
        }
    }
} 