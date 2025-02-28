using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;

namespace PickItUp.Behaviors
{
    public class PickUpWeaponBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float SearchRadius => Settings.Settings.Instance?.SearchRadius ?? 5.0f;        //搜索半径

        private const float PICKUP_ANIMATION_DURATION = 0.7f; // 拾取动画持续时间
        private const int MAX_AGENTS_PER_TICK = 8;  // 每tick处理的最大AI数量
        private const float WEAPON_CACHE_UPDATE_INTERVAL = 0.5f; // 武器缓存更新间隔
        private const int INITIAL_LIST_CAPACITY = 32;

        private float _lastWeaponCacheUpdateTime;
        private readonly List<SpawnedItemEntity> _cachedWeapons = new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY);
        private int _currentAgentIndex;
        private readonly Dictionary<Agent, float> _nextSearchTime = new Dictionary<Agent, float>();

        private readonly Dictionary<Agent, SpawnedItemEntity> _agentTargetWeapons = new Dictionary<Agent, SpawnedItemEntity>();//目标武器
        private readonly Dictionary<Agent, float> _agentPickupTimers = new Dictionary<Agent, float>();//拾取计时器
        private readonly Dictionary<Agent, float> _agentLastPickupAttempts = new Dictionary<Agent, float>();//上次拾取尝试时间
        private readonly Dictionary<Agent, (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot)> _pickupAnimationTracker = new();

        private const float SPATIAL_CELL_SIZE = 15f;                                                     //空间分区大小
        private readonly Dictionary<(int x, int z), List<SpawnedItemEntity>> _spatialWeaponCache = new Dictionary<(int x, int z), List<SpawnedItemEntity>>();
        private readonly Stack<List<SpawnedItemEntity>> _listPool = new Stack<List<SpawnedItemEntity>>();

        public PickUpWeaponBehavior()
        {
            // 预先创建一些List对象放入对象池
            for (int i = 0; i < 10; i++)
            {
                _listPool.Push(new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY));
            }
#if DEBUG
            DebugHelper.Log("PickUpWeapon", "=== PickItUp Mod Started ===");
#endif
        }

        private List<SpawnedItemEntity> GetListFromPool()
        {
            if (_listPool.Count > 0)
            {
                return _listPool.Pop();
            }
            return new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY);
        }

        private void ReturnListToPool(List<SpawnedItemEntity> list)
        {
            list.Clear();
            _listPool.Push(list);
        }

        // 辅助方法：检查Agent是否有效
        private bool IsAgentValid(Agent agent)
        {
            return agent != null && agent.IsActive() && agent.Health > 0;
        }

        // 辅助方法：检查武器是否有效
        private bool IsValidWeapon(SpawnedItemEntity spawnedItem)
        {
            try
            {
                if (spawnedItem?.GameEntity == null ||
                    !spawnedItem.GameEntity.IsVisibleIncludeParents() ||
                    spawnedItem.WeaponCopy.IsEmpty ||
                    spawnedItem.WeaponCopy.Item?.WeaponComponent == null)
                    return false;

                var weaponClass = spawnedItem.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass;

                // 如果是投掷武器，检查是否为空袋子
                bool isThrowingWeapon = weaponClass == WeaponClass.ThrowingAxe ||
                                      weaponClass == WeaponClass.ThrowingKnife ||
                                      weaponClass == WeaponClass.Javelin;

                if (isThrowingWeapon && spawnedItem.WeaponCopy.Amount <= 0)
                {
                    return false;
                }

                var settings = Settings.Settings.Instance;
                
                // 检查是否为盾牌，且是否有任何近战武器被启用
                if (weaponClass == WeaponClass.SmallShield || weaponClass == WeaponClass.LargeShield)
                {
                    // 如果没有任何单手武器被启用，就不允许拾取盾牌
                    return settings.PickupOneHandedSword || 
                           settings.PickupOneHandedAxe || 
                           settings.PickupMace || 
                           settings.PickupOneHandedPolearm || 
                           settings.PickupDagger;
                }

                return IsMeleeWeapon(spawnedItem.WeaponCopy.Item.WeaponComponent);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"检查武器有效性时出错: {ex.Message}");
#endif
                return false;
            }
        }

        private bool CanBeUsedAsMeleeWeapon(WeaponClass weaponClass)
        {
            // 排除所有远程武器和弹药
            if (weaponClass == WeaponClass.Arrow ||
                weaponClass == WeaponClass.Bolt ||
                weaponClass == WeaponClass.Stone ||
                weaponClass == WeaponClass.Bow ||
                weaponClass == WeaponClass.Crossbow)
            {
                return false;
            }

            var settings = Settings.Settings.Instance;
            
            // 根据设置检查每种武器类型
            switch (weaponClass)
            {
                case WeaponClass.OneHandedSword:
                    return settings.PickupOneHandedSword;
                case WeaponClass.TwoHandedSword:
                    return settings.PickupTwoHandedSword;
                case WeaponClass.OneHandedAxe:
                    return settings.PickupOneHandedAxe;
                case WeaponClass.TwoHandedAxe:
                    return settings.PickupTwoHandedAxe;
                case WeaponClass.Mace:
                    return settings.PickupMace;
                case WeaponClass.TwoHandedMace:
                    return settings.PickupTwoHandedMace;
                case WeaponClass.OneHandedPolearm:
                    return settings.PickupOneHandedPolearm;
                case WeaponClass.TwoHandedPolearm:
                    return settings.PickupTwoHandedPolearm;
                case WeaponClass.Dagger:
                    return settings.PickupDagger;
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Javelin:
                    return settings.PickupThrowingWeapons;
                case WeaponClass.SmallShield:
                case WeaponClass.LargeShield:
                    return true; // 允许拾取盾牌
                default:
                    return false;
            }
        }

        private bool IsMeleeWeapon(WeaponComponent weaponComponent)
        {
            if (weaponComponent?.PrimaryWeapon == null) return false;
            return CanBeUsedAsMeleeWeapon(weaponComponent.PrimaryWeapon.WeaponClass);
        }

        private bool IsRegularTroop(Agent agent)
        {
            if (agent?.Character == null) return false;
            return agent.Character.IsSoldier || agent.Character.IsHero;
        }

        private void UpdateWeaponStatus(WeaponClass weaponClass, ref bool hasShield, ref bool hasSingleHandedWeapon, ref bool hasTwoHandedWeapon)
        {
            if (IsShield(weaponClass))
            {
                hasShield = true;
            }
            else if (IsSingleHandedWeapon(weaponClass))
            {
                hasSingleHandedWeapon = true;
            }
            else if (weaponClass == WeaponClass.TwoHandedSword || 
                     weaponClass == WeaponClass.TwoHandedAxe || 
                     weaponClass == WeaponClass.TwoHandedMace || 
                     weaponClass == WeaponClass.TwoHandedPolearm)
            {
                hasTwoHandedWeapon = true;
            }
        }

        private (bool hasShield, bool hasSingleHandedWeapon, bool hasTwoHandedWeapon) GetAgentWeaponStatus(Agent agent)
        {
            bool hasShield = false;
            bool hasSingleHandedWeapon = false;
            bool hasTwoHandedWeapon = false;

            if (agent?.Equipment != null)
            {
                // 检查当前装备
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    var equipment = agent.Equipment[i];
                    if (!equipment.IsEmpty)
                    {
                        var item = equipment.Item;
                        if (item?.WeaponComponent != null)
                        {
                            var weaponClass = item.WeaponComponent.PrimaryWeapon.WeaponClass;
                            
                            // 如果是投掷武器，检查数量
                            bool isThrowingWeapon = weaponClass == WeaponClass.ThrowingAxe ||
                                                  weaponClass == WeaponClass.ThrowingKnife ||
                                                  weaponClass == WeaponClass.Javelin;

                            if (!isThrowingWeapon || (isThrowingWeapon && equipment.Amount > 0))
                            {
                                UpdateWeaponStatus(weaponClass, ref hasShield, ref hasSingleHandedWeapon, ref hasTwoHandedWeapon);
                            }
                            
                            // 如果已经找到双手武器，可以提前退出
                            if (hasTwoHandedWeapon)
                            {
                                return (hasShield, hasSingleHandedWeapon, true);
                            }
                        }
                    }
                }
            }

            return (hasShield, hasSingleHandedWeapon, hasTwoHandedWeapon);
        }

        private bool CanAgentPickup(Agent agent)
        {
            try
            {
                if (!IsAgentValid(agent) || !agent.IsAIControlled)
                {
                    return false;
                }

                if (!IsRegularTroop(agent) || agent.IsMount)
                {
                    return false;
                }

                // 检查是否在缴械延迟时间内
                if (Patches.AgentWeaponDropPatch.HasDisarmCooldown(agent))
                {
                    return false;
                }

                // 检查是否正在执行CinematicCombat动作
                if (CCmodAct.IsExecutingCinematicAction(agent))
                {
                    return false;
                }

                // 检查是否正在进行拾取动画
                if (_pickupAnimationTracker.TryGetValue(agent, out var pickupInfo))
                {
                    float currentTime = Mission.Current.CurrentTime;
                    float animationTime = currentTime - pickupInfo.StartTime;

                    // 如果动画还在进行中，不允许新的拾取
                    if (animationTime <= PICKUP_ANIMATION_DURATION)
                    {
                        return false;
                    }
                }

                var (hasShield, hasSingleHandedWeapon, hasTwoHandedWeapon) = GetAgentWeaponStatus(agent);

                // 如果有双手武器，不允许拾取任何武器
                if (hasTwoHandedWeapon)
                {
                    return false;
                }

                // 检查当前装备槽位中是否有任何可用武器
                bool hasAnyUsableWeapon = hasSingleHandedWeapon || hasTwoHandedWeapon;

                // 如果没有任何可用武器，允许拾取
                if (!hasAnyUsableWeapon)
                {
                    return true;
                }

                // 如果只有单手武器且没有盾牌，允许拾取盾牌
                return hasSingleHandedWeapon && !hasShield;
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"检查Agent {agent?.Name} 是否可以拾取时出错: {ex.Message}");
#endif
                return false;
            }
        }

        private void UpdateWeaponCache()
        {
            float currentTime = Mission.Current.CurrentTime;
            if (currentTime - _lastWeaponCacheUpdateTime < WEAPON_CACHE_UPDATE_INTERVAL)
            {
                return;
            }

            _lastWeaponCacheUpdateTime = currentTime;
            _cachedWeapons.Clear();

            // 清理并回收所有List到对象池
            foreach (var lists in _spatialWeaponCache.Values)
            {
                ReturnListToPool(lists);
            }
            _spatialWeaponCache.Clear();

            foreach (var missionObject in Mission.Current.MissionObjects)
            {
                if (missionObject is SpawnedItemEntity spawnedItem && IsValidWeapon(spawnedItem))
                {
                    _cachedWeapons.Add(spawnedItem);

                    var position = spawnedItem.GameEntity.GlobalPosition;
                    var cellX = (int)(position.x / SPATIAL_CELL_SIZE);
                    var cellZ = (int)(position.z / SPATIAL_CELL_SIZE);
                    var cell = (cellX, cellZ);

                    if (!_spatialWeaponCache.TryGetValue(cell, out var weaponList))
                    {
                        weaponList = GetListFromPool();
                        _spatialWeaponCache[cell] = weaponList;
                    }
                    weaponList.Add(spawnedItem);
                }
            }
        }

        private SpawnedItemEntity FindNearestWeapon(Agent agent)
        {
            if (agent == null) return null;

            var agentPosition = agent.Position;
            var cellX = (int)(agentPosition.x / SPATIAL_CELL_SIZE);
            var cellZ = (int)(agentPosition.z / SPATIAL_CELL_SIZE);

            // 搜索周围9个格子
            var nearbyWeapons = new List<SpawnedItemEntity>();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var cell = (cellX + dx, cellZ + dz);
                    if (_spatialWeaponCache.TryGetValue(cell, out var weapons))
                    {
                        nearbyWeapons.AddRange(weapons);
                    }
                }
            }

            // 过滤有效武器
            var validWeapons = nearbyWeapons
                .Where(w => w != null &&
                          !w.IsRemoved &&
                          w.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius &&
                          (!w.IsStuckMissile() || agent.CanReachAndUseObject(w, w.GameEntity.GlobalPosition.DistanceSquared(agentPosition))))
                .ToList();

            if (validWeapons.Count == 0) return null;

            var (hasShield, hasSingleHandedWeapon, hasTwoHandedWeapon) = GetAgentWeaponStatus(agent);

            // 如果有双手武器，不允许拾取任何武器
            if (hasTwoHandedWeapon)
            {
                return null;
            }

            // 如果AI只有盾牌没有武器，找任何非盾牌武器
            if (hasShield && !hasSingleHandedWeapon)
            {
                return validWeapons
                    .Where(w => !IsShield(w.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass))
                    .OrderBy(w => w.GameEntity.GlobalPosition.Distance(agentPosition))
                    .FirstOrDefault();
            }
            // 如果AI有单手武器且没有盾牌，找盾牌
            else if (hasSingleHandedWeapon && !hasShield)
            {
                return validWeapons
                    .Where(w => IsShield(w.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass))
                    .OrderBy(w => w.GameEntity.GlobalPosition.Distance(agentPosition))
                    .FirstOrDefault();
            }
            // 如果AI没有武器，找任何非盾牌武器
            else if (!hasSingleHandedWeapon && !hasShield)
            {
                return validWeapons
                    .Where(w => !IsShield(w.WeaponCopy.Item.WeaponComponent.PrimaryWeapon.WeaponClass))
                    .OrderBy(w => w.GameEntity.GlobalPosition.Distance(agentPosition))
                    .FirstOrDefault();
            }

            return null;
        }

        public override void OnMissionTick(float dt)
        {
            try
            {
                UpdateWeaponCache();

                // 处理正在进行拾取动画的AI
                foreach (var kvp in _pickupAnimationTracker.ToList())
                {
                    var agent = kvp.Key;
                    if (agent == null || !agent.IsActive())
                    {
                        _pickupAnimationTracker.Remove(agent);
                        continue;
                    }

                    var (startTime, weaponToPickup, targetSlot) = kvp.Value;
                    float currentAnimationTime = Mission.Current.CurrentTime - startTime;

                    if (currentAnimationTime <= PICKUP_ANIMATION_DURATION)
                    {
                        try
                        {
                            if (weaponToPickup == null || weaponToPickup.IsRemoved)
                            {
                                ResetAgentPickupState(agent);
                                continue;
                            }

                            if (currentAnimationTime >= PICKUP_ANIMATION_DURATION * 0.7f)
                            {
                                bool removeWeapon;
                                agent.OnItemPickup(weaponToPickup, targetSlot, out removeWeapon);
#if DEBUG
                                DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 完成拾取武器到槽位 {targetSlot}");
#endif
                                ResetAgentPickupState(agent);
                            }
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            DebugHelper.Log("PickUpWeapon", $"处理拾取动画时出错: {ex.Message}");
#endif
                            ResetAgentPickupState(agent);
                        }
                    }
                    else
                    {
                        ResetAgentPickupState(agent);
                    }
                }

                // 获取所有需要处理的AI
                var agentsNeedingWeapons = Mission.Current.Agents
                    .Where(a => CanAgentPickup(a) && !_pickupAnimationTracker.ContainsKey(a))
                    .ToList();

                if (agentsNeedingWeapons.Count == 0)
                {
                    return;
                }

                int startIndex = _currentAgentIndex;
                int processedCount = 0;

                while (processedCount < MAX_AGENTS_PER_TICK && processedCount < agentsNeedingWeapons.Count)
                {
                    int index = (startIndex + processedCount) % agentsNeedingWeapons.Count;
                    var agent = agentsNeedingWeapons[index];

                    float currentTime = Mission.Current.CurrentTime;
                    if (!_nextSearchTime.TryGetValue(agent, out float nextSearch) || currentTime >= nextSearch)
                    {
                        if (!_agentTargetWeapons.ContainsKey(agent))
                        {
                            SpawnedItemEntity nearestWeapon = FindNearestWeapon(agent);
                            if (nearestWeapon != null)
                            {
                                _agentTargetWeapons[agent] = nearestWeapon;
                                MoveToWeapon(agent, nearestWeapon);
                            }
                        }
                        else if (_agentTargetWeapons.TryGetValue(agent, out SpawnedItemEntity targetWeapon))
                        {
                            if (targetWeapon == null || targetWeapon.IsRemoved)
                            {
                                SpawnedItemEntity newWeapon = FindNearestWeapon(agent);
                                if (newWeapon != null)
                                {
                                    _agentTargetWeapons[agent] = newWeapon;
                                    MoveToWeapon(agent, newWeapon);
                                }
                                else
                                {
                                    ResetAgentPickupState(agent);
                                }
                            }
                            else
                            {
                                float distanceSq = agent.Position.DistanceSquared(targetWeapon.GameEntity.GlobalPosition);
                                if (distanceSq > 1f && agent.MovementVelocity.Length < 0.1f)
                                {
                                    MoveToWeapon(agent, targetWeapon);
                                }
                                else if (agent.CanReachAndUseObject(targetWeapon, distanceSq))
                                {
                                    TryPickupWeapon(agent);
                                }
                            }
                        }

                        _nextSearchTime[agent] = currentTime + 0.2f; // 设置下次搜索时间
                    }

                    processedCount++;
                }

                if (agentsNeedingWeapons.Count > 0)
                {
                    _currentAgentIndex = (_currentAgentIndex + processedCount) % agentsNeedingWeapons.Count;
                }
                else
                {
                    _currentAgentIndex = 0;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"OnMissionTick出错: {ex.Message}");
#endif
            }
        }

        private void MoveToWeapon(Agent agent, SpawnedItemEntity weapon)
        {
            if (agent == null || weapon == null) return;

            try
            {
                // ----备用方法----
                // agent.DisableScriptedMovement();
                // agent.ClearTargetFrame();
                // WorldPosition targetPosition = new(Mission.Current.Scene, weapon.GameEntity.GlobalPosition);
                // agent.SetScriptedPosition(ref targetPosition, false, Agent.AIScriptedFrameFlags.NoAttack);
                // ----备用方法----
                agent.AIMoveToGameObjectEnable(weapon, null, Agent.AIScriptedFrameFlags.None);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"MoveToWeapon出错: {ex.Message}");
#endif
            }
        }

        private void TryPickupWeapon(Agent agent)
        {
            try
            {
                if (!IsAgentValid(agent))
                {
                    return;
                }

                // Equipment是否有效
                if (agent.Equipment == null)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 的Equipment为空");
#endif
                    return;
                }

                // AI正在执行拾取动画,不尝试新的拾取
                if (_pickupAnimationTracker.ContainsKey(agent))
                {
                    return;
                }

                var agentPosition = agent.Position;
                var itemsInRange = Mission.Current.MissionObjects
                    .OfType<SpawnedItemEntity>()
                    .Where(item => item != null &&
                                 !item.IsRemoved &&
                                 !item.IsDeactivated &&
                                 IsValidWeapon(item) &&
                                 item.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius)
                    .OrderBy(x => x.GameEntity.GlobalPosition.Distance(agentPosition))
                    .ToList();

                foreach (var spawnedItem in itemsInRange)
                {
                    try
                    {
                        float distanceSq = agent.Position.DistanceSquared(spawnedItem.GameEntity.GlobalPosition);

                        if (!agent.CanQuickPickUp(spawnedItem) ||
                            !agent.CanReachAndUseObject(spawnedItem, distanceSq))
                        {
                            continue;
                        }

                        // 尝试获取目标槽位
                        EquipmentIndex targetSlot;
                        try
                        {
                            targetSlot = MissionEquipment.SelectWeaponPickUpSlot(agent, spawnedItem.WeaponCopy, spawnedItem.IsStuckMissile());
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            DebugHelper.Log("PickUpWeapon", $"SelectWeaponPickUpSlot出错: {ex.Message}");
#endif
                            continue;
                        }

                        if (targetSlot != EquipmentIndex.None)
                        {
                            try
                            {
                                string animationName = agent.HasMount ?
                                    "act_pickup_from_right_down_horseback_begin" :
                                    "act_pickup_down_begin";

                                agent.SetActionChannel(0, ActionIndexCache.Create(animationName), ignorePriority: true, 0UL);

                                // 记录拾取动画开始时间和目标武器
                                _pickupAnimationTracker[agent] = (Mission.Current.CurrentTime, spawnedItem, targetSlot);
                                _agentLastPickupAttempts[agent] = Mission.Current.CurrentTime;
                                _agentPickupTimers.Remove(agent);
#if DEBUG
                                DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 开始拾取动画，目标槽位: {targetSlot}，骑马状态: {agent.HasMount}");
#endif
                                break;
                            }
                            catch (Exception ex)
                            {
#if DEBUG
                                DebugHelper.Log("PickUpWeapon", $"设置拾取动画时出错: {ex.Message}");
#endif
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        DebugHelper.Log("PickUpWeapon", $"处理单个武器时出错: {ex.Message}");
#endif
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugHelper.Log("PickUpWeapon", $"TryPickupWeapon 出错: {ex.Message}");
            }
        }

        private void ResetAgentPickupState(Agent agent, string reason = null)
        {
            if (agent == null) return;

            try
            {
                _pickupAnimationTracker.Remove(agent);
                _agentTargetWeapons.Remove(agent);
                _nextSearchTime.Remove(agent);
                _agentPickupTimers.Remove(agent);
                _agentLastPickupAttempts.Remove(agent);

                agent.DisableScriptedMovement();
                agent.AIMoveToGameObjectDisable();
                agent.InvalidateAIWeaponSelections();

                if (!string.IsNullOrEmpty(reason))
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"重置Agent {agent.Name} 的状态: {reason}");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"重置Agent状态时出错: {ex.Message}");
#endif
            }
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            if (affectedAgent == null) return;

            try
            {
                _nextSearchTime.Remove(affectedAgent);
                _agentTargetWeapons.Remove(affectedAgent);
                _agentPickupTimers.Remove(affectedAgent);
                _agentLastPickupAttempts.Remove(affectedAgent);
                _pickupAnimationTracker.Remove(affectedAgent);

                base.OnAgentDeleted(affectedAgent);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"清理Agent状态时出错: {ex.Message}");
#endif
            }
        }

        protected override void OnEndMission()
        {
            try
            {
                // 清理所有缓存和对象池
                _cachedWeapons.Clear();
                _nextSearchTime.Clear();
                _agentTargetWeapons.Clear();
                _agentPickupTimers.Clear();
                _agentLastPickupAttempts.Clear();
                _pickupAnimationTracker.Clear();

                // 清理空间分区缓存
                foreach (var lists in _spatialWeaponCache.Values)
                {
                    lists.Clear();
                }
                _spatialWeaponCache.Clear();
                _listPool.Clear();

                base.OnEndMission();
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"清理Mission结束状态时出错: {ex.Message}");
#endif
            }
        }

        private bool IsShield(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.SmallShield || weaponClass == WeaponClass.LargeShield;
        }

        private bool IsSingleHandedWeapon(WeaponClass weaponClass)
        {
            return weaponClass == WeaponClass.OneHandedSword || 
                   weaponClass == WeaponClass.OneHandedAxe || 
                   weaponClass == WeaponClass.Mace || 
                   weaponClass == WeaponClass.OneHandedPolearm || 
                   weaponClass == WeaponClass.Dagger ||
                   weaponClass == WeaponClass.ThrowingAxe ||
                   weaponClass == WeaponClass.ThrowingKnife ||
                   weaponClass == WeaponClass.Javelin;
        }
    }
}