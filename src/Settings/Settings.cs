using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace PickItUp.Settings
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        private const string ModName = "Pick It Up";
        private const string ModVersion = "v1.0.0";
        
        public override string Id => "PickItUp_v1";
        public override string DisplayName => ModName;
        public override string FolderName => "PickItUp";
        public override string FormatType => "json";
        public override string ModName => ModName;
        public override string ModuleFolderName => "PickItUp";
        public string ModVersion => ModVersion;

        // General Settings
        [SettingPropertyGroup("General Settings")]
        [SettingPropertyBool("Enable Mod", Order = 0, RequireRestart = false, HintText = "Enable/Disable the mod functionality")]
        public bool IsEnabled { get; set; } = true;

        [SettingPropertyGroup("General Settings")]
        [SettingPropertyFloatingInteger("Weapon Search Radius", 1f, 20f, "0.0 meters", Order = 1, RequireRestart = false, HintText = "The radius (in meters) within which AI will search for weapons")]
        public float WeaponSearchRadius { get; set; } = 10f;

        [SettingPropertyGroup("General Settings")]
        [SettingPropertyFloatingInteger("Search Interval", 0.5f, 5f, "0.0 seconds", Order = 2, RequireRestart = false, HintText = "How often (in seconds) AI will check for nearby weapons")]
        public float SearchInterval { get; set; } = 1f;

        // Weapon Settings
        [SettingPropertyGroup("Weapon Settings")]
        [SettingPropertyBool("Only Melee Weapons", Order = 0, RequireRestart = false, HintText = "If enabled, AI will only pick up melee weapons")]
        public bool OnlyMeleeWeapons { get; set; } = true;

        // Debug Settings
        [SettingPropertyGroup("Debug Settings")]
        [SettingPropertyBool("Debug Mode", Order = 0, RequireRestart = false, HintText = "Enable debug messages in game")]
        public bool DebugMode { get; set; } = false;
    }
} 