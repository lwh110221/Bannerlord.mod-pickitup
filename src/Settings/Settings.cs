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

        [SettingPropertyBool("{=ahao_items}Enabled items do not disappear", RequireRestart = false,
            HintText = "{=ahao_items_hint}If enabled, dropped weapons will remain in the battlefield indefinitely. Default: On", Order = 0)]
        [SettingPropertyGroup("{=ahao_items_settings}Items Setting", GroupOrder = 1)]
        public bool EnableWeaponPersistence { get; set; } = true;

        [SettingPropertyFloatingInteger("{=ahao_pickup_delay}Disoriented Duration", 0.5f, 5.0f, "0.0", RequireRestart = false, 
        HintText = "{=ahao_pickup_delay_hint}The time (in seconds) AI remains disoriented after losing weapon before attempting to pick it up. Default: 1.5 seconds", Order = 0)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 0)]
        public float PickupDelay { get; set; } = 1.5f;

        [SettingPropertyFloatingInteger("{=ahao_search_radius}Search Radius", 3.0f, 20.0f, "0.0", RequireRestart = false, 
            HintText = "{=ahao_search_radius_hint}The range (in meters) AI searches for dropped weapons. Default: 5.0 meters", Order = 1)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 0)]
        public float SearchRadius { get; set; } = 5.0f; 

        [SettingPropertyFloatingInteger("{=ahao_pickup_cooldown}Pickup Cooldown", 0.5f, 3.0f, "0.0", RequireRestart = false, 
            HintText = "{=ahao_pickup_cooldown_hint}The interval (in seconds) between AI's pickup attempts. Default: 1.0 second", Order = 2)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 0)]
        public float PickupCooldown { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("{=ahao_cleanup_interval}Cleanup Interval", 10.0f, 60.0f, "0.0", RequireRestart = false, 
            HintText = "{=ahao_cleanup_interval_hint}The interval (in seconds) for memory cleanup. Default: 30.0 seconds", Order = 3)]
        [SettingPropertyGroup("{=ahao_huancun_settings}Cache Settings - Optimizations for overall game performance, recommended to keep default", GroupOrder = 0)]
        public float CleanupInterval { get; set; } = 30.0f;

        [SettingPropertyFloatingInteger("{=ahao_cache_lifetime}Cache Lifetime", 30.0f, 120.0f, "0.0", RequireRestart = false, 
            HintText = "{=ahao_cache_lifetime_hint}The duration (in seconds) cached data is retained. Default: 60.0 seconds", Order = 4)]
        [SettingPropertyGroup("{=ahao_huancun_settings}Cache Settings - Optimizations for overall game performance, recommended to keep default", GroupOrder = 0)]
        public float CacheLifetime { get; set; } = 60.0f;

        public Settings()
        {
            EnableWeaponPersistence = true;  // 默认启用武器持久化
            PickupDelay = 1.5f;        //懵逼时间
            SearchRadius = 5.0f;       //搜索范围
            PickupCooldown = 1.0f;     //拾取冷却
            CleanupInterval = 30.0f;   //清理间隔
            CacheLifetime = 60.0f;     //缓存生命周期
        }
    }
} 