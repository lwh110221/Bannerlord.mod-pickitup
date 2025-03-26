using System.Reflection;
using HarmonyLib;
using PickItUp.Behaviors;
using PickItUp.Patches;
using PickItUp.Settings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using System;
using System.Linq;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private const string HARMONY_ID = "mod.bannerlord.pickitup";
        private static Harmony _mainHarmony;
        private static Harmony _extendHarmony;
        private ReloadReset _reloadReset;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                _extendHarmony = new Harmony(HARMONY_ID);
                _reloadReset = new ReloadReset();

                if (_reloadReset.HasRBMPatches() && ModSettings.Instance.EnableReloadResetPatch)
                {
                    ApplyReloadResetPatch();
                }
            }
            catch (Exception ex)
            {
                DebugHelper.LogError("SubModule", "Error: " + ex.Message);
            }
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);

            try
            {
                var _ = McmSettings.Instance;

                if (_mainHarmony == null)
                {
                    _mainHarmony = new Harmony(HARMONY_ID);
                    var types = Assembly.GetExecutingAssembly().GetTypes()
                        .Where(t => t != typeof(ReloadReset));
                    foreach (var type in types)
                    {
                        _mainHarmony.CreateClassProcessor(type).Patch();
                    }
#if DEBUG
                    InformationManager.DisplayMessage(new InformationMessage("主Mod补丁已加载", Colors.Green));
#endif
                }

                if (_reloadReset.HasRBMPatches() && ModSettings.Instance.EnableReloadResetPatch)
                {
                    ApplyReloadResetPatch();
#if DEBUG
                    InformationManager.DisplayMessage(new InformationMessage("已重新应用ReloadReset补丁", Colors.Green));
#endif
                }
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

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            if (_mainHarmony != null)
            {
                _mainHarmony.UnpatchAll(HARMONY_ID);
                _mainHarmony = null;

            }
            if (_extendHarmony != null)
            {
                _extendHarmony.UnpatchAll(HARMONY_ID);
                _extendHarmony = null;
            }
        }


        #region RBM填装重置补丁
        private void ApplyReloadResetPatch()
        {
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
            _extendHarmony.CreateClassProcessor(typeof(ReloadReset)).Patch();
        }
        #endregion
    }
}