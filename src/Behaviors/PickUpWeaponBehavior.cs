using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using PickItUp.Util;

namespace PickItUp.Behaviors
{
    public class PickUpWeaponBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        #region 常量
        private float SearchRadius => Settings.Settings.Instance?.SearchRadius ?? 5.0f;   
        private const float High =1.2f;
        private const float HIGH_WEAPON_RECHECK_TIME = 4.0f;
        private const float WEAPON_CHASE_TIMEOUT = 15f;
        private const float PICKUP_ANIMATION_DURATION = 0.7f;
        private const int MAX_AGENTS_PER_TICK = 8;
        private const float WEAPON_CACHE_UPDATE_INTERVAL = 0.5f;
        private const int INITIAL_LIST_CAPACITY = 32;
        private const float AGENT_CHECK_INTERVAL = 1.0f;
        private const float SPATIAL_CELL_SIZE = 5f;
        private float _lastWeaponCacheUpdateTime;
        private bool _hasDroppedWeapons;
        private int _currentAgentIndex;
        private readonly Dictionary<SpawnedItemEntity, (float Height, float Time)> _highWeapons = new Dictionary<SpawnedItemEntity, (float, float)>();
        private readonly List<SpawnedItemEntity> _cachedWeapons = new List<SpawnedItemEntity>(INITIAL_LIST_CAPACITY);
        private readonly Dictionary<Agent, AgentState> _agentStates = new Dictionary<Agent, AgentState>();
        private readonly Dictionary<(int x, int z), List<SpawnedItemEntity>> _spatialWeaponCache = new Dictionary<(int x, int z), List<SpawnedItemEntity>>();
        private readonly Stack<List<SpawnedItemEntity>> _listPool = new Stack<List<SpawnedItemEntity>>();
        private readonly MBArrayList<SpawnedItemEntity> _previousWeapons = new MBArrayList<SpawnedItemEntity>();
        private readonly Dictionary<SpawnedItemEntity, (int x, int z)> _weaponCellCache = new Dictionary<SpawnedItemEntity, (int x, int z)>();
        #endregion
        #region 初始化
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
        #endregion
        #region Agent检查
        /// <summary>
        /// Agent状态
        /// </summary>
        private class AgentState
        {
            public float NextSearchTime;
            public SpawnedItemEntity TargetWeapon;
            public float LastPickupAttempt;
            public float PickupTimer;
            public (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot) AnimationState;
            public bool NeedsWeaponCheck;
            public float LastStateUpdateTime;
            public float LastShieldCheckTime;
            public bool CanPickupShield;
            public float WeaponTargetStartTime;

            public void Reset()
            {
                NextSearchTime = 0f;
                TargetWeapon = null;
                LastPickupAttempt = 0f;
                PickupTimer = 0f;
                AnimationState = default;
                NeedsWeaponCheck = true;
                LastStateUpdateTime = 0f;
                LastShieldCheckTime = 0f;
                CanPickupShield = false;
                WeaponTargetStartTime = 0f;
            }
        }
        /// <summary>
        /// 检查Agent是否有效
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <returns>是否有效</returns>
        private bool IsAgentValid(Agent agent)
        {
            return agent != null && agent.IsActive() && agent.Health > 0;
        }
        /// <summary>
        /// 检查Agent是否需要武器
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <param name="currentTime">当前时间</param>
        /// <returns>是否需要武器</returns>
        private bool NeedsWeaponCheck(Agent agent, float currentTime)
        {
            if (!IsAgentValid(agent))
            {
                _agentStates.Remove(agent);
                Patches.AgentWeaponDropPatch.RemoveDisarmCooldown(agent);
                    return false;
                }

            if (!_agentStates.TryGetValue(agent, out var state))
            {
                if (!agent.IsAIControlled || !agent.IsHuman)
                {
                return false;
                }

                // 检查任务模式
                var missionMode = Mission.Current.Mode;
                if (missionMode != MissionMode.Battle &&
                    missionMode != MissionMode.Duel &&
                    missionMode != MissionMode.Tournament)
            {
                return false;
                }

                // 通过预检查后才创建状态
                state = new AgentState();
                _agentStates[agent] = state;
                return CanAgentPickup(agent);
            }

            if (currentTime - state.LastStateUpdateTime < AGENT_CHECK_INTERVAL)
            {
                return state.NeedsWeaponCheck;
            }

            state.LastStateUpdateTime = currentTime;
            state.NeedsWeaponCheck = CanAgentPickup(agent);
            return state.NeedsWeaponCheck;
        }

