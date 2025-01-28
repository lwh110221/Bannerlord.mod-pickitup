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
        private const float PICKUP_COOLDOWN = 1f; //冷却时间 秒
        private const float SEARCH_RADIUS = 5f; //搜索范围 米
        private const float PICKUP_DISTANCE = 1.5f; //拾取距离 米
        private const float PICKUP_DELAY = 2f; // 拾取延迟 秒
        private readonly string _logFilePath;

        // 初始化debug日志
        public PickUpWeaponBehavior()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _logFilePath = Path.Combine(desktopPath, "PickItUp_log.txt");
            File.WriteAllText(_logFilePath, $"=== PickItUp Mod Log Started at {DateTime.Now} ===\n");
        }

        private void DebugLog(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(_logFilePath, logMessage);
            }
            catch
            {
                // 忽略错误
            }
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public void OnAgentDropWeapon(Agent agent, MissionWeapon droppedWeapon, EquipmentIndex slot)
        {
            try
            {
                if (agent == null) return;
                
                DebugLog($"Agent {agent.Name} 丢弃武器从槽 {slot}, IsEmpty: {droppedWeapon.IsEmpty}");
                
                // 设置拾取延迟
                _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime + PICKUP_DELAY;
                
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

        public override void OnMissionTick(float dt)
        {
            try
            {
                base.OnMissionTick(dt);

                foreach (var agent in Mission.Current.Agents.Where(a => a != null && CanAgentPickup(a)))
                {
                    try
                    {
                        // 寻找最近的武器
                        var nearbyWeapons = Mission.Current.MissionObjects
                            .OfType<SpawnedItemEntity>()
                            .Where(item => 
                                item?.GameEntity != null && 
                                item.GameEntity.IsVisibleIncludeParents() &&
                                item.WeaponCopy.Item?.WeaponComponent != null &&
                                IsMeleeWeapon(item.WeaponCopy.Item.WeaponComponent) &&
                                item.GameEntity.GlobalPosition.DistanceSquared(agent.Position) <= SEARCH_RADIUS * SEARCH_RADIUS)
                            .OrderBy(item => item.GameEntity.GlobalPosition.DistanceSquared(agent.Position))
                            .FirstOrDefault();

                        if (nearbyWeapons != null)
                        {
                            float distanceSquared = agent.Position.DistanceSquared(nearbyWeapons.GameEntity.GlobalPosition);

                            if (distanceSquared <= PICKUP_DISTANCE * PICKUP_DISTANCE)
                            {
                                // 尝试拾取武器
                                try
                                {
                                    // 找到一个空的武器槽
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
                                        // 播放拾取动画
                                        agent.SetActionChannel(0, ActionIndexCache.Create("act_pickup_down_begin"), ignorePriority: false, 0UL);
                                        // 执行拾取武器
                                        bool removeWeapon;
                                        agent.OnItemPickup(nearbyWeapons, emptySlot, out removeWeapon);
                                        _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime;
                                        DebugLog($"Agent {agent.Name} 拾取武器到槽位 {emptySlot}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    DebugLog($"拾取武器时出错: {ex.Message}");
                                }
                            }
                            else
                            {
                                // 移动到武器位置
                                agent.AIMoveToGameObjectEnable(nearbyWeapons, null, Agent.AIScriptedFrameFlags.NoAttack);
                                DebugLog($"Agent {agent.Name} 移动到武器位置，距离: {Math.Sqrt(distanceSquared)}");
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
            DebugLog("=== 行为移除 ===");
            _lastPickupAttemptTime.Clear();
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
        }
    }
} 