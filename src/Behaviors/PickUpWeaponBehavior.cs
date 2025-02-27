using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;
using Path = System.IO.Path;

namespace PickItUp.Behaviors
{
    public class PickUpWeaponBehavior : MissionBehavior
    {
        private float PickupDelay => Settings.Settings.Instance?.PickupDelay ?? 1.5f;          //拾取延迟(检查到没有近战武器时，此时间结束后才去拾取)
        private float SearchRadius => Settings.Settings.Instance?.SearchRadius ?? 5.0f;        //搜索半径
        private float PickupCooldown => Settings.Settings.Instance?.PickupCooldown ?? 1.0f;    //拾取冷却时间

#if DEBUG
        private readonly string _logFilePath;
#endif

        private readonly Dictionary<Agent, SpawnedItemEntity> _agentTargetWeapons = new Dictionary<Agent, SpawnedItemEntity>();
        private readonly Dictionary<Agent, float> _agentPickupTimers = new Dictionary<Agent, float>();
        private readonly Dictionary<Agent, float> _agentLastPickupAttempts = new Dictionary<Agent, float>();

        public PickUpWeaponBehavior()
        {
#if DEBUG
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _logFilePath = Path.Combine(desktopPath, "PickItUp_log.txt");
            File.WriteAllText(_logFilePath, $"=== PickItUp Mod Log Started at {DateTime.Now} ===\n");
#endif
        }

        private void DebugLog(string message)
        {
#if DEBUG
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(_logFilePath, logMessage);
            }
            catch
            {
            }
#endif
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        private bool CanBeUsedAsMeleeWeapon(WeaponClass weaponClass)
        {
            // 纯近战武器
            if (weaponClass == WeaponClass.OneHandedSword ||    // 单手剑
                weaponClass == WeaponClass.TwoHandedSword ||    // 双手剑
                weaponClass == WeaponClass.OneHandedAxe ||      // 单手斧
                weaponClass == WeaponClass.TwoHandedAxe ||      // 双手斧
                weaponClass == WeaponClass.Mace ||              // 单手锤
                weaponClass == WeaponClass.TwoHandedMace ||     // 双手锤
                weaponClass == WeaponClass.OneHandedPolearm ||  // 单手长杆武器（包括短矛等）
                weaponClass == WeaponClass.TwoHandedPolearm ||  // 双手长杆武器（包括长矛、骑枪等）
                weaponClass == WeaponClass.Dagger)              // 匕首
            {
                return true;
            }

            // 可以用作近战武器的投掷武器
            if (weaponClass == WeaponClass.ThrowingAxe ||
                weaponClass == WeaponClass.ThrowingKnife ||
                weaponClass == WeaponClass.Javelin)
            {
                return true;
            }

            return false;
        }

        private bool CanAgentPickup(Agent agent)
        {
            try
            {
                // 基本检查
                if (agent == null || !agent.IsActive() || agent.Health <= 0 || !agent.IsAIControlled)
                {
                    return false;
                }

                // 检查是否是坐骑
                if (agent.IsMount)
                {
                    return false;
                }

                // 检查是否已有可用的近战武器
                bool hasUsableMeleeWeapon = false;
                if (agent.Equipment != null)
                {
                    for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                    {
                        var equipment = agent.Equipment[i];
                        if (!equipment.IsEmpty && equipment.Item?.WeaponComponent != null)
                        {
                            var weaponClass = equipment.Item.WeaponComponent.PrimaryWeapon.WeaponClass;

                            if (CanBeUsedAsMeleeWeapon(weaponClass))
                            {
                                hasUsableMeleeWeapon = true;
                                break;
                            }
                        }
                    }
                }

                return !hasUsableMeleeWeapon;
            }
            catch (Exception ex)
            {
                DebugLog($"检查Agent {agent?.Name} 是否可以拾取时出错: {ex.Message}");
                return false;
            }
        }

        private bool IsMeleeWeapon(WeaponComponent weaponComponent)
        {
            if (weaponComponent?.PrimaryWeapon == null) return false;
            return CanBeUsedAsMeleeWeapon(weaponComponent.PrimaryWeapon.WeaponClass);
        }

