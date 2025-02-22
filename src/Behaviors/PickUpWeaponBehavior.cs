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
        //private const float PICKUP_COOLDOWN = 1f; //冷却时间 秒
        //private const float SEARCH_RADIUS = 10f; //搜索范围 米
        //private const float PICKUP_DELAY = 2f; // 拾取延迟 秒
        private const float PICKUP_ANIMATION_DURATION = 0.7f; // 拾取动画持续时间 秒
        private const float AGENT_RADIUS = 0.4f; // AI的碰撞半径
        private const float PATH_CALCULATION_INTERVAL = 0.2f; // 路径计算间隔（秒）
        private const float PICKUP_DISTANCE = 1f; //拾取距离 米

        // 清理相关的计时器
       // private const float CLEANUP_INTERVAL = 30f; // 每30秒进行一次清理
       // private const float CACHE_LIFETIME = 60f;   // 缓存数据保留60秒

        private readonly MemoryManager _memoryManager;

        // 从Settings获取配置值
        private float PickupDelay => PickItUp.Settings.Settings.Instance?.PickupDelay ?? 1.5f; // 懵逼时间
        private float SearchRadius => PickItUp.Settings.Settings.Instance?.SearchRadius ?? 5.0f; // 搜索范围
        private float PickupCooldown => PickItUp.Settings.Settings.Instance?.PickupCooldown ?? 1.0f; // 拾取冷却
        private float CleanupInterval => PickItUp.Settings.Settings.Instance?.CleanupInterval ?? 30.0f; // 清理间隔
        private float CacheLifetime => PickItUp.Settings.Settings.Instance?.CacheLifetime ?? 60.0f; // 缓存生命周期

#if DEBUG
        private readonly string _logFilePath;
#endif

        public PickUpWeaponBehavior()
        {
            _memoryManager = new MemoryManager(
                _lastPickupAttemptTime,
                _pickupAnimationTracker,
                _lastPathCalculationTime,
                DebugLog
            );

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
            _lastPickupAttemptTime[agent] = Mission.Current.CurrentTime + PickupDelay;
            
            DebugLog($"Agent {agent.Name} 丢弃武器从槽 {slot}, IsEmpty: {droppedWeapon.IsEmpty}");
            
            try
            {
                // 通知周围的AI检查是否需要拾取武器
                var nearbyAgents = new MBList<Agent>();
                Mission.Current.GetNearbyAgents(agent.Position.AsVec2, SearchRadius, nearbyAgents);
                
                foreach(var nearbyAgent in nearbyAgents.Where(a => a != agent && CanAgentPickup(a)))
                {
                    DebugLog($"通知附近的Agent {nearbyAgent.Name} 检查是否需要拾取武器");
                    _lastPickupAttemptTime[nearbyAgent] = Mission.Current.CurrentTime + PickupDelay; // 重置附近AI的冷却时间
                }
                
                nearbyAgents.Clear(); // 清理列表
            }
            catch (Exception ex)
            {
                DebugLog($"OnAgentDropWeapon出错: {ex.Message}");
            }
        }

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
                            
                            // 检查是否是可用作近战武器的武器
                            if (CanBeUsedAsMeleeWeapon(weaponClass))
                            {
                                // 如果是投掷武器，检查数量
                                bool isThrowingWeapon = weaponClass == WeaponClass.ThrowingAxe ||
                                                      weaponClass == WeaponClass.ThrowingKnife ||
                                                      weaponClass == WeaponClass.Javelin;

                                if (!isThrowingWeapon || (isThrowingWeapon && equipment.Amount > 0))
                                {
                                    hasUsableMeleeWeapon = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                // 如果已经有可用的近战武器，就不需要捡武器
                if (hasUsableMeleeWeapon)
                {
                    return false;
                }
                // 检查冷却时间和拾取延迟
                if (_lastPickupAttemptTime.TryGetValue(agent, out float lastAttempt))
                {
                    if (Mission.Current.CurrentTime < lastAttempt || Mission.Current.CurrentTime - lastAttempt < PickupCooldown)
                    {
                        return false;
                    }
                }
                
                if (CCmodAct.IsExecutingCinematicAction(agent))
                {
                    DebugLog($"Agent {agent.Name} 需要捡武器但正在执行Cinematic Combat动作，暂时禁止拾取");
                    return false;
                }

                return true;
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

        private SpawnedItemEntity FindNearestWeapon(Agent agent)
        {
            if (agent == null) return null;

            _nearbyWeapons.Clear();
            
            // 获取附近所有MissionObjects，进行初步筛选
            foreach (var missionObject in Mission.Current.MissionObjects)
            {
                var spawnedItem = missionObject as SpawnedItemEntity;
                if (spawnedItem == null || 
                    spawnedItem.GameEntity == null || 
                    spawnedItem.WeaponCopy.Item?.WeaponComponent == null) continue;

                // 排除弓箭类武器和盾牌
                var weaponClass = spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass;
                if (weaponClass == WeaponClass.Arrow ||   // 箭矢
                    weaponClass == WeaponClass.Bolt ||    // 弩箭
                    weaponClass == WeaponClass.Stone ||   // 投石
                    weaponClass == WeaponClass.Bow ||     // 弓
                    weaponClass == WeaponClass.Crossbow || // 弩
                    CosmeticsManagerHelper.IsWeaponClassShield(weaponClass)) // 盾牌
                {
                    continue;
                }

                // 使用直线距离进行粗略筛选
                var distance = agent.Position.Distance(spawnedItem.GameEntity.GlobalPosition);
                if (distance <= SearchRadius * 1.5f) // 使用1.5倍的搜索范围进行初步筛选
                {
                    // 只添加近战武器
                    if (IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent))
                    {
                        _nearbyWeapons.Add(spawnedItem);
                    }
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
                    if (pathDistance > SearchRadius) continue;

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
            try
            {
                if (spawnedItem?.GameEntity == null || 
                    !spawnedItem.GameEntity.IsVisibleIncludeParents() || 
                    spawnedItem.WeaponCopy.Item?.WeaponComponent == null)
                    return false;

                var weaponClass = spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass;
                
                // 排除所有远程武器、弹药和盾牌
                if (weaponClass == WeaponClass.Arrow ||   // 箭矢
                    weaponClass == WeaponClass.Bolt ||    // 弩箭
                    weaponClass == WeaponClass.Stone ||   // 投石
                    weaponClass == WeaponClass.Bow ||     // 弓
                    weaponClass == WeaponClass.Crossbow || // 弩
                    CosmeticsManagerHelper.IsWeaponClassShield(weaponClass)) // 盾牌
                {
                    return false;
                }

                // 检查是否是近战武器
                return IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent);
            }
            catch (Exception ex)
            {
                DebugLog($"检查武器有效性时出错: {ex.Message}");
                return false;
            }
        }

        // 重置AI状态
        private void ResetAgentPickupState(Agent agent, string reason = null)
        {
            if (agent == null) return;

            try
            {
                // 先保存当前时间
                float currentTime = Mission.Current.CurrentTime;
                
                // 清理数据
                _memoryManager.CleanupAgentData(agent);
                
                // 重新设置时间戳
                _lastPickupAttemptTime[agent] = currentTime;
                
                // 确保动画完整播放
          //    agent.SetActionChannel(0, ActionIndexCache.Create("act_pickup_down_end"), ignorePriority: true, 0UL);
                
                // 重置AI的其他状态
                agent.ClearTargetFrame();
                agent.InvalidateTargetAgent();
                agent.InvalidateAIWeaponSelections();
                agent.ResetLookAgent();
                agent.ResetGuard();
                
                if (!string.IsNullOrEmpty(reason))
                {
                    DebugLog($"重置Agent {agent.Name} 的状态: {reason}");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"重置Agent状态时出错: {ex.Message}");
            }
        }

        private void TryPickupWeapon(Agent agent, SpawnedItemEntity weaponToPickup)
        {
            try
            {
                // 空值和状态检查
                if (agent == null || !agent.IsActive() || agent.Health <= 0 || 
                    weaponToPickup == null || weaponToPickup.GameEntity == null)
                {
                    if (agent != null)
                    {
                        ResetAgentPickupState(agent, "武器或agent状态无效");
                    }
                    return;
                }

                // 检查武器的有效性
                if (!IsValidWeapon(weaponToPickup))
                {
                    DebugLog($"Agent {agent.Name} 尝试拾取无效武器");
                    ResetAgentPickupState(agent, "武器无效");
                    return;
                }

                // 确保WeaponCopy是有效的
                if (weaponToPickup.WeaponCopy.IsEmpty || weaponToPickup.WeaponCopy.Item == null)
                {
                    DebugLog($"Agent {agent.Name} 的目标武器WeaponCopy无效");
                    ResetAgentPickupState(agent, "WeaponCopy无效");
                    return;
                }

                // 骑马状态特殊处理
                if (agent.HasMount)
                {
                    // 检查是否处于适合拾取的状态
                    if (agent.GetCurrentVelocity().Length > 5f)
                    {
                        DebugLog($"Agent {agent.Name} 骑马速度过快，不适合拾取");
                        ResetAgentPickupState(agent, "骑马速度过快");
                        return;
                    }
                }

                // 使用try-catch包装原生方法调用
                EquipmentIndex targetSlot;
                try
                {
                    targetSlot = MissionEquipment.SelectWeaponPickUpSlot(
                        agent, 
                        weaponToPickup.WeaponCopy, 
                        false
                    );
                }
                catch (Exception ex)
                {
                    DebugLog($"SelectWeaponPickUpSlot失败: {ex.Message}");
                    ResetAgentPickupState(agent, "选择槽位失败");
                    return;
                }

                if (targetSlot != EquipmentIndex.None)
                {
                    // 根据骑马状态选择适当的动画
                    if (agent.HasMount)
                    {
                        agent.SetActionChannel(0, ActionIndexCache.Create("act_pickup_from_right_down_horseback_begin"), ignorePriority: true, 0UL);
                    }
                    else
                    {
                        agent.SetActionChannel(0, ActionIndexCache.Create("act_pickup_down_begin"), ignorePriority: true, 0UL);
                    }
                    _pickupAnimationTracker[agent] = (Mission.Current.CurrentTime, weaponToPickup, targetSlot);
                    DebugLog($"Agent {agent.Name} 开始拾取动画，目标槽位: {targetSlot}，骑马状态: {agent.HasMount}");
                }
                else
                {
                    ResetAgentPickupState(agent, "没有合适的武器槽位");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"尝试拾取武器时出错: {ex.Message}");
                if (agent != null)
                {
                    ResetAgentPickupState(agent, "拾取尝试失败");
                }
            }
        }

        public override void OnMissionTick(float dt)
        {
            try
            {
                // 使用内存管理器进行清理
                _memoryManager.Update(Mission.Current.CurrentTime);

                base.OnMissionTick(dt);

                if (Mission.Current == null || Mission.Current.Scene == null)
                {
                    DebugLog("Mission或Scene为空，跳过处理");
                    return;
                }

                // 处理正在进行拾取动画的AI
                foreach (var kvp in _pickupAnimationTracker.ToList())
                {
                    var agent = kvp.Key;
                    if (agent == null || !agent.IsActive())
                    {
                        _pickupAnimationTracker.Remove(kvp.Key);
                        continue;
                    }

                    var (startTime, weaponToPickup, targetSlot) = kvp.Value;
                    float currentAnimationTime = Mission.Current.CurrentTime - startTime;

                    // 只在拾取动画执行期间检查
                    if (currentAnimationTime <= PICKUP_ANIMATION_DURATION)
                    {
                        try
                        {
                            // 检查武器是否还存在且可拾取
                            if (weaponToPickup == null || 
                                weaponToPickup.GameEntity == null || 
                                !weaponToPickup.GameEntity.IsVisibleIncludeParents() || 
                                !IsValidWeapon(weaponToPickup))
                            {
                                DebugLog($"Agent {agent.Name} 的目标武器已不存在或不可用");
                                EnhancedResetAgentState(agent);
                                continue;
                            }

                            // 在动画进行到一定程度时尝试拾取
                            if (currentAnimationTime >= PICKUP_ANIMATION_DURATION * 0.7f && 
                                currentAnimationTime < PICKUP_ANIMATION_DURATION * 0.9f)
                            {
                                try
                                {
                                    if (agent.HasMount && agent.GetCurrentVelocity().Length > 5f)
                                    {
                                        DebugLog($"Agent {agent.Name} 骑马速度过快，取消拾取");
                                        EnhancedResetAgentState(agent);
                                        continue;
                                    }

                                    bool removeWeapon;
                                    agent.OnItemPickup(weaponToPickup, targetSlot, out removeWeapon);
                                    DebugLog($"Agent {agent.Name} 完成拾取武器到槽位 {targetSlot}");
                                    
                                    // 等待动画完全结束再重置状态
                                    if (currentAnimationTime >= PICKUP_ANIMATION_DURATION)
                                    {
                                        // 在重置状态前检查武器是否成功拾取
                                        if (agent.Equipment[targetSlot].IsEmpty)
                                        {
                                            DebugLog($"Agent {agent.Name} 拾取武器失败，目标槽位为空");
                                        }
                                        else
                                        {
                                            DebugLog($"Agent {agent.Name} 成功拾取武器到槽位 {targetSlot}");
                                        }
                                        
                                        EnhancedResetAgentState(agent);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    DebugLog($"执行拾取时出错: {ex.Message}");
                                    EnhancedResetAgentState(agent);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"处理拾取动画时出错: {ex.Message}");
                            EnhancedResetAgentState(agent);
                        }
                    }
                    else
                    {
                        // 如果超过动画时间还没完成，强制重置状态
                        EnhancedResetAgentState(agent);
                    }
                }

                // 处理其他AI的拾取逻辑
                foreach (var agent in Mission.Current.Agents.Where(a => a != null && a.IsActive()))
                {
                    try
                    {
                        if (!CanAgentPickup(agent) || _pickupAnimationTracker.ContainsKey(agent))
                            continue;

                        // 骑马状态下的额外检查
                        if (agent.HasMount && agent.GetCurrentVelocity().Length > 5f)
                            continue;

                        // 检查路径计算间隔
                        if (_lastPathCalculationTime.TryGetValue(agent, out float lastCalcTime) &&
                            Mission.Current.CurrentTime - lastCalcTime < PATH_CALCULATION_INTERVAL)
                        {
                            continue;
                        }

                        var nearbyWeapon = FindNearestWeapon(agent);
                        if (nearbyWeapon != null && IsValidWeapon(nearbyWeapon))  // 添加额外的有效性检查
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
                                _lastPathCalculationTime[agent] = Mission.Current.CurrentTime;

                                if (pathDistance <= PICKUP_DISTANCE)
                                {
                                    TryPickupWeapon(agent, nearbyWeapon);
                                }
                                else if (pathDistance <= SearchRadius)
                                {
                                    agent.AIMoveToGameObjectEnable(nearbyWeapon, null, Agent.AIScriptedFrameFlags.NoAttack);
                                    DebugLog($"Agent {agent.Name} 移动到武器位置，路径距离: {pathDistance}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"处理Agent {agent?.Name} 时出错: {ex.Message}");
                        if (agent != null)
                        {
                            EnhancedResetAgentState(agent);
                        }
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
            try
            {
                _memoryManager.CleanupAll();
                _nearbyWeapons.Clear();
                base.OnRemoveBehavior();
                DebugLog("=== 行为移除 ===");
            }
            catch (Exception ex)
            {
                DebugLog($"OnRemoveBehavior出错: {ex.Message}");
            }
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
            _lastPathCalculationTime.Remove(affectedAgent);
        }

        private void EnhancedResetAgentState(Agent agent)
        {
            if (agent == null) return;

            try
            {
                // 重置基本状态
                ResetAgentPickupState(agent);
                
                // 强制重置AI目标
                agent.ClearTargetFrame();
                agent.InvalidateTargetAgent();
                agent.ResetLookAgent();
                agent.ResetGuard();
                
                // 重置AI脚本状态
                agent.DisableScriptedMovement();
                agent.SetScriptedFlags(agent.GetScriptedFlags() & ~Agent.AIScriptedFrameFlags.NoAttack);
                
                // 重置AI行为
                agent.StopUsingGameObject();
                
                // 强制重新计算AI行为
                agent.InvalidateAIWeaponSelections();
                agent.InvalidateTargetAgent();
                
                DebugLog($"Agent {agent.Name} 状态已完全重置");
            }
            catch (Exception ex)
            {
                DebugLog($"重置Agent状态时出错: {ex.Message}");
            }
        }
    }
} 