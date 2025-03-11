using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Localization;

namespace PickItUp.Settings
{
    public class McmSettings : AttributeGlobalSettings<McmSettings>
    {
        public override string Id => "PickItUp_v1";
        public override string DisplayName => new TextObject("{=ahao_mod_name}PickItUp -Ahao221").ToString();
        public override string FolderName => "PickItUp";
        public override string FormatType => "json";

        public new static McmSettings Instance => AttributeGlobalSettings<McmSettings>.Instance;

        [SettingPropertyBool("{=ahao_items}Enable Weapon Persistence",
            RequireRestart = false,
            HintText = "{=ahao_items_hint}If enabled, dropped weapons will remain in the battlefield indefinitely. Default: On", Order = 1)]
        [SettingPropertyGroup("{=ahao_items_settings}Items Setting", GroupOrder = 4)]
        public bool EnableWeaponPersistence { get; set; } = false;

        [SettingPropertyBool("{=ahao_show_status}Show Status Message",
            RequireRestart = false,
            HintText = "{=ahao_show_status_hint}Whether to display status messages when entering the battlefield. Default: On", Order = 2)]
        [SettingPropertyGroup("{=ahao_items_settings}Items Setting", GroupOrder = 4)]
        public bool ShowStatusMessage { get; set; } = true;

        [SettingPropertyFloatingInteger("{=ahao_pickup_delay}Disoriented Duration", 0.5f, 5.0f, "0.0", RequireRestart = false,
        HintText = "{=ahao_pickup_delay_hint}The time (in seconds) AI remains disoriented after losing weapon before attempting to pick it up. Default: 1.5 seconds", Order = 0)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 1)]
        public float PickupDelay { get; set; } = 1.5f;

