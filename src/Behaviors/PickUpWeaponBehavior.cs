using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace PickItUp.Behaviors
{
    public class PickUpWeaponBehavior : MissionBehavior
    {
        private readonly Dictionary<Agent, float> _lastPickupAttemptTime = new();
        private const float PICKUP_COOLDOWN = 2f; // 2秒冷却时间
        private const float SEARCH_RADIUS = 5f; // 5米搜索范围
        private const float PICKUP_DISTANCE = 1.5f; // 拾取距离

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public void OnAgentDropWeapon(Agent agent, MissionWeapon droppedWeapon, EquipmentIndex slot)
        {
            if (!ShouldTryPickup(agent)) return;
            
            FindAndPickupNearbyWeapon(agent);
        }

        private bool ShouldTryPickup(Agent agent)
        {
            // 检查是否是AI控制的
            if (!agent.IsAIControlled) return false;
            
            // 检查是否还活着
            if (!agent.IsActive() || agent.Health <= 0) return false;
            
            // 检查是否还有其他武器
            if (HasAnyUsableWeapon(agent)) return false;
            
            // 检查冷却时间
            if (_lastPickupAttemptTime.TryGetValue(agent, out float lastAttempt))
            {
                if (Mission.Current.CurrentTime - lastAttempt < PICKUP_COOLDOWN)
                    return false;
            }

            return true;
        }

        private bool HasAnyUsableWeapon(Agent agent)
        {
            for (EquipmentIndex index = EquipmentIndex.WeaponItemBeginSlot; 
                 index < EquipmentIndex.NumAllWeaponSlots; index++)
            {
                var equipment = agent.Equipment[index];
                if (!equipment.IsEmpty && equipment.Item != null && equipment.Item.WeaponComponent != null)
                    return true;
            }
            return false;
        }

        private void FindAndPickupNearbyWeapon(Agent agent)
        {
            _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime;

            var weaponEntities = Mission.Current
                .MissionObjects
                .OfType<SpawnedItemEntity>()
                .Where(item => IsValidWeapon(item) && 
                       item.GameEntity.GlobalPosition.DistanceSquared(agent.Position) <= SEARCH_RADIUS * SEARCH_RADIUS)
                .OrderBy(item => agent.Position.DistanceSquared(item.GameEntity.GlobalPosition))
                .ToList();

            if (!weaponEntities.Any()) return;

            var closestWeapon = weaponEntities.First();
            TryPickupWeapon(agent, closestWeapon);
        }

        private bool IsValidWeapon(SpawnedItemEntity item)
        {
            return item?.GameEntity?.IsVisibleIncludeParents() == true && 
                   item.WeaponCopy.Item != null && 
                   item.WeaponCopy.Item.WeaponComponent != null;
        }

        private bool TryPickupWeapon(Agent agent, SpawnedItemEntity weaponEntity)
        {
            var weaponPosition = weaponEntity.GameEntity.GlobalPosition;
            var distanceSquared = agent.Position.DistanceSquared(weaponPosition);
            
            // 如果太远，移动到武器位置
            if (distanceSquared > PICKUP_DISTANCE * PICKUP_DISTANCE)
            {
                var worldPosition = weaponPosition.ToWorldPosition();
                agent.SetScriptedPosition(ref worldPosition, false);
                return false;
            }
            
            // 如果够近，拾取武器
            // 找一个空的武器槽
            EquipmentIndex? emptySlot = null;
            for (EquipmentIndex index = EquipmentIndex.WeaponItemBeginSlot; 
                 index < EquipmentIndex.NumAllWeaponSlots; index++)
            {
                if (agent.Equipment[index].IsEmpty)
                {
                    emptySlot = index;
                    break;
                }
            }
            
            if (emptySlot.HasValue)
            {
                // 播放拾取音效
                Mission.Current.MakeSound(
                    SoundEvent.GetEventIdFromString("event:/mission/combat/shield/pickup"), 
                    weaponPosition,
                    false, 
                    true,
                    agent.Index,
                    -1);

                // 装备武器
                agent.EquipWeaponFromSpawnedItemEntity(emptySlot.Value, weaponEntity, true);
                return true;
            }

            return false;
        }
    }
} 