        /// <summary>
        /// 检查Agent是否可以拾取盾牌
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <returns>是否可以拾取盾牌</returns>
        private bool CanAgentPickupShield(Agent agent)
        {
            try
            {
                if (!WeaponCheck.IsShieldPickupEnabled())
                {
                    return false;
                }

                if (agent.HasShieldCached)
                {
                    return false;
                }

                // 检查主手是否装备单手或投掷武器
                EquipmentIndex mainHandIndex = agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                bool hasOneHandedWeapon = false;

                if (mainHandIndex != EquipmentIndex.None && !agent.Equipment[mainHandIndex].IsEmpty &&
                    agent.Equipment[mainHandIndex].Item?.WeaponComponent != null)
                {
                    var weaponComponent = agent.Equipment[mainHandIndex].Item.WeaponComponent;
                    var primaryWeapon = weaponComponent.PrimaryWeapon;
                    bool isOneHandedMelee = primaryWeapon?.IsOneHanded == true;
                    bool isThrowableMelee = WeaponCheck.IsThrowableMeleeWeapon(primaryWeapon.WeaponClass);
                    
                    hasOneHandedWeapon = isOneHandedMelee || isThrowableMelee;
                }

                if (!hasOneHandedWeapon)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 主手没有单手武器，不能拾取盾牌");
#endif
                    return false;
                }
                
                // 检查是否有空闲装备槽位
                bool hasEmptySlot = false;
                for (int i = 0; i < (int)EquipmentIndex.NumPrimaryWeaponSlots; i++)
                {
                    if (agent.Equipment[i].IsEmpty)
                    {
                        hasEmptySlot = true;
                        break;
                    }
                }
                
                if (!hasEmptySlot)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 没有空闲装备槽位，不能拾取盾牌");
#endif
                    return false;
                }
                // 盾牌需求检查缓存
                if (_agentStates.TryGetValue(agent, out var state))
                {
                    float currentTime = Mission.Current.CurrentTime;
                    if (currentTime - state.LastShieldCheckTime < 1.0f)
                    {
                        return state.CanPickupShield;
                    }
                    state.LastShieldCheckTime = currentTime;
                }
                else
                {
                    state = new AgentState();
                    _agentStates[agent] = state;
                    state.LastShieldCheckTime = Mission.Current.CurrentTime;
                }
                
                bool canPickupAnyShield = false;
                var agentPosition = agent.Position;
                var cellX = (int)(agentPosition.x / SPATIAL_CELL_SIZE);
                var cellZ = (int)(agentPosition.z / SPATIAL_CELL_SIZE);
                
