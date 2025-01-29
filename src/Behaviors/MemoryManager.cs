using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;

namespace PickItUp.Behaviors
{
    public class MemoryManager
    {
        private readonly Dictionary<Agent, float> _lastPickupAttemptTime;
        private readonly Dictionary<Agent, (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot)> _pickupAnimationTracker;
        private readonly Dictionary<Agent, float> _lastPathCalculationTime;
        private float _lastCleanupTime;
        
        private const float CLEANUP_INTERVAL = 30f; // 每30秒进行一次清理
        private const float CACHE_LIFETIME = 60f;   // 缓存数据保留60秒
        
        private readonly Action<string> _debugLog;

        public MemoryManager(
            Dictionary<Agent, float> lastPickupAttemptTime,
            Dictionary<Agent, (float StartTime, SpawnedItemEntity WeaponToPickup, EquipmentIndex TargetSlot)> pickupAnimationTracker,
            Dictionary<Agent, float> lastPathCalculationTime,
            Action<string> debugLog)
        {
            _lastPickupAttemptTime = lastPickupAttemptTime;
            _pickupAnimationTracker = pickupAnimationTracker;
            _lastPathCalculationTime = lastPathCalculationTime;
            _debugLog = debugLog;
            _lastCleanupTime = 0f;
        }

        public void Update(float currentTime)
        {
            try
            {
                if (currentTime - _lastCleanupTime >= CLEANUP_INTERVAL)
                {
                    CleanupInactiveAgentData(currentTime);
                    _lastCleanupTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"更新内存管理器时出错: {ex.Message}");
            }
        }

        private void CleanupInactiveAgentData(float currentTime)
        {
            try
            {
                // 清理无效或不活跃的Agent数据
                var inactiveAgents = _lastPickupAttemptTime.Keys
                    .Where(agent => ShouldCleanupAgent(agent, currentTime))
                    .ToList();

                int cleanedCount = 0;
                foreach (var agent in inactiveAgents)
                {
                    cleanedCount += CleanupAgentData(agent);
                }

                // 清理路径计算缓存
                var oldPathCalculations = _lastPathCalculationTime.Keys
                    .Where(agent => ShouldCleanupAgent(agent, currentTime))
                    .ToList();

                foreach (var agent in oldPathCalculations)
                {
                    if (_lastPathCalculationTime.Remove(agent))
                    {
                        cleanedCount++;
                    }
                }

                if (cleanedCount > 0)
                {
                    _debugLog?.Invoke($"内存清理完成: 清理了 {cleanedCount} 条缓存数据");
                }
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"清理缓存数据时出错: {ex.Message}");
            }
        }

        private bool ShouldCleanupAgent(Agent agent, float currentTime)
        {
            try
            {
                if (agent == null || !agent.IsActive())
                    return true;

                // 检查Agent的基本状态
                if (agent.Health <= 0)
                    return true;

                // 检查Agent是否正在进行其他交互
                if (agent.InteractingWithAnyGameObject())
                    return true;

                // 检查最后拾取时间
                if (_lastPickupAttemptTime.TryGetValue(agent, out float lastAttempt))
                {
                    if (currentTime - lastAttempt > CACHE_LIFETIME)
                        return true;
                }

                // 检查最后路径计算时间
                if (_lastPathCalculationTime.TryGetValue(agent, out float lastCalc))
                {
                    if (currentTime - lastCalc > CACHE_LIFETIME)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"检查Agent清理状态时出错: {ex.Message}");
                return true; // 发生错误时倾向于清理
            }
        }

        public int CleanupAgentData(Agent agent)
        {
            try
            {
                if (agent == null)
                    return 0;

                int cleanedCount = 0;
                
                if (_lastPickupAttemptTime.Remove(agent))
                    cleanedCount++;
                
                if (_pickupAnimationTracker.Remove(agent))
                    cleanedCount++;
                
                if (_lastPathCalculationTime.Remove(agent))
                    cleanedCount++;

                return cleanedCount;
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"清理Agent数据时出错: {ex.Message}");
                return 0;
            }
        }

        public void CleanupAll()
        {
            try
            {
                int totalCleaned = 0;
                
                foreach (var agent in _pickupAnimationTracker.Keys.ToList())
                {
                    if (agent != null && agent.IsActive())
                    {
                        // 重置AI状态
                        agent.ClearTargetFrame();
                        agent.InvalidateTargetAgent();
                        agent.InvalidateAIWeaponSelections();
                    }
                    totalCleaned += CleanupAgentData(agent);
                }
                
                _lastPickupAttemptTime.Clear();
                _lastPathCalculationTime.Clear();
                
                _debugLog?.Invoke($"=== 所有内存数据已清理，共清理 {totalCleaned} 条数据 ===");
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"清理所有数据时出错: {ex.Message}");
            }
        }
    }
} 