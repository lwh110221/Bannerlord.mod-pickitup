using System.Reflection;
using HarmonyLib;
using PickItUp.Behaviors;
using PickItUp.Patches;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using System;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private static Harmony _mainHarmony;
        private static Harmony _extendHarmony;
        private ReloadReset _reloadReset;

        protected override void OnSubModuleLoad()
        {
            try
            {
                _extendHarmony = new Harmony("mod.bannerlord.pickitupextend");
                _reloadReset = new ReloadReset();
                
                if (!_reloadReset.HasRBMPatches())
                {
                    return;
                }

                var originalWeaponEquipped = AccessTools.Method(typeof(Agent), "WeaponEquipped");
                var originalWieldedItemChange = AccessTools.Method(typeof(Agent), "OnWieldedItemIndexChange");

                if (originalWeaponEquipped != null)
                {
                    _extendHarmony.Unpatch(originalWeaponEquipped, HarmonyPatchType.All, "com.rbmcombat");
                }

                if (originalWieldedItemChange != null)
                {
                    _extendHarmony.Unpatch(originalWieldedItemChange, HarmonyPatchType.All, "com.rbmcombat");
                }

                _extendHarmony.PatchAll(typeof(ReloadReset).Assembly);

#if DEBUG
                InformationManager.DisplayMessage(new InformationMessage("ReloadReset补丁已加载", Colors.Green));
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                InformationManager.DisplayMessage(new InformationMessage($"初始化补丁时出错: {ex.Message}", Colors.Red));
#endif
            }
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            
            try
            {
                var _ = Settings.Settings.Instance;

                // 加载主mod的补丁
                if (_mainHarmony == null)
                {
                    _mainHarmony = new Harmony("mod.bannerlord.pickitup");
                    _mainHarmony.PatchAll(Assembly.GetExecutingAssembly());
#if DEBUG
                    InformationManager.DisplayMessage(new InformationMessage("主Mod补丁已加载", Colors.Green));
#endif
                }
                
                if (!_reloadReset.HasRBMPatches())
                {
                    return;
                }

                var originalWeaponEquipped = AccessTools.Method(typeof(Agent), "WeaponEquipped");
                var originalWieldedItemChange = AccessTools.Method(typeof(Agent), "OnWieldedItemIndexChange");

                if (originalWeaponEquipped != null)
                {
                    _extendHarmony.Unpatch(originalWeaponEquipped, HarmonyPatchType.All, "com.rbmcombat");
                }

                if (originalWieldedItemChange != null)
                {
                    _extendHarmony.Unpatch(originalWieldedItemChange, HarmonyPatchType.All, "com.rbmcombat");
                }

                _extendHarmony.PatchAll(typeof(ReloadReset).Assembly);

#if DEBUG
                InformationManager.DisplayMessage(new InformationMessage("已重新应用ReloadReset补丁", Colors.Green));
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                InformationManager.DisplayMessage(new InformationMessage($"应用补丁时出错: {ex.Message}", Colors.Red));
#endif
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new PickUpWeaponBehavior());
            mission.AddMissionBehavior(new DroppedItemManager());
        }
    }
} 