using TaleWorlds.MountAndBlade;
using PickItUp.Behaviors;
using PickItUp.Settings;
using HarmonyLib;

namespace PickItUp
{
    public class SubModule : MBSubModuleBase
    {
        private readonly Harmony _harmony;
        private bool _isInitialized;

        public SubModule()
        {
            _harmony = new Harmony("mod.bannerlord.pickitup");
            _isInitialized = false;
        }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            
            if (!_isInitialized)
            {
                // 初始化设置
                Settings.Settings.Instance ??= new Settings.Settings();
                
                // 应用Harmony补丁
                _harmony.PatchAll();
                _isInitialized = true;
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();
            
            // 卸载所有补丁
            _harmony.UnpatchAll(_harmony.Id);
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            
            // 添加武器拾取行为
            mission.AddMissionBehavior(new WeaponPickupBehavior());
        }
    }
} 