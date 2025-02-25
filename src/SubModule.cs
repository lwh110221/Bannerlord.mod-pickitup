using TaleWorlds.MountAndBlade;
using HarmonyLib;
using PickItUp.Behaviors;
using System.Reflection;
using TaleWorlds.Core;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private static Harmony _harmony;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
        }
 
        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            var _ = Settings.Settings.Instance;
            if (_harmony == null)
            {
                _harmony = new Harmony("mod.bannerlord.pickitup");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
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