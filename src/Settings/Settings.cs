using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Localization;

namespace PickItUp.Settings
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public override string Id => "PickItUp_v1";
        public override string DisplayName => new TextObject("{=ahao_mod_name}PickItUp -Ahao221").ToString();
        public override string FolderName => "PickItUp";
        public override string FormatType => "json";
        
        public new static Settings Instance => AttributeGlobalSettings<Settings>.Instance;

        [SettingPropertyBool("{=ahao_items}Enable Weapon Persistence", 
            RequireRestart = false,
            HintText = "{=ahao_items_hint}If enabled, dropped weapons will remain in the battlefield indefinitely. Default: On")]
        [SettingPropertyGroup("{=ahao_items_settings}Items Setting")]
        public bool EnableWeaponPersistence { get; set; } = false;

        [SettingPropertyBool("{=ahao_show_status}Show Status Message", 
            RequireRestart = false,
            HintText = "{=ahao_show_status_hint}Whether to display status messages when entering the battlefield. Default: On")]
        [SettingPropertyGroup("{=ahao_items_settings}Items Setting")]
        public bool ShowStatusMessage { get; set; } = true;

        [SettingPropertyFloatingInteger("{=ahao_pickup_delay}Disoriented Duration", 0.5f, 5.0f, "0.0", RequireRestart = false, 
        HintText = "{=ahao_pickup_delay_hint}The time (in seconds) AI remains disoriented after losing weapon before attempting to pick it up. Default: 1.5 seconds", Order = 0)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 0)]
        public float PickupDelay { get; set; } = 1.5f;

        [SettingPropertyFloatingInteger("{=ahao_search_radius}Search Radius", 3.0f, 20.0f, "0.0", RequireRestart = false, 
            HintText = "{=ahao_search_radius_hint}The range (in meters) AI searches for dropped weapons. Default: 5.0 meters", Order = 1)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 0)]
        public float SearchRadius { get; set; } = 5.0f; 

        public Settings()
        {
            EnableWeaponPersistence = false;  // 默认启用武器持久化
            PickupDelay = 1.5f;        //懵逼时间
            SearchRadius = 5.0f;       //搜索范围
            ShowStatusMessage = true; // 默认显示状态消息
        }
    }
} 