        [SettingPropertyFloatingInteger("{=ahao_search_radius}Search Radius", 3.0f, 20.0f, "0.0", RequireRestart = false,
            HintText = "{=ahao_search_radius_hint}The range (in meters) AI searches for dropped weapons. Default: 5.0 meters", Order = 1)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 1)]
        public float SearchRadius { get; set; } = 5.0f;

        [SettingPropertyBool("{=ahao_drop_bag}Dropped empty ammo pouches", RequireRestart = false,
            HintText = "{=ahao_drop_bag_hint}If enabled, empty 'thrown weapon' ammo pouches will be dropped. Default: Enabled", Order = 2)]
        [SettingPropertyGroup("{=ahao_pickup_settings}Pickup Settings", GroupOrder = 1)]
        public bool DropBag { get; set; } = true;

        // 武器类型设置
        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types", GroupOrder = 3)]
        [SettingPropertyBool("{=ahao_one_handed_sword}One Handed Sword", RequireRestart = false,
            HintText = "{=ahao_one_handed_sword_hint}Allow AI to pick up one-handed swords", Order = 0)]
        public bool PickupOneHandedSword { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_two_handed_sword}Two Handed Sword", RequireRestart = false,
            HintText = "{=ahao_two_handed_sword_hint}Allow AI to pick up two-handed swords", Order = 1)]
        public bool PickupTwoHandedSword { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_one_handed_axe}One Handed Axe", RequireRestart = false,
            HintText = "{=ahao_one_handed_axe_hint}Allow AI to pick up one-handed axes", Order = 2)]
        public bool PickupOneHandedAxe { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_two_handed_axe}Two Handed Axe", RequireRestart = false,
            HintText = "{=ahao_two_handed_axe_hint}Allow AI to pick up two-handed axes", Order = 3)]
        public bool PickupTwoHandedAxe { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_mace}Mace", RequireRestart = false,
            HintText = "{=ahao_mace_hint}Allow AI to pick up maces", Order = 4)]
        public bool PickupMace { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_two_handed_mace}Two Handed Mace", RequireRestart = false,
            HintText = "{=ahao_two_handed_mace_hint}Allow AI to pick up two-handed maces", Order = 5)]
        public bool PickupTwoHandedMace { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_one_handed_polearm}One Handed Polearm", RequireRestart = false,
            HintText = "{=ahao_one_handed_polearm_hint}Allow AI to pick up one-handed polearms", Order = 6)]
        public bool PickupOneHandedPolearm { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_two_handed_polearm}Two Handed Polearm", RequireRestart = false,
            HintText = "{=ahao_two_handed_polearm_hint}Allow AI to pick up two-handed polearms", Order = 7)]
        public bool PickupTwoHandedPolearm { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_dagger}Dagger", RequireRestart = false,
            HintText = "{=ahao_dagger_hint}Allow AI to pick up daggers", Order = 8)]
        public bool PickupDagger { get; set; } = true;

        [SettingPropertyGroup("{=ahao_weapon_types}Allow Weapon Types")]
        [SettingPropertyBool("{=ahao_throwing_weapons}Throwing Weapons", RequireRestart = false,
            HintText = "{=ahao_throwing_weapons_hint}Allow AI to pick up throwing weapons (axes, knives, javelins)", Order = 9)]
        public bool PickupThrowingWeapons { get; set; } = true;

        // 武器持久化类型设置
        [SettingPropertyGroup("{=ahao_persistence_types}Non-disappearing item types", GroupOrder = 5)]
        [SettingPropertyBool("{=ahao_persist_melee}Melee Weapons", RequireRestart = false,
            HintText = "{=ahao_persist_melee_hint}Keep melee weapons (swords, axes, maces, etc.) do not disappear on battlefield", Order = 0)]
        public bool PersistMeleeWeapons { get; set; } = true;

        [SettingPropertyGroup("{=ahao_persistence_types}Non-disappearing item types")]
        [SettingPropertyBool("{=ahao_persist_ranged}Ranged Weapons", RequireRestart = false,
            HintText = "{=ahao_persist_ranged_hint}Keep ranged weapons (bows, crossbows) do not disappear on battlefield", Order = 1)]
        public bool PersistRangedWeapons { get; set; } = true;

        [SettingPropertyGroup("{=ahao_persistence_types}Non-disappearing item types")]
        [SettingPropertyBool("{=ahao_persist_thrown}Throwing Weapons", RequireRestart = false,
            HintText = "{=ahao_persist_thrown_hint}Keep throwing weapons do not disappear on battlefield", Order = 2)]
        public bool PersistThrownWeapons { get; set; } = true;

        [SettingPropertyGroup("{=ahao_persistence_types}Non-disappearing item types")]
        [SettingPropertyBool("{=ahao_persist_ammo}Ammunition", RequireRestart = false,
            HintText = "{=ahao_persist_ammo_hint}Keep ammunition (arrows, bolts, etc.) do not disappear on battlefield", Order = 3)]
        public bool PersistAmmunition { get; set; } = true;

        [SettingPropertyGroup("{=ahao_persistence_types}Non-disappearing item types")]
        [SettingPropertyBool("{=ahao_persist_shield}Shields", RequireRestart = false,
            HintText = "{=ahao_persist_shield_hint}Keep shields do not disappear on battlefield", Order = 4)]
        public bool PersistShields { get; set; } = true;

        // 盾牌拾取开关
        [SettingPropertyBool("{=ahao_shield}Shield Pickup",
            RequireRestart = false,
            HintText = "{=ahao_shield_hint}If enabled, the soldier with an empty right hand will pick up the shield. Default: On", Order = 1)]
        [SettingPropertyGroup("{=ahao_shield_settings}Shield", GroupOrder = 2)]
        public bool EnableShieldPickup { get; set; } = true;

        public McmSettings()
        {
            EnableWeaponPersistence = false;
            PickupDelay = 1.5f;
            SearchRadius = 5.0f;
            DropBag = true;
            ShowStatusMessage = true;
            PersistMeleeWeapons = true;
            PersistRangedWeapons = true;
            PersistThrownWeapons = true;
            PersistAmmunition = true;
            PersistShields = true;
            EnableShieldPickup = true;
        }
    }
}