using TaleWorlds.MountAndBlade;
using HarmonyLib;
using PickItUp.Behaviors;
using System.Reflection;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private static Harmony _harmony;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            
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