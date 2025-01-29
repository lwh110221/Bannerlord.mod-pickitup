using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;  

namespace PickItUp.Behaviors
{
    public class PickUpWeaponBehavior : MissionBehavior
    {
        private readonly Dictionary<Agent, float> _lastPickupAttemptTime = new();
        private readonly Dictionary<Agent, (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot)> _pickupAnimationTracker = new();
        private readonly Dictionary<Agent, float> _lastPathCalculationTime = new(); // 添加路径计算时间跟踪
        private readonly List<SpawnedItemEntity> _nearbyWeapons = new(); // 用于缓存附近武器的列表
        private const float PICKUP_COOLDOWN = 1f; //冷却时间 秒
        private const float SEARCH_RADIUS = 5f; //搜索范围 米
        private const float PICKUP_DISTANCE = 1f; //拾取距离 米
        private const float PICKUP_DELAY = 2f; // 拾取延迟 秒
        private const float PICKUP_ANIMATION_DURATION = 0.5f; // 拾取动画持续时间 秒
        private const float AGENT_RADIUS = 0.4f; // AI的碰撞半径
        private const float PATH_CALCULATION_INTERVAL = 0.2f; // 路径计算间隔（秒）

#if DEBUG
        private readonly string _logFilePath;
#endif

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
                // 忽略错误
            }
#endif
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public void OnAgentDropWeapon(Agent agent, MissionWeapon droppedWeapon, EquipmentIndex slot)
        {
            if (agent == null) return;
            
            // 设置拾取延迟
            _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime + PICKUP_DELAY;
            
            DebugLog($"Agent {agent.Name} 丢弃武器从槽 {slot}, IsEmpty: {droppedWeapon.IsEmpty}");
            
            try
            {
                // 通知周围的AI检查是否需要拾取武器
                var nearbyAgents = new MBList<Agent>();
                Mission.Current.GetNearbyAgents(agent.Position.AsVec2, SEARCH_RADIUS, nearbyAgents);
                
                foreach(var nearbyAgent in nearbyAgents.Where(a => a != agent && CanAgentPickup(a)))
                {
                    DebugLog($"通知附近的Agent {nearbyAgent.Name} 检查是否需要拾取武器");
                    _lastPickupAttemptTime[nearbyAgent] = Mission.Current.CurrentTime + PICKUP_DELAY; // 重置附近AI的冷却时间
                }
                
                nearbyAgents.Clear(); // 清理列表
            }
            catch (Exception ex)
            {
                DebugLog($"OnAgentDropWeapon出错: {ex.Message}");
            }
        }

        private bool CanAgentPickup(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0 || !agent.IsAIControlled)
            {
                return false;
            }

            // 检查冷却时间和拾取延迟
            if (_lastPickupAttemptTime.TryGetValue(agent, out float lastAttempt))
            {
                if (Mission.Current.CurrentTime < lastAttempt || Mission.Current.CurrentTime - lastAttempt < PICKUP_COOLDOWN)
                {
                    return false;
                }
            }

            // 检查是否已有武器
            try
            {
                if (agent.Equipment != null)
                {
                    for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                    {
                        var equipment = agent.Equipment[i];
                        if (!equipment.IsEmpty && equipment.Item != null && 
                            equipment.Item.Type != ItemObject.ItemTypeEnum.Shield && 
                            equipment.Item.Type != ItemObject.ItemTypeEnum.Banner)
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"检查装备时出错: {ex.Message}");
                return false;
            }

            return true;
        }

        private bool IsMeleeWeapon(WeaponComponent weaponComponent)
        {
            if (weaponComponent?.PrimaryWeapon == null) return false;

            var weaponClass = weaponComponent.PrimaryWeapon.WeaponClass;
            return weaponClass == WeaponClass.OneHandedSword ||
                   weaponClass == WeaponClass.TwoHandedSword ||
                   weaponClass == WeaponClass.OneHandedAxe ||
                   weaponClass == WeaponClass.TwoHandedAxe ||
                   weaponClass == WeaponClass.Mace ||
                   weaponClass == WeaponClass.TwoHandedMace ||
                   weaponClass == WeaponClass.OneHandedPolearm ||
                   weaponClass == WeaponClass.TwoHandedPolearm ||
                   weaponClass == WeaponClass.Dagger;
        }