                for (int dx = -1; dx <= 1 && !canPickupAnyShield; dx++)
                {
                    for (int dz = -1; dz <= 1 && !canPickupAnyShield; dz++)
                    {
                        var cell = (cellX + dx, cellZ + dz);
                        if (_spatialWeaponCache.TryGetValue(cell, out var weapons))
                        {
                            foreach (var weapon in weapons)
                            {
                                if (WeaponCheck.IsItemShield(weapon) && !weapon.IsRemoved && agent.CanQuickPickUp(weapon))
                                {
                                    canPickupAnyShield = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                
                state.CanPickupShield = canPickupAnyShield;
                
                if (!canPickupAnyShield)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 没有找到可拾取的盾牌");
#endif
                }
                
                return canPickupAnyShield;
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"检查盾牌拾取条件时出错: {ex.Message}");
#endif
                return false;
            }
        }
        /// <summary>
        /// 检查Agent是否可以拾取武器
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <returns>是否可以拾取武器</returns>
        private bool CanAgentPickup(Agent agent)
        {
            try
            {
                if (!agent.IsAIControlled || !agent.IsHuman)
                {
                    return false;
                }
                var missionMode = Mission.Current.Mode;
                if (missionMode != MissionMode.Battle &&
                    missionMode != MissionMode.Duel &&
                    missionMode != MissionMode.Tournament)
                {
                    return false;
                }

                // 正常武器拾取逻辑：如果有近战武器或长矛，不拾取武器
                bool needsWeapon = !agent.HasMeleeWeaponCached && !agent.HasSpearCached;

                // 检查是否需要盾牌
                bool needsShield = WeaponCheck.IsShieldPickupEnabled() && CanAgentPickupShield(agent);

                // 如果两种都不需要，直接返回false
                if (!needsWeapon && !needsShield)
                {
                    return false;
                }

                // 如果没有近战武器，但在缴械冷却时间内，不允许拾取
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

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion
        #region 武器缓存
        /// <summary>
        /// 添加武器到缓存
        /// </summary>
        /// <param name="weapon">武器</param>
        private void AddWeaponToCache(SpawnedItemEntity weapon)
        {
            try
            {
                if (weapon == null ||
                 weapon.GameEntity == null ||
                   weapon.HasAIMovingTo ||
                   weapon.WeaponCopy.IsAnyAmmo() && weapon.WeaponCopy.Amount <= 0 ||
                   weapon.WeaponCopy.Item.PrimaryWeapon.IsBow||
                   weapon.WeaponCopy.Item.PrimaryWeapon.IsCrossBow||
                   weapon.IsBanner())
                   return;

                // 检查武器是否卡在物体上
                if (weapon.IsStuckMissile())
                {
                    // 获取武器的全局位置
                    var weaponPosition = weapon.GameEntity.GlobalPosition;
                    // 获取地形高度
                    float terrainHeight = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(weaponPosition.x, weaponPosition.y, 0));
                    
                    // 计算武器与地形之间的高度差
                    float heightDifference = weaponPosition.z - terrainHeight;
                    // 如果高度差超过阈值，则添加到高处武器列表
                    if (heightDifference > High)
                    {
#if DEBUG
                        InformationManager.DisplayMessage(new InformationMessage($"高度过高: {heightDifference}米，添加到待检查列表", Colors.Yellow));
#endif
                        _highWeapons[weapon] = (heightDifference, Mission.Current.CurrentTime);
                        return;
                    }
                }
               
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
        /// <summary>
        /// 从缓存中移除武器
        /// </summary>
        /// <param name="weapon">武器</param>
        private void RemoveWeaponFromCache(SpawnedItemEntity weapon)
        {
            try
            {
                if (weapon == null) return;

                // 清理空间分区缓存
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

                // 重要：清理所有相关Agent的目标武器
                foreach (var agentState in _agentStates)
                {
                    if (agentState.Value.TargetWeapon == weapon)
                    {
                        ResetAgentPickupState(agentState.Key, "目标武器已被移除");
                    }
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
        /// <summary>
        /// 更新武器缓存
        /// </summary>
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
                var highWeaponsToRemove = new List<SpawnedItemEntity>();
                foreach (var weapon in _highWeapons.Keys)
                {
                    if (weapon == null || weapon.IsRemoved)
                    {
                        highWeaponsToRemove.Add(weapon);
                    }
                }
                
                foreach (var weapon in highWeaponsToRemove)
                {
                    _highWeapons.Remove(weapon);
                }
                
                foreach (var missionObject in Mission.Current.MissionObjects)
                {
                    if (missionObject is SpawnedItemEntity spawnedItem)
                    {
                        // 检查武器是否有效
                        if (WeaponCheck.IsValidWeapon(spawnedItem))
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
                _previousWeapons.AddRange(currentWeapons);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"UpdateWeaponCache出错: {ex.Message}");
#endif
                ResetWeaponCache();
            }
        }
        #endregion
        #region 寻找移动和拾取
        /// <summary>
        /// 寻找最近的武器或盾牌
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <returns>最近的武器或盾牌</returns>
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

            // 判断是否需要武器和盾牌
            bool needsWeapon = !agent.HasMeleeWeaponCached && !agent.HasSpearCached;
            bool needsShield = WeaponCheck.IsShieldPickupEnabled() && CanAgentPickupShield(agent);

            // 过滤有效武器并按距离排序
            if (needsWeapon)
            {
                // 优先查找武器
                var nearestWeapon = nearbyWeapons
                    .Where(w => w != null &&
                              !w.IsRemoved &&
                              !WeaponCheck.IsItemShield(w) && // 使用WeaponCheck
                              w.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius &&
                              (!agent.HasMount || WeaponCheck.CanUseWeaponOnHorseback(w.WeaponCopy.Item.WeaponComponent))) // 添加骑马限制
                    .OrderBy(w => w.GameEntity.GlobalPosition.Distance(agentPosition))
                    .FirstOrDefault();

                if (nearestWeapon != null)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 需要武器，找到最近武器");
#endif
                    return nearestWeapon;
                }
            }

            if (needsShield)
            {
                // 查找盾牌
                var nearestShield = nearbyWeapons
                    .Where(w => w != null &&
                              !w.IsRemoved &&
                              WeaponCheck.IsItemShield(w) &&
                              w.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius &&
                              agent.CanQuickPickUp(w))
                    .OrderBy(w => w.GameEntity.GlobalPosition.Distance(agentPosition))
                    .FirstOrDefault();

                if (nearestShield != null)
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 需要盾牌，找到最近盾牌");
#endif
                    return nearestShield;
                }
            }

            return null;
        }
        /// <summary>
        /// 移动到武器
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <param name="weapon">武器</param>
        private void MoveToWeapon(Agent agent, SpawnedItemEntity weapon)
        {
            if (agent == null || weapon == null || agent.IsUsingGameObject) return;

            try
            {
                if (_agentStates.TryGetValue(agent, out var state))
                {
                    float currentTime = Mission.Current.CurrentTime;
                    
                    if (state.TargetWeapon != weapon || state.WeaponTargetStartTime == 0f)
                    {
                        state.WeaponTargetStartTime = currentTime;
                        state.TargetWeapon = weapon;
                    }
                    else if (state.WeaponTargetStartTime > 0f && currentTime - state.WeaponTargetStartTime > WEAPON_CHASE_TIMEOUT)
                    {
                        RemoveWeaponFromCache(weapon);
                        ResetAgentPickupState(agent);
#if DEBUG
                        InformationManager.DisplayMessage(new InformationMessage($"武器{weapon.WeaponCopy.Item.Name}无法到达，移除", Colors.Yellow));
#endif
                        return;
                    }
                }
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
        /// <summary>
        /// 尝试拾取武器
        /// </summary>
        /// <param name="agent">Agent</param>
        private void TryPickupWeapon(Agent agent)
        {
            try
            {
                if (agent.IsUsingGameObject)
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

                // 确定Agent当前需要的武器类型
                bool needsWeapon = !agent.HasMeleeWeaponCached && !agent.HasSpearCached;
                bool needsShield = CanAgentPickupShield(agent);

                // 根据需求筛选物品
                var itemsInRange = Mission.Current.MissionObjects
                    .OfType<SpawnedItemEntity>()
                    .Where(item => item != null &&
                                 !item.IsRemoved &&
                                 !item.IsDeactivated &&
                                 WeaponCheck.IsValidWeapon(item) &&
                                 item.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius &&
                                 ((needsShield && WeaponCheck.IsItemShield(item)) ||
                                  (needsWeapon && !WeaponCheck.IsItemShield(item))) &&
                                 (!agent.HasMount || !needsWeapon || WeaponCheck.CanUseWeaponOnHorseback(item.WeaponCopy.Item.WeaponComponent)))
                    .OrderBy(x => x.GameEntity.GlobalPosition.Distance(agentPosition))
                    .ToList();

                foreach (var spawnedItem in itemsInRange)
                {
                    try
                    {
                        // 确保物品类型与需求匹配
                        bool isShield = WeaponCheck.IsItemShield(spawnedItem);
                        if ((isShield && !needsShield) || (!isShield && !needsWeapon))
                        {
                            continue;
                        }

                        // 简化检查，只使用CanQuickPickUp
                        if (!agent.CanQuickPickUp(spawnedItem))
                        {
                            continue;
                        }

                        // 尝试获取目标槽位
                        EquipmentIndex targetSlot;
                        try
                        {
                            targetSlot = MissionEquipment.SelectWeaponPickUpSlot(agent, spawnedItem.WeaponCopy, spawnedItem.IsStuckMissile());
                            if (targetSlot == EquipmentIndex.None)
                            {
                                continue;
                            }
                            if (!agent.Equipment[targetSlot].IsEmpty)
                            {
                                continue;
                            }
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
                                DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 开始拾取动画，物品: {spawnedItem.WeaponCopy.Item.Name}, 目标槽位: {targetSlot}，骑马状态: {agent.HasMount}");
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
        #endregion
        #region 重置方法
        /// <summary>
        /// 重置武器缓存
        /// </summary>
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

                _highWeapons.Clear();
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"ResetWeaponCache出错: {ex.Message}");
#endif
            }
        }
        /// <summary>
        /// 重置Agent拾取状态
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <param name="reason">原因</param>
        private void ResetAgentPickupState(Agent agent, string reason = null)
        {
            if (agent == null) return;

            try
            {
                // 重置所有状态
                if (_agentStates.TryGetValue(agent, out var state))
                {
                    state.Reset();
                    state.LastStateUpdateTime = 0f;
                    state.NextSearchTime = 0f;
                    state.TargetWeapon = null;
                }

                if ((Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle) && !agent.IsUsingGameObject)
                {
                    agent.DisableScriptedMovement();
                }
                else if (!agent.IsUsingGameObject)
                {
                    agent.DisableScriptedMovement();
                    agent.AIMoveToGameObjectDisable();
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
        #endregion
        #region Mission任务方法
        /// <summary>
        /// 每Tick执行
        /// </summary>
        /// <param name="dt">时间间隔</param>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            try
            {
                UpdateWeaponCache();

                if (_highWeapons.Count > 0)
                {
                    CheckHighWeapons();
                }

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

                                    // 立即更新检查时间，避免卡住
                                    if (_agentStates.TryGetValue(agent, out var newState))
                                    {
                                        newState.LastStateUpdateTime = 0f;
                                        newState.NextSearchTime = 0f;
                                    }
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
        /// <summary>
        /// 当Agent被删除时执行
        /// </summary>
        /// <param name="affectedAgent">被删除的Agent</param>
        public override void OnAgentDeleted(Agent affectedAgent)
        {
            if (affectedAgent == null) return;

            try
            {
                // 先清理自己的状态
                if (_agentStates.Remove(affectedAgent))
                {
#if DEBUG
                    DebugHelper.Log("PickUpWeapon", $"清理已删除Agent {affectedAgent.Name} 的拾取状态");
#endif
                }

                // 同步清理缴械状态
                Patches.AgentWeaponDropPatch.RemoveDisarmCooldown(affectedAgent);
                base.OnAgentDeleted(affectedAgent);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"清理Agent状态时出错: {ex.Message}");
#endif
            }
        }
        /// <summary>
        /// 当任务结束时执行
        /// </summary>
        protected override void OnEndMission()
        {
            try
            {
#if DEBUG
                int agentCount = _agentStates.Count;
                int weaponCount = _previousWeapons.Count;
#endif

                ResetWeaponCache();
                _cachedWeapons.Clear();
                _agentStates.Clear();

                // 同步清理所有缴械状态
                Patches.AgentWeaponDropPatch.ClearAllCooldowns();

#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"任务结束，清理状态 - Agents: {agentCount}, Weapons: {weaponCount}");
#endif

                base.OnEndMission();
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"清理Mission结束状态时出错: {ex.Message}");
#endif
            }
        }
        #endregion
        #region 检查武器高度
        private void CheckHighWeapons()
        {
            float currentTime = Mission.Current.CurrentTime;
            var weaponsToRemove = new List<SpawnedItemEntity>();
            
            foreach (var kvp in _highWeapons)
            {
                var weapon = kvp.Key;
                var (originalHeight, recordTime) = kvp.Value;
                
                // 如果武器已被移除或不存在
                if (weapon == null || weapon.IsRemoved || weapon.GameEntity == null)
                {
                    weaponsToRemove.Add(weapon);
                    continue;
                }
                
                // 检查是否到了重新检查的时间
                if (currentTime - recordTime >= HIGH_WEAPON_RECHECK_TIME)
                {
                    // 获取武器的全局位置
                    var weaponPosition = weapon.GameEntity.GlobalPosition;
                    // 获取地形高度
                    float terrainHeight = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(weaponPosition.x, weaponPosition.y, 0));
                    
                    // 计算武器与地形之间的高度差
                    float heightDifference = weaponPosition.z - terrainHeight;
                    
                    // 如果高度差变小了（说明已经落地或正在下落）
                    if (heightDifference < originalHeight)
                    {
                        if (heightDifference <= High)
                        {
#if DEBUG
                            InformationManager.DisplayMessage(new InformationMessage($"武器已落地，添加到缓存", Colors.Green));
#endif
                            // 添加到缓存
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
                    }
                    else
                    {
#if DEBUG
                        InformationManager.DisplayMessage(new InformationMessage($"武器高度未变化，移除", Colors.Red));
#endif
                    }
                    
                    weaponsToRemove.Add(weapon);
                }
            }
            
            // 从高处武器列表中移除已处理的武器
            foreach (var weapon in weaponsToRemove)
            {
                _highWeapons.Remove(weapon);
            }
        }
        #endregion
    }
}