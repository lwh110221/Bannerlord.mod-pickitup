using TaleWorlds.MountAndBlade;
using HarmonyLib;
using PickItUp.Behaviors;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony("mod.bannerlord.pickitup");
            harmony.PatchAll();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new PickUpWeaponBehavior());
        }
    }
} 