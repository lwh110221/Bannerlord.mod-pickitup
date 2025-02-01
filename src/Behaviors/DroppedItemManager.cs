using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using Debug = TaleWorlds.Library.Debug;

namespace PickItUp.Behaviors
{
    public class DroppedItemManager : MissionBehavior
    {
        private MBBindingList<MissionObject> _droppedItems;
#if DEBUG
        private const bool _isDebugMode = true;
#else
        private const bool _isDebugMode = false;
#endif
        
        public DroppedItemManager()
        {
            _droppedItems = new MBBindingList<MissionObject>();
#if DEBUG
            Debug.Print("PickItUp: DroppedItemManager已初始化");
#endif
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

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
            if (item != null && !_droppedItems.Contains(item))
            {
                _droppedItems.Add(item);
                
                if (item is SpawnedItemEntity spawnedItem)
                {
                    // 设置物品不会被移除
                    spawnedItem.SetDisabled(false);  // 确保物品启用
                    spawnedItem.HasLifeTime = false; // 设置物品永久存在

#if DEBUG
                    string itemName = spawnedItem.WeaponCopy.Item?.Name?.ToString() ?? "未知物品";
                    string debugInfo = $"物品已注册: {itemName}";
                    debugInfo += $"\n - 当前追踪物品数量: {_droppedItems.Count}";
                    debugInfo += $"\n - 有生命周期: {spawnedItem.HasLifeTime}";
                    debugInfo += $"\n - 是否永久存在: {spawnedItem.IsDisabled}";
                    
                    if (spawnedItem.WeaponCopy.Item != null)
                    {
                        debugInfo += $"\n - 物品类型: {GetItemType(spawnedItem.WeaponCopy.Item)}";
                    }
                    
                    Debug.Print(debugInfo);
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
                    Debug.Print($"物品已移除: {itemName}\n当前追踪物品数量: {_droppedItems.Count}");
                }
#endif
            }
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
#if DEBUG
            Debug.Print("DroppedItemManager行为已初始化");
#endif
        }

        public override void OnRemoveBehavior()
        {
#if DEBUG
            Debug.Print($"DroppedItemManager行为已移除\n最终追踪物品数量: {_droppedItems.Count}");
#endif
            base.OnRemoveBehavior();
            _droppedItems.Clear();
        }
    }
} 