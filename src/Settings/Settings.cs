using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Localization;

namespace PickItUp.Settings
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public override string Id => "PickItUp_v1";
        public override string DisplayName => new TextObject("{=piu_mod_name}PickItUp - AI武器拾取").ToString();
        public override string FolderName => "PickItUp";
        public override string FormatType => "json";
        
        public new static Settings Instance => AttributeGlobalSettings<Settings>.Instance;

        [SettingPropertyFloatingInteger("{=piu_pickup_delay}懵逼时间", 0.5f, 5.0f, "0.0", RequireRestart = false, 
            HintText = "{=piu_pickup_delay_hint}武器被打落后AI的懵逼时间（秒）。默认：1.5 秒", Order = 0)]
        [SettingPropertyGroup("{=piu_pickup_settings}拾取设置", GroupOrder = 0)]
        public float PickupDelay { get; set; } = 1.5f;

        [SettingPropertyFloatingInteger("{=piu_search_radius}搜索范围", 3.0f, 20.0f, "0.0", RequireRestart = false, 
            HintText = "{=piu_search_radius_hint}AI搜索掉落武器的范围（米）。默认：5.0 米", Order = 1)]
        [SettingPropertyGroup("{=piu_pickup_settings}拾取设置", GroupOrder = 0)]
        public float SearchRadius { get; set; } = 5.0f; 

        [SettingPropertyFloatingInteger("{=piu_pickup_cooldown}拾取冷却时间", 0.5f, 3.0f, "0.0", RequireRestart = false, 
            HintText = "{=piu_pickup_cooldown_hint}AI两次拾取尝试之间的间隔时间（秒）。默认：1.0 秒", Order = 2)]
        [SettingPropertyGroup("{=piu_pickup_settings}拾取设置", GroupOrder = 0)]
        public float PickupCooldown { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("{=piu_cleanup_interval}清理间隔", 10.0f, 60.0f, "0.0", RequireRestart = false, 
            HintText = "{=piu_cleanup_interval_hint}内存清理的间隔时间（秒）。默认：30.0 秒", Order = 3)]
        [SettingPropertyGroup("{=piu_huancun_settings}缓存相关", GroupOrder = 0)]
        public float CleanupInterval { get; set; } = 30.0f;

        [SettingPropertyFloatingInteger("{=piu_cache_lifetime}缓存生命周期", 30.0f, 120.0f, "0.0", RequireRestart = false, 
            HintText = "{=piu_cache_lifetime_hint}缓存数据的保留时间（秒）。默认：60.0 秒", Order = 4)]
        [SettingPropertyGroup("{=piu_huancun_settings}缓存相关", GroupOrder = 0)]
        public float CacheLifetime { get; set; } = 60.0f;

        public Settings()
        {
            PickupDelay = 1.5f;        //懵逼时间
            SearchRadius = 5.0f;       //搜索范围
            PickupCooldown = 1.0f;     //拾取冷却
            CleanupInterval = 30.0f;   //清理间隔
            CacheLifetime = 60.0f;     //缓存生命周期
        }
    }
} 