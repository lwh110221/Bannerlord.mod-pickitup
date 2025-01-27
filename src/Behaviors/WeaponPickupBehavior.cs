using System;
using System.Linq;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using PickItUp.Settings;

namespace PickItUp.Behaviors
{
    public class WeaponPickupBehavior : MissionBehavior
    {
        private float _lastCheckTime;
        private readonly Dictionary<Agent, float> _agentLastPickupAttemptTime = new Dictionary<Agent, float>();
        private readonly float PICKUP_ATTEMPT_COOLDOWN = 2f; // 两次拾取尝试之间的冷却时间
        private readonly float SAFE_DISTANCE_THRESHOLD = 5f; // 安全距离阈值

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            try
            {
                base.OnMissionTick(dt);

                if (!Settings.Settings.Instance.IsEnabled || Mission.Current == null) return;

                _lastCheckTime += dt;
                if (_lastCheckTime >= Settings.Settings.Instance.SearchInterval)
                {
                    _lastCheckTime = 0f;
                    CheckAgentsForWeapons();
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"PickItUp Error: {ex.Message}"));
                }
            }
        }

        private void CheckAgentsForWeapons()
        {
            if (Mission.Current?.Agents == null) return;

            // 清理已经不存在的Agent
            _agentLastPickupAttemptTime.Keys
                .Where(agent => agent == null || !agent.IsActive())
                .ToList()
                .ForEach(agent => _agentLastPickupAttemptTime.Remove(agent));

            foreach (Agent agent in Mission.Current.Agents.Where(IsValidAgentForPickup))
            {
                TryPickupNearbyWeapon(agent);
            }
        }

        private bool IsValidAgentForPickup(Agent agent)
        {
            if (agent == null || !agent.IsActive() || !agent.IsAIControlled || agent.IsMount) 
                return false;

            // 检查是否在冷却中
            if (_agentLastPickupAttemptTime.TryGetValue(agent, out float lastAttemptTime))
            {
                if (Mission.Current.CurrentTime - lastAttemptTime < PICKUP_ATTEMPT_COOLDOWN)
                    return false;
            }

            return !HasWeapon(agent);
        }

        private bool HasWeapon(Agent agent)
        {
            try
            {
                if (agent?.Equipment == null) return true; // 如果无法检查，假设有武器以避免问题

                for (EquipmentIndex index = EquipmentIndex.WeaponItemBeginSlot; 
                     index < EquipmentIndex.NumAllWeaponSlots; 
                     index++)
                {
                    var item = agent.Equipment[index]?.Item;
                    if (item != null && item.IsWeapon)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return true; // 出错时假设有武器
            }
        }

        private void TryPickupNearbyWeapon(Agent agent)
        {
            try
            {
                if (!IsSafeToPickup(agent)) return;

                var nearbyWeapons = Mission.Current.GetNearbyEntities(
                    agent.Position,
                    Settings.Settings.Instance.WeaponSearchRadius,
                    entity => IsValidWeaponEntity(entity)
                ).ToList();

                if (!nearbyWeapons.Any()) return;

                var bestWeapon = FindBestWeapon(nearbyWeapons, agent);
                if (bestWeapon != null)
                {
                    if (Settings.Settings.Instance.DebugMode)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Agent {agent.Name} found weapon to pick up"));
                    }

                    _agentLastPickupAttemptTime[agent] = Mission.Current.CurrentTime;
                    MoveToAndPickupWeapon(agent, bestWeapon);
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Weapon pickup error: {ex.Message}"));
                }
            }
        }

        private bool IsSafeToPickup(Agent agent)
        {
            var nearbyEnemies = Mission.Current.GetNearbyEnemyAgents(
                agent.Position.AsVec2, 
                SAFE_DISTANCE_THRESHOLD,
                agent.Team).ToList();

            if (!nearbyEnemies.Any()) return true; // 没有敌人，当然安全

            // 计算最近的武器距离
            var nearestWeaponDistance = float.MaxValue;
            var nearbyWeapons = Mission.Current.GetNearbyEntities(
                agent.Position,
                Settings.Settings.Instance.WeaponSearchRadius,
                entity => IsValidWeaponEntity(entity));

            foreach (var weapon in nearbyWeapons)
            {
                float distance = agent.Position.DistanceSquared(weapon.Position);
                if (distance < nearestWeaponDistance)
                {
                    nearestWeaponDistance = distance;
                }
            }

            // 计算最近的敌人距离
            float nearestEnemyDistance = float.MaxValue;
            Agent nearestEnemy = null;
            foreach (var enemy in nearbyEnemies)
            {
                float distance = agent.Position.DistanceSquared(enemy.Position);
                if (distance < nearestEnemyDistance)
                {
                    nearestEnemyDistance = distance;
                    nearestEnemy = enemy;
                }
            }

            // 决策逻辑
            if (nearestEnemy == null) return true;

            // 1. 如果武器比敌人近，值得去捡
            if (nearestWeaponDistance < nearestEnemyDistance)
                return true;

            // 2. 如果敌人正在战斗其他目标，可以去捡
            if (nearestEnemy.IsAIControlled && nearestEnemy.AttackingAgent != null && nearestEnemy.AttackingAgent != agent)
                return true;

            // 3. 如果敌人没有武器，也可以去捡
            if (!HasWeapon(nearestEnemy))
                return true;

            // 4. 如果敌人背对着我们，可以去捡
            Vec2 directionToAgent = (agent.Position.AsVec2 - nearestEnemy.Position.AsVec2).Normalized();
            if (nearestEnemy.LookDirection.DotProduct(directionToAgent) < 0f)
                return true;

            // 5. 如果我方附近有友军支援，可以去捡
            var nearbyFriendlies = Mission.Current.GetNearbyAgents(
                agent.Position.AsVec2,
                SAFE_DISTANCE_THRESHOLD,
                agent.Team);

            if (nearbyFriendlies.Count() > nearbyEnemies.Count)
                return true;

            // 6. 如果实在太远了，就别去冒险了
            float maxRiskDistance = SAFE_DISTANCE_THRESHOLD * SAFE_DISTANCE_THRESHOLD;
            return nearestWeaponDistance < maxRiskDistance;
        }

        private SpawnedItemEntity FindBestWeapon(List<SpawnedItemEntity> weapons, Agent agent)
        {
            return weapons
                .OrderBy(w => w.Position.DistanceSquared(agent.Position))
                .ThenByDescending(w => GetWeaponScore(w))
                .FirstOrDefault();
        }

        private float GetWeaponScore(SpawnedItemEntity weaponEntity)
        {
            var weaponComponent = weaponEntity.GameEntity.GetFirstScriptOfType<WeaponComponent>();
            if (weaponComponent?.Item == null) return 0f;

            var weapon = weaponComponent.Item;
            return weapon.Value + weapon.Tier * 100; // 考虑武器价值和等级
        }

        private bool IsValidWeaponEntity(SpawnedItemEntity entity)
        {
            try
            {
                if (entity?.GameEntity == null) return false;

                var weaponComponent = entity.GameEntity.GetFirstScriptOfType<WeaponComponent>();
                if (weaponComponent?.Item == null) return false;

                // 只捡近战武器的逻辑
                if (Settings.Settings.Instance.OnlyMeleeWeapons)
                {
                    return weaponComponent.Item.IsMeleeWeapon;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void MoveToAndPickupWeapon(Agent agent, SpawnedItemEntity weaponEntity)
        {
            try
            {
                var weaponComponent = weaponEntity.GameEntity.GetFirstScriptOfType<WeaponComponent>();
                if (weaponComponent == null) return;

                var distanceSquared = agent.Position.DistanceSquared(weaponEntity.Position);
                if (distanceSquared < 1f)
                {
                    EquipWeaponToAgent(agent, weaponEntity, weaponComponent);
                }
                else
                {
                    WorldPosition targetPos = weaponEntity.Position.ToWorldPosition();
                    if (agent.CanMoveDirectlyToPosition(targetPos))
                    {
                        agent.SetMoveGoToPosition(targetPos);
                    }
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Move to weapon error: {ex.Message}"));
                }
            }
        }

        private void EquipWeaponToAgent(Agent agent, SpawnedItemEntity weaponEntity, WeaponComponent weaponComponent)
        {
            try
            {
                // 找到一个空的武器槽
                EquipmentIndex? emptySlot = null;
                for (EquipmentIndex index = EquipmentIndex.WeaponItemBeginSlot; 
                     index < EquipmentIndex.NumAllWeaponSlots; 
                     index++)
                {
                    if (agent.Equipment[index]?.Item == null)
                    {
                        emptySlot = index;
                        break;
                    }
                }

                if (emptySlot.HasValue)
                {
                    agent.EquipWeaponToSlot(weaponComponent.Item, emptySlot.Value);
                    Mission.Current.RemoveSpawnedItem(weaponEntity);

                    if (Settings.Settings.Instance.DebugMode)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Agent {agent.Name} picked up {weaponComponent.Item.Name}"));
                    }
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Equip weapon error: {ex.Message}"));
                }
            }
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            base.OnAgentDeleted(affectedAgent);
            _agentLastPickupAttemptTime.Remove(affectedAgent);
        }

        public override void OnAgentDroppedItem(Agent agent, SpawnedItemEntity item)
        {
            base.OnAgentDroppedItem(agent, item);
            
            if (!Settings.Settings.Instance.IsEnabled) return;

            try 
            {
                // 当有AI丢弃武器时，让附近没有武器的AI考虑捡起
                var nearbyAgents = Mission.Current.GetNearbyAgents(
                    item.Position.AsVec2,
                    Settings.Settings.Instance.WeaponSearchRadius,
                    agent.Team);

                foreach (var nearbyAgent in nearbyAgents.Where(a => 
                    a != agent && 
                    IsValidAgentForPickup(a)))
                {
                    TryPickupNearbyWeapon(nearbyAgent);
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"OnAgentDroppedItem error: {ex.Message}"));
                }
            }
        }

        public override void OnAgentPickupItem(Agent agent, SpawnedItemEntity item)
        {
            base.OnAgentPickupItem(agent, item);
            
            if (!Settings.Settings.Instance.IsEnabled) return;

            try
            {
                // 当AI成功捡起武器时，从尝试时间字典中移除
                if (_agentLastPickupAttemptTime.ContainsKey(agent))
                {
                    _agentLastPickupAttemptTime.Remove(agent);
                }

                if (Settings.Settings.Instance.DebugMode)
                {
                    var weaponComponent = item.GameEntity.GetFirstScriptOfType<WeaponComponent>();
                    if (weaponComponent != null)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Agent {agent.Name} successfully picked up {weaponComponent.Item.Name}"));
                    }
                }
            }
            catch (Exception ex)
            {
                if (Settings.Settings.Instance.DebugMode)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"OnAgentPickupItem error: {ex.Message}"));
                }
            }
        }
    }
} 