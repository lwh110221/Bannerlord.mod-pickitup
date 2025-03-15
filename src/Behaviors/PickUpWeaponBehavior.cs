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
        private float SearchRadius => Settings.McmSettings.Instance?.SearchRadius ?? 5.0f;
        private const float High = 1.2f;
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
            if (!WeaponCheck.IsShieldPickupEnabled())
            {
                return false;
            }

            if (agent.HasShieldCached)
            {
                return false;
            }

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
        /// <summary>
        /// 检查Agent是否可以拾取武器
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <returns>是否可以拾取武器</returns>
        private bool CanAgentPickup(Agent agent)
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
        #endregion
        #region 武器缓存
        /// <summary>
        /// 添加武器到缓存
        /// </summary>
        /// <param name="weapon">武器</param>
        private void AddWeaponToCache(SpawnedItemEntity weapon)
        {
            if (weapon == null ||
                weapon.GameEntity == null ||
                weapon.HasAIMovingTo ||
                weapon.WeaponCopy.IsAnyAmmo() && weapon.WeaponCopy.Amount <= 0 ||
                weapon.WeaponCopy.Item.PrimaryWeapon.IsBow ||
                weapon.WeaponCopy.Item.PrimaryWeapon.IsCrossBow ||
                weapon.IsBanner())
                return;

            if (weapon.IsStuckMissile())
            {
                var weaponPosition = weapon.GameEntity.GlobalPosition;
                float terrainHeight = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(weaponPosition.x, weaponPosition.y, 0));
                float heightDifference = weaponPosition.z - terrainHeight;

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
        /// <summary>
        /// 从缓存中移除武器
        /// </summary>
        /// <param name="weapon">武器</param>
        private void RemoveWeaponFromCache(SpawnedItemEntity weapon)
        {
            if (weapon == null) return;

            if (_weaponCellCache.TryGetValue(weapon, out var cell))
            {
                if (_spatialWeaponCache.TryGetValue(cell, out var weaponList))
                {
                    weaponList.Remove(weapon);

                    if (weaponList.Count == 0)
                    {
                        _spatialWeaponCache.Remove(cell);
                        ReturnListToPool(weaponList);
                    }
                }
                _weaponCellCache.Remove(weapon);
            }

            foreach (var agentState in _agentStates)
            {
                if (agentState.Value.TargetWeapon == weapon)
                {
                    ResetAgentPickupState(agentState.Key, "目标武器已被移除");
                }
            }

            _hasDroppedWeapons = _spatialWeaponCache.Any(x => x.Value.Count > 0);
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
                    if (WeaponCheck.IsValidWeapon(spawnedItem))
                    {
                        currentWeapons.Add(spawnedItem);

                        if (!_previousWeapons.Contains(spawnedItem))
                        {
                            AddWeaponToCache(spawnedItem);
                        }
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

            foreach (var oldWeapon in _previousWeapons)
            {
                if (!currentWeapons.Contains(oldWeapon) || oldWeapon.IsRemoved)
                {
                    RemoveWeaponFromCache(oldWeapon);
                }
            }

            _previousWeapons.Clear();
            _previousWeapons.AddRange(currentWeapons);
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
                              !WeaponCheck.IsItemShield(w) &&
                              w.GameEntity != null && // 确保GameEntity不为空
                              w.WeaponCopy.Item?.WeaponComponent != null && // 确保物品和武器组件不为空
                              w.GameEntity.GlobalPosition.Distance(agentPosition) <= SearchRadius &&
                              (!agent.HasMount || WeaponCheck.CanUseWeaponOnHorseback(w.WeaponCopy.Item.WeaponComponent)))
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
                // 查找盾牌 - 添加更多安全检查
                var nearestShield = nearbyWeapons
                    .Where(w => w != null &&
                              !w.IsRemoved &&
                              w.GameEntity != null && // 确保GameEntity不为空
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
                agent.DisableScriptedMovement();
                agent.ClearTargetFrame();

                var targetPosition = weapon.GameEntity.GlobalPosition;

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
        /// <summary>
        /// 尝试拾取武器
        /// </summary>
        /// <param name="agent">Agent</param>
        private void TryPickupWeapon(Agent agent)
        {
            if (agent.IsUsingGameObject)
            {
                return;
            }

            if (agent.Equipment == null)
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"Agent {agent.Name} 的Equipment为空");
#endif
                return;
            }

            if (_agentStates.TryGetValue(agent, out var state) && state.AnimationState.WeaponToPickup != null)
            {
                return;
            }

            var agentPosition = agent.Position;

            bool needsWeapon = !agent.HasMeleeWeaponCached && !agent.HasSpearCached;
            bool needsShield = CanAgentPickupShield(agent);

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
                bool isShield = WeaponCheck.IsItemShield(spawnedItem);
                if ((isShield && !needsShield) || (!isShield && !needsWeapon))
                {
                    continue;
                }

                if (!agent.CanQuickPickUp(spawnedItem))
                {
                    continue;
                }

                EquipmentIndex targetSlot = MissionEquipment.SelectWeaponPickUpSlot(agent, spawnedItem.WeaponCopy, spawnedItem.IsStuckMissile());
                if (targetSlot == EquipmentIndex.None)
                {
                    continue;
                }
                if (!agent.Equipment[targetSlot].IsEmpty)
                {
                    continue;
                }

                string animationName;

                if (WeaponCheck.IsItemShield(spawnedItem))
                {
                    animationName = agent.HasMount ?
                        "act_pickup_from_left_down_horseback_begin" :
                        "act_pickup_down_left_begin";
                }
                else
                {
                    animationName = agent.HasMount ?
                        "act_pickup_from_right_down_horseback_begin" :
                        "act_pickup_down_begin";
                }

                agent.SetActionChannel(0, ActionIndexCache.Create(animationName), ignorePriority: true, 0UL);

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
        }
        #endregion
        #region 重置方法
        /// <summary>
        /// 重置武器缓存
        /// </summary>
        private void ResetWeaponCache()
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
        /// <summary>
        /// 重置Agent拾取状态
        /// </summary>
        /// <param name="agent">Agent</param>
        /// <param name="reason">原因</param>
        private void ResetAgentPickupState(Agent agent, string reason = null)
        {
            if (agent == null) return;

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
        #endregion
        #region Mission任务方法
        /// <summary>
        /// 每Tick执行
        /// </summary>
        /// <param name="dt">时间间隔</param>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

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

                            if (_agentStates.TryGetValue(agent, out var newState))
                            {
                                newState.LastStateUpdateTime = 0f;
                                newState.NextSearchTime = 0f;
                            }
                        }
                    }
                    else
                    {
                        ResetAgentPickupState(agent);
                    }
                }
            }

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
        /// <summary>
        /// 当Agent被删除时执行
        /// </summary>
        /// <param name="affectedAgent">被删除的Agent</param>
        public override void OnAgentDeleted(Agent affectedAgent)
        {
            if (affectedAgent == null) return;

            if (_agentStates.Remove(affectedAgent))
            {
#if DEBUG
                DebugHelper.Log("PickUpWeapon", $"清理已删除Agent {affectedAgent.Name} 的拾取状态");
#endif
            }

            Patches.AgentWeaponDropPatch.RemoveDisarmCooldown(affectedAgent);
            base.OnAgentDeleted(affectedAgent);
        }
        /// <summary>
        /// 当任务结束时执行
        /// </summary>
        protected override void OnEndMission()
        {
#if DEBUG
            int agentCount = _agentStates.Count;
            int weaponCount = _previousWeapons.Count;
#endif

            ResetWeaponCache();
            _cachedWeapons.Clear();
            _agentStates.Clear();

            Patches.AgentWeaponDropPatch.ClearAllCooldowns();

            CCmodAct.ClearCache();

#if DEBUG
            DebugHelper.Log("PickUpWeapon", $"任务结束，清理状态 - Agents: {agentCount}, Weapons: {weaponCount}");
#endif

            base.OnEndMission();
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

                if (weapon == null || weapon.IsRemoved || weapon.GameEntity == null)
                {
                    weaponsToRemove.Add(weapon);
                    continue;
                }

                if (currentTime - recordTime >= HIGH_WEAPON_RECHECK_TIME)
                {
                    var weaponPosition = weapon.GameEntity.GlobalPosition;
                    float terrainHeight = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(weaponPosition.x, weaponPosition.y, 0));
                    float heightDifference = weaponPosition.z - terrainHeight;

                    if (heightDifference < originalHeight)
                    {
                        if (heightDifference <= High)
                        {
#if DEBUG
                            InformationManager.DisplayMessage(new InformationMessage($"武器已落地，添加到缓存", Colors.Green));
#endif
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

            foreach (var weapon in weaponsToRemove)
            {
                _highWeapons.Remove(weapon);
            }
        }
        #endregion
    }
}