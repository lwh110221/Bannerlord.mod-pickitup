using HarmonyLib;
using PickItUp.Behaviors;
using PickItUp.Settings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using System;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private const string HARMONY_ID = "mod.bannerlord.pickitup";
        private static Harmony _mainHarmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
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
                    _mainHarmony.PatchAll();
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
        }
    }
}