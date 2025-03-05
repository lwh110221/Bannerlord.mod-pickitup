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
        private const float AGENT_CHECK_INTERVAL = 1.0f; // 检查间隔
        private const float SPATIAL_CELL_SIZE = 5f;    // 空间分区大小
        private float _lastWeaponCacheUpdateTime;
        private readonly List<SpawnedItemEntity> _cachedWeapons = new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY);
        private int _currentAgentIndex;
        
        // 合并多个Dictionary为一个AgentState类
        private class AgentState
        {
            public float NextSearchTime;
            public SpawnedItemEntity TargetWeapon;
            public float LastPickupAttempt;
            public float PickupTimer;
            public (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot) AnimationState;
            public bool NeedsWeaponCheck;
            public float LastStateUpdateTime;
            public float StuckTime;
            
            public void Reset()
            {
                NextSearchTime = 0f;
                TargetWeapon = null;
                LastPickupAttempt = 0f;
                PickupTimer = 0f;
                AnimationState = default;
                NeedsWeaponCheck = true;
                LastStateUpdateTime = 0f;
                StuckTime = 0f;
            }
        }

        private readonly Dictionary<Agent, AgentState> _agentStates = new Dictionary<Agent, AgentState>();
        private readonly Dictionary<(int x, int z), List<SpawnedItemEntity>> _spatialWeaponCache = new Dictionary<(int x, int z), List<SpawnedItemEntity>>();
        private readonly Stack<List<SpawnedItemEntity>> _listPool = new Stack<List<SpawnedItemEntity>>();
        
        // 新增: 用于快速检查是否有掉落武器
        private bool _hasDroppedWeapons;

        private readonly HashSet<SpawnedItemEntity> _previousWeapons = new HashSet<SpawnedItemEntity>();
        private readonly Dictionary<SpawnedItemEntity, (int x, int z)> _weaponCellCache = new Dictionary<SpawnedItemEntity, (int x, int z)>();

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
            return _listPool.Count > 0 ? _listPool.Pop() : new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY);
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
                if (_agentStates.TryGetValue(agent, out var state) && state.AnimationState.WeaponToPickup != null)
                {
                    float currentTime = Mission.Current.CurrentTime;
                    float animationTime = currentTime - state.AnimationState.StartTime;

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

        private void AddWeaponToCache(SpawnedItemEntity weapon)
        {
            try
            {
                if (weapon == null || weapon.GameEntity == null) return;

                var position = weapon.GameEntity.GlobalPosition;
                var cellX = (int)(position.x / SPATIAL_CELL_SIZE);
                var cellZ = (int)(position.z / SPATIAL_CELL_SIZE);
                var cell = (cellX, cellZ);

                if (!_spatialWeaponCache.TryGetValue(cell, out var weaponList))
                {
                    weaponList = GetListFromPool();
                    _spatialWeaponCache[cell] = weaponList;
                }

                if (!weaponList.Contains(weapon))
                {
                    weaponList.Add(weapon);
                    _weaponCellCache[weapon] = cell;
                    _hasDroppedWeapons = true;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"AddWeaponToCache出错: {ex.Message}");
#endif
            }
        }

        private void RemoveWeaponFromCache(SpawnedItemEntity weapon)
        {
            try
            {
                if (weapon == null) return;

                if (_weaponCellCache.TryGetValue(weapon, out var cell))
                {
                    if (_spatialWeaponCache.TryGetValue(cell, out var weaponList))
                    {
                        weaponList.Remove(weapon);
                        
                        // 如果列表为空，返回到对象池
                        if (weaponList.Count == 0)
                        {
                            _spatialWeaponCache.Remove(cell);
                            ReturnListToPool(weaponList);
                        }
                    }
                    _weaponCellCache.Remove(weapon);
                }

                // 检查是否还有任何武器
                _hasDroppedWeapons = _spatialWeaponCache.Any(x => x.Value.Count > 0);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"RemoveWeaponFromCache出错: {ex.Message}");
#endif
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
            var currentWeapons = new HashSet<SpawnedItemEntity>();

            try
            {
                foreach (var missionObject in Mission.Current.MissionObjects)
                {
                    if (missionObject is SpawnedItemEntity spawnedItem)
                    {
                        // 检查武器是否有效
                        if (IsValidWeapon(spawnedItem))
                        {
                            currentWeapons.Add(spawnedItem);
                            
                            // 如果是新武器，添加到缓存
                            if (!_previousWeapons.Contains(spawnedItem))
                            {
                                AddWeaponToCache(spawnedItem);
                            }
                            // 如果武器位置发生变化，更新缓存
                            else if (_weaponCellCache.TryGetValue(spawnedItem, out var oldCell))
                            {
                                var position = spawnedItem.GameEntity.GlobalPosition;
                                var newCellX = (int)(position.x / SPATIAL_CELL_SIZE);
                                var newCellZ = (int)(position.z / SPATIAL_CELL_SIZE);
                                
                                if (oldCell != (newCellX, newCellZ))
                                {
                                    RemoveWeaponFromCache(spawnedItem);
                                    AddWeaponToCache(spawnedItem);
                                }
                            }
                        }
                    }
                }

                // 移除不再存在的武器
                foreach (var oldWeapon in _previousWeapons)
                {
                    if (!currentWeapons.Contains(oldWeapon) || oldWeapon.IsRemoved)
                    {
                        RemoveWeaponFromCache(oldWeapon);
                    }
                }

                // 更新前一帧的武器集合
                _previousWeapons.Clear();
                _previousWeapons.UnionWith(currentWeapons);

#if DEBUG
                DebugHelper.Log("PickUpWeapon", 
                    $"武器缓存更新完成 - 当前武器数量: {currentWeapons.Count}, " +
                    $"空间分区数: {_spatialWeaponCache.Count}, " +
                    $"有掉落武器: {_hasDroppedWeapons}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"UpdateWeaponCache出错: {ex.Message}");
#endif
                // 发生错误时，清理所有缓存并重置状态
                ResetWeaponCache();
            }
        }

        private void ResetWeaponCache()
        {
            try
            {
                _previousWeapons.Clear();
                _weaponCellCache.Clear();
                _hasDroppedWeapons = false;

                foreach (var lists in _spatialWeaponCache.Values)
                {
                    ReturnListToPool(lists);
                }
                _spatialWeaponCache.Clear();
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"ResetWeaponCache出错: {ex.Message}");
#endif
            }
        }

        private SpawnedItemEntity FindNearestWeapon(Agent agent)
        {
            if (agent == null) return null;

            // 首先检查Agent是否可以拾取武器
            if (!CanAgentPickup(agent))
            {
                return null;
            }

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

                // 如果没有掉落武器，跳过大部分处理
                if (!_hasDroppedWeapons)
                {
                    return;
                }

                float currentTime = Mission.Current.CurrentTime;

                // 处理正在进行拾取动画的AI
                foreach (var kvp in _agentStates.ToList())
                {
                    var agent = kvp.Key;
                    var state = kvp.Value;

                    if (state.AnimationState.WeaponToPickup != null)
                    {
                        if (!IsAgentValid(agent))
                        {
                            ResetAgentPickupState(agent);
                            continue;
                        }

                        float currentAnimationTime = currentTime - state.AnimationState.StartTime;
                        if (currentAnimationTime <= PICKUP_ANIMATION_DURATION)
                        {
                            try
                            {
                                var weaponToPickup = state.AnimationState.WeaponToPickup;
                                if (weaponToPickup == null || weaponToPickup.IsRemoved)
                                {
                                    ResetAgentPickupState(agent);
                                    continue;
                                }

                                if (currentAnimationTime >= PICKUP_ANIMATION_DURATION * 0.7f)
                                {
                                    bool removeWeapon;
                                    agent.OnItemPickup(weaponToPickup, state.AnimationState.TargetSlot, out removeWeapon);
                                    ResetAgentPickupState(agent);
                                }
                            }
                            catch (Exception)
                            {
                                ResetAgentPickupState(agent);
                            }
                        }
                        else
                        {
                            ResetAgentPickupState(agent);
                        }
                    }
                }

                // 获取需要处理的AI
                var agentsNeedingWeapons = Mission.Current.Agents
                    .Where(a => NeedsWeaponCheck(a, currentTime))
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

                    if (!_agentStates.TryGetValue(agent, out var state))
                    {
                        state = new AgentState();
                        _agentStates[agent] = state;
                    }

                    if (currentTime >= state.NextSearchTime)
                    {
                        if (state.TargetWeapon == null)
                        {
                            SpawnedItemEntity nearestWeapon = FindNearestWeapon(agent);
                            if (nearestWeapon != null)
                            {
                                state.TargetWeapon = nearestWeapon;
                                MoveToWeapon(agent, nearestWeapon);
                            }
                        }
                        else if (state.TargetWeapon != null)
                        {
                            if (state.TargetWeapon.IsRemoved)
                            {
                                SpawnedItemEntity newWeapon = FindNearestWeapon(agent);
                                if (newWeapon != null)
                                {
                                    state.TargetWeapon = newWeapon;
                                    MoveToWeapon(agent, newWeapon);
                                }
                                else
                                {
                                    ResetAgentPickupState(agent);
                                }
                            }
                            else
                            {
                                float distanceSq = agent.Position.DistanceSquared(state.TargetWeapon.GameEntity.GlobalPosition);
                                if (distanceSq > 1f && agent.MovementVelocity.Length < 0.1f)
                                {
                                    MoveToWeapon(agent, state.TargetWeapon);
                                }
                                else if (agent.CanReachAndUseObject(state.TargetWeapon, distanceSq))
                                {
                                    TryPickupWeapon(agent);
                                }
                            }
                        }

                        state.NextSearchTime = currentTime + 0.2f;
                    }

                    processedCount++;
                }

                _currentAgentIndex = agentsNeedingWeapons.Count > 0 ? 
                    (_currentAgentIndex + processedCount) % agentsNeedingWeapons.Count : 0;
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
            if (agent == null || weapon == null || agent.IsUsingGameObject) return;

            try
            {
                if ((Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle) && !agent.IsUsingGameObject)
                {
                    // 攻城战使用优化后的移动方法
                    agent.DisableScriptedMovement();
                    agent.ClearTargetFrame();
                    
                    // 直接使用已缓存的位置信息
                    var targetPosition = weapon.GameEntity.GlobalPosition;
                    
                    // Agent索引进行位置偏移，避免AI堆积
                    float offsetX = (agent.Index % 3 - 1) * 0.3f;
                    float offsetZ = (agent.Index / 3 % 3 - 1) * 0.3f;
                    targetPosition.x += offsetX;
                    targetPosition.z += offsetZ;
                    
                    WorldPosition worldPos = new(Mission.Current.Scene, targetPosition);
                    agent.SetScriptedPosition(ref worldPos, false, Agent.AIScriptedFrameFlags.None);
#if DEBUG
                    if (_weaponCellCache.TryGetValue(weapon, out var cell))
                    {
                        DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 在攻城战中移动到武器 Cell:({cell.x},{cell.z})");
                    }
#endif
                }
                else if (!agent.IsUsingGameObject)
                {
                    agent.AIMoveToGameObjectEnable(weapon, null, Agent.AIScriptedFrameFlags.None);
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 在非攻城战中移动到武器");
#endif
                }
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
                if (!IsAgentValid(agent) || agent.IsUsingGameObject)
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
                if (_agentStates.TryGetValue(agent, out var state) && state.AnimationState.WeaponToPickup != null)
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

                                // 创建新的状态或更新现有状态
                                if (!_agentStates.TryGetValue(agent, out state))
                                {
                                    state = new AgentState();
                                    _agentStates[agent] = state;
                                }

                                state.AnimationState = (Mission.Current.CurrentTime, spawnedItem, targetSlot);
                                state.LastPickupAttempt = Mission.Current.CurrentTime;
                                state.PickupTimer = Mission.Current.CurrentTime;
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
                if (_agentStates.TryGetValue(agent, out var state))
                {
                    state.Reset();
                    // 确保下一次立即检查
                    state.NextSearchTime = 0f;
                    state.LastStateUpdateTime = 0f;
                    state.NeedsWeaponCheck = true;
                }

                // 完全重置AI的移动状态
                if (!agent.IsUsingGameObject)
                {
                    agent.DisableScriptedMovement();
                    agent.AIMoveToGameObjectDisable();
                    
                    agent.SetActionChannel(0, ActionIndexCache.act_none);
                }

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
                _agentStates.Remove(affectedAgent);
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
                ResetWeaponCache();
                _cachedWeapons.Clear();
                _agentStates.Clear();

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

        // 新增: 快速检查Agent是否需要武器
        private bool NeedsWeaponCheck(Agent agent, float currentTime)
        {
            if (!IsAgentValid(agent) || !agent.IsAIControlled || agent.IsMount)
            {
                return false;
            }

            if (!_agentStates.TryGetValue(agent, out var state))
            {
                state = new AgentState();
                _agentStates[agent] = state;
                return CanAgentPickup(agent);
            }

            // 如果正在执行拾取动画，不需要检查
            if (state.AnimationState.WeaponToPickup != null)
            {
                return false;
            }

            // 如果距离上次更新时间太短，使用缓存的结果
            if (currentTime - state.LastStateUpdateTime < AGENT_CHECK_INTERVAL)
            {
                return state.NeedsWeaponCheck;
            }

            // 更新检查时间
            state.LastStateUpdateTime = currentTime;

            // 获取武器状态
            var (hasShield, hasSingleHandedWeapon, hasTwoHandedWeapon) = GetAgentWeaponStatus(agent);
            
            // 如果完全没有武器，立即设置需要检查
            if (!hasShield && !hasSingleHandedWeapon && !hasTwoHandedWeapon)
            {
                state.NeedsWeaponCheck = true;
                state.NextSearchTime = 0f;
                return true;
            }

            // 其他情况按正常逻辑检查
            state.NeedsWeaponCheck = CanAgentPickup(agent);
            
            return state.NeedsWeaponCheck;
        }
    }
}