        private SpawnedItemEntity FindNearestWeapon(Agent agent)
        {
            if (agent == null) return null;

            _nearbyWeapons.Clear();
            
            // 获取附近所有MissionObjects
            foreach (var missionObject in Mission.Current.MissionObjects)
            {
                var spawnedItem = missionObject as SpawnedItemEntity;
                if (spawnedItem == null || !IsValidWeapon(spawnedItem)) continue;

                // 使用直线距离进行粗略筛选使用更大的范围
                var distance = agent.Position.Distance(spawnedItem.GameEntity.GlobalPosition);
                if (distance <= SEARCH_RADIUS * 1.5f) // 使用1.5倍的搜索范围进行初步筛选
                {
                    _nearbyWeapons.Add(spawnedItem);
                }
            }

            if (_nearbyWeapons.Count == 0) return null;

            SpawnedItemEntity nearestWeapon = null;
            float minPathDistance = float.MaxValue;

            foreach (var weapon in _nearbyWeapons)
            {
                float pathDistance;
                WorldPosition agentWorldPosition = agent.GetWorldPosition();
                WorldPosition weaponWorldPosition = new WorldPosition(Mission.Current.Scene, weapon.GameEntity.GlobalPosition);
                
                if (Mission.Current.Scene.GetPathDistanceBetweenPositions(
                    ref agentWorldPosition, 
                    ref weaponWorldPosition,
                    AGENT_RADIUS,
                    out pathDistance))
                {
                    // 使用实际路径距离作为最终判断标准
                    if (pathDistance > SEARCH_RADIUS) continue;

                    if (pathDistance < minPathDistance)
                    {
                        minPathDistance = pathDistance;
                        nearestWeapon = weapon;
                    }
                }
            }

            return nearestWeapon;
        }

        private bool IsValidWeapon(SpawnedItemEntity spawnedItem)
        {
            return spawnedItem?.GameEntity != null && 
                   spawnedItem.GameEntity.IsVisibleIncludeParents() &&
                   spawnedItem.WeaponCopy.Item?.WeaponComponent != null &&
                   IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent);
        }

        public override void OnMissionTick(float dt)
        {
            try
            {
                base.OnMissionTick(dt);

                var completedAnimations = new List<Agent>();
                foreach (var kvp in _pickupAnimationTracker)
                {
                    var agent = kvp.Key;
                    var (startTime, weaponToPickup, targetSlot) = kvp.Value;

                    if (Mission.Current.CurrentTime - startTime >= PICKUP_ANIMATION_DURATION)
                    {
                        bool removeWeapon;
                        agent.OnItemPickup(weaponToPickup, targetSlot, out removeWeapon);
                        _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime;
                        completedAnimations.Add(agent);
                        DebugLog($"Agent {agent.Name} 完成拾取动画并拾取武器到槽位 {targetSlot}");
                    }
                }
                foreach (var agent in completedAnimations)
                {
                    _pickupAnimationTracker.Remove(agent);
                }

                foreach (var agent in Mission.Current.Agents.Where(a => a != null && CanAgentPickup(a)))
                {
                    if (_pickupAnimationTracker.ContainsKey(agent))
                        continue;

                    // 检查路径计算间隔
                    if (_lastPathCalculationTime.TryGetValue(agent, out float lastCalcTime) &&
                        Mission.Current.CurrentTime - lastCalcTime < PATH_CALCULATION_INTERVAL)
                    {
                        continue;
                    }

                    try
                    {
                        var nearbyWeapon = FindNearestWeapon(agent);
                        if (nearbyWeapon != null)
                        {
                            float pathDistance;
                            WorldPosition agentWorldPosition = agent.GetWorldPosition();
                            WorldPosition weaponWorldPosition = new WorldPosition(Mission.Current.Scene, nearbyWeapon.GameEntity.GlobalPosition);
                            
                            if (Mission.Current.Scene.GetPathDistanceBetweenPositions(
                                ref agentWorldPosition, 
                                ref weaponWorldPosition,
                                AGENT_RADIUS,
                                out pathDistance))
                            {
                                // 更新路径计算时间
                                _lastPathCalculationTime[agent] = Mission.Current.CurrentTime;

                                if (pathDistance <= PICKUP_DISTANCE)
                                {
                                    // 尝试拾取武器
                                    EquipmentIndex emptySlot = EquipmentIndex.None;
                                    for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                                    {
                                        if (agent.Equipment[i].IsEmpty)
                                        {
                                            emptySlot = i;
                                            break;
                                        }
                                    }

                                    if (emptySlot != EquipmentIndex.None)
                                    {
                                        agent.SetActionChannel(0, ActionIndexCache.Create("act_pickup_down_begin"), ignorePriority: false, 0UL);
                                        _pickupAnimationTracker[agent] = (Mission.Current.CurrentTime, nearbyWeapon, emptySlot);
                                        DebugLog($"Agent {agent.Name} 开始拾取动画");
                                    }
                                }
                                else
                                {
                                    // 移动到武器位置
                                    agent.AIMoveToGameObjectEnable(nearbyWeapon, null, Agent.AIScriptedFrameFlags.NoAttack);
                                    DebugLog($"Agent {agent.Name} 移动到武器位置，路径距离: {pathDistance}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"处理单个AI时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"OnMissionTick出错: {ex.Message}");
            }
        }

        public override void OnRemoveBehavior()
        {
            base.OnRemoveBehavior();
            _lastPickupAttemptTime.Clear();
            _pickupAnimationTracker.Clear();
            _lastPathCalculationTime.Clear();
            DebugLog("=== 行为移除 ===");
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            DebugLog("=== 行为初始化 ===");
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            base.OnAgentDeleted(affectedAgent);
            _lastPickupAttemptTime.Remove(affectedAgent);
            _pickupAnimationTracker.Remove(affectedAgent);
            _lastPathCalculationTime.Remove(affectedAgent); // 清理路径计算时间记录
        }
    }
} 