        public override void OnMissionTick(float dt)
        {
            foreach (Agent agent in Mission.Current.Agents.Where(a => CanAgentPickup(a)))
            {
                // 检查拾取冷却
                if (_agentLastPickupAttempts.TryGetValue(agent, out float lastAttempt) && 
                    Mission.Current.CurrentTime - lastAttempt < PickupCooldown)
                    continue;

                // 检查拾取延迟
                if (!_agentPickupTimers.ContainsKey(agent))
                {
                    _agentPickupTimers[agent] = Mission.Current.CurrentTime + PickupDelay;
                    continue;
                }

                if (Mission.Current.CurrentTime < _agentPickupTimers[agent])
                    continue;

                // 如果AI没有目标武器,寻找最近的可用武器
                if (!_agentTargetWeapons.ContainsKey(agent))
                {
                    SpawnedItemEntity nearestWeapon = FindNearestWeapon(agent);
                    if (nearestWeapon != null)
                    {
                        _agentTargetWeapons[agent] = nearestWeapon;
                        // 命令AI移动到武器位置
                        MoveToWeapon(agent, nearestWeapon);
                        continue;
                    }
                }

                // 如果AI有目标武器,检查是否到达位置
                if (_agentTargetWeapons.TryGetValue(agent, out SpawnedItemEntity targetWeapon))
                {
                    if (targetWeapon == null || targetWeapon.IsRemoved)
                    {
                        _agentTargetWeapons.Remove(agent);
                        continue;
                    }

                    float distanceSq = agent.Position.DistanceSquared(targetWeapon.GameEntity.GlobalPosition);
                    if (agent.CanReachAndUseObject(targetWeapon, distanceSq))
                    {
                        TryPickupWeapon(agent);
                        _agentTargetWeapons.Remove(agent);
                    }
                }
            }
        }

        private SpawnedItemEntity FindNearestWeapon(Agent agent)
        {
            var agentPosition = agent.Position;
            var itemsInRange = new List<SpawnedItemEntity>();
            
            foreach (var missionObject in Mission.Current.MissionObjects)
            {
                if (missionObject is SpawnedItemEntity spawnedItem)
                {
                    if (spawnedItem != null && !spawnedItem.IsRemoved && !spawnedItem.IsDeactivated)
                    {
                        float distance = spawnedItem.GameEntity.GlobalPosition.Distance(agentPosition);
                        if (distance <= SearchRadius && IsMeleeWeapon(spawnedItem.WeaponCopy.Item?.WeaponComponent))
                        {
                            itemsInRange.Add(spawnedItem);
                        }
                    }
                }
            }

            return itemsInRange.OrderBy(x => x.GameEntity.GlobalPosition.Distance(agentPosition)).FirstOrDefault();
        }

        private void MoveToWeapon(Agent agent, SpawnedItemEntity weapon)
        {
            if (agent == null || weapon == null) return;

            WorldPosition targetPosition = new WorldPosition(Mission.Current.Scene, weapon.GameEntity.GlobalPosition);
            agent.SetScriptedPosition(ref targetPosition, false, Agent.AIScriptedFrameFlags.NoAttack);
        }

        private void TryPickupWeapon(Agent agent)
        {
            try
            {
                // 获取周围的所有掉落武器
                var agentPosition = agent.Position;
                var itemsInRange = new List<SpawnedItemEntity>();
                
                // 遍历所有任务物品找到在范围内的
                foreach (var missionObject in Mission.Current.MissionObjects)
                {
                    if (missionObject is SpawnedItemEntity spawnedItem)
                    {
                        if (spawnedItem != null && !spawnedItem.IsRemoved && !spawnedItem.IsDeactivated)
                        {
                            float distance = spawnedItem.GameEntity.GlobalPosition.Distance(agentPosition);
                            if (distance <= SearchRadius)
                            {
                                itemsInRange.Add(spawnedItem);
                            }
                        }
                    }
                }

                // 按距离排序
                itemsInRange = itemsInRange.OrderBy(x => x.GameEntity.GlobalPosition.Distance(agentPosition)).ToList();

                foreach (var spawnedItem in itemsInRange)
                {
                    // 检查是否是可用的近战武器
                    if (spawnedItem.WeaponCopy.IsEmpty || !IsMeleeWeapon(spawnedItem.WeaponCopy.Item?.WeaponComponent))
                        continue;

                    // 检查是否可以拾取
                    if (!agent.CanQuickPickUp(spawnedItem))
                        continue;

                    // 检查是否在可达范围内
                    float distanceSq = agent.Position.DistanceSquared(spawnedItem.GameEntity.GlobalPosition);
                    
                    // 如果在拾取范围内，直接拾取
                    if (agent.CanReachAndUseObject(spawnedItem, distanceSq))
                    {
                        EquipmentIndex slotIndex = MissionEquipment.SelectWeaponPickUpSlot(agent, spawnedItem.WeaponCopy, spawnedItem.IsStuckMissile());
                        if (slotIndex != EquipmentIndex.None)
                        {
                            agent.HandleStartUsingAction(spawnedItem, -1);
                            _agentLastPickupAttempts[agent] = Mission.Current.CurrentTime;
                            _agentPickupTimers.Remove(agent);
                            DebugLog($"Agent {agent.Name} 尝试拾取武器 {spawnedItem.WeaponCopy.Item?.Name}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"TryPickupWeapon 出错: {ex.Message}");
            }
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            base.OnAgentDeleted(affectedAgent);
            _agentPickupTimers.Remove(affectedAgent);
            _agentLastPickupAttempts.Remove(affectedAgent);
            _agentTargetWeapons.Remove(affectedAgent);
        }
    }
}