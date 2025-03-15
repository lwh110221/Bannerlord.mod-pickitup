using System;
using System.Xml;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;


namespace PickItUp.Patches
{
    //<summary>
    // 恢复原版弓箭机制
    //</summary>
    public class ReloadReset
    {
        #region 检查
        //<summary>
        // 检查是否存在RBM补丁
        //</summary>    
        public bool HasRBMPatches()
        {
            var originalWeaponEquipped = AccessTools.Method(typeof(Agent), "WeaponEquipped");
            if (originalWeaponEquipped == null) return false;

            var patches = Harmony.GetPatchInfo(originalWeaponEquipped);
            if (patches == null) return false;

            foreach (var patch in patches.Prefixes)
            {
                if (patch.owner == "com.rbmcombat")
                {
                    return true;
                }
            }
            return false;
        }
        #endregion
        #region 补丁
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        [HarmonyBefore(new string[] { "com.rbmcombat" })]
        [HarmonyPriority(Priority.First)]
        private class RestoreVanillaWeaponEquippedPatch
        {
            private static bool Prefix(ref Agent __instance, EquipmentIndex equipmentSlot, in WeaponData weaponData, ref WeaponStatsData[] weaponStatsData)
            {
                if (weaponStatsData != null)
                {
                    for (int i = 0; i < weaponStatsData.Length; i++)
                    {
                        if ((WeaponClass)weaponStatsData[i].WeaponClass == WeaponClass.Bow)
                        {
                            var weaponFlags = __instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags;
                            weaponFlags |= WeaponFlags.UnloadWhenSheathed;
                            __instance.Equipment[equipmentSlot].GetWeaponComponentDataForUsage(0).WeaponFlags = weaponFlags;
                            weaponStatsData[i].WeaponFlags = (ulong)weaponFlags;
                        }
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        [HarmonyBefore(new string[] { "com.rbmcombat" })]
        [HarmonyPriority(Priority.First)]
        private class RestoreVanillaWieldedItemChangePatch
        {
            private static bool Prefix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                EquipmentIndex wieldedItemIndex = __instance.GetWieldedItemIndex(0);
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (__instance.Equipment[equipmentIndex].GetWeaponStatsData() != null &&
                            __instance.Equipment[equipmentIndex].GetWeaponStatsData().Length > 0)
                        {
                            WeaponStatsData wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData()[0];
                            if (wsd.WeaponClass == (int)WeaponClass.Bow)
                            {
                                var weaponFlags = __instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags;
                                weaponFlags |= WeaponFlags.UnloadWhenSheathed;
                                __instance.Equipment[equipmentIndex].GetWeaponComponentDataForUsage(0).WeaponFlags = weaponFlags;
                                wsd.WeaponFlags = (ulong)weaponFlags;
                                MissionWeapon mw = __instance.Equipment[equipmentIndex];
                                if (mw.AmmoWeapon.Amount > 0)
                                {
                                    __instance.Equipment.GetAmmoCountAndIndexOfType(mw.Item.Type, out var _, out var eIndex);
                                    if (eIndex != EquipmentIndex.None)
                                    {
                                        int ammoInHandCount = mw.AmmoWeapon.Amount;
                                        __instance.SetWeaponAmountInSlot(eIndex,
                                            Convert.ToInt16(__instance.Equipment[eIndex].Amount + ammoInHandCount),
                                            enforcePrimaryItem: true);
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }
        }
        [HarmonyPatch(typeof(MBObjectManager))]
        [HarmonyPatch("MergeTwoXmls")]
        private class MergeTwoXmlsPatch
        {
            private static void Prefix(ref XmlDocument xmlDocument1, ref XmlDocument xmlDocument2)
            {
                if (xmlDocument2?.BaseURI != null && xmlDocument2.BaseURI.Contains("RBMCombat_ranged"))
                {
                    var weapons = xmlDocument2.SelectNodes("//Weapon[@weapon_class='Bow']");
                    int modifiedCount = 0;

                    if (weapons != null)
                    {
                        foreach (XmlNode weapon in weapons)
                        {
                            if (weapon.Attributes?["ammo_limit"] != null)
                            {
                                weapon.Attributes["ammo_limit"].Value = "1";
                                modifiedCount++;
                            }
                        }

                        if (modifiedCount > 0)
                        {
#if DEBUG
                            InformationManager.DisplayMessage(new InformationMessage($"重置{modifiedCount}个弓箭的装填数", Colors.Green));
#endif
                        }
                    }
                }
            }
        }
        #endregion
    }
}