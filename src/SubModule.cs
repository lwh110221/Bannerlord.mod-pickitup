using TaleWorlds.MountAndBlade;
using HarmonyLib;
using PickItUp.Behaviors;
using PickItUp.Settings;
using System.Reflection;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private static Harmony _harmony;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            
            // 确保Settings已初始化
            var _ = Settings.Settings.Instance;
            
            if (_harmony == null)
            {
                _harmony = new Harmony("mod.bannerlord.pickitup");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            
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
        }
    }
} 