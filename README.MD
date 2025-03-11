# Pick It UP

[中文版说明](README_CN.MD)

## Introduction
Agent will pick up weapons or shields dropped on the battlefield

## Version
✦[v1.2.4.2] - 2025-03-11

## Features
1. If a soldier does not have a melee weapon on him, he will pick up the nearest melee weapon (including thrown weapons that can be switched to melee, and will not pick up un-pickupable weapons rooted in trees)
2. Ranged soldiers make sure they have a melee weapon on them
3. Soldiers who pick up one-handed weapons pick up shields (Needs to be turned on in the mod setting menu)
4. CinematicCombatMod support:
- Does not pick up when performing a CinematicCombat action

## Configuration
New menu configuration items:
- Can configure the types of items that don't disappear and the types of weapons that soldiers will pick up
- Pickup shield function switch

## Shield Pickup Conditions:
- The soldier is not equipped with a shield
- The soldier has a one-handed weapon in his right hand
- The soldier has a free weapon slot on his body

## Another option - For players who want only soldiers who originally had shields to be more active in picking up shields
- Only soldiers who originally had shields but lost them will pick up shields
- More active than vanilla



Works well with other mods that shoot down weapons in combat.

## Tip
If RBM is used, this mod will by default revert the RBM's ammo reload modification to vanilla (faster and more stable).
- If you don't want to reset ammo reload to vanilla, please modify EnableReloadResetPatch to false in config.xml in the mod folder
- Changes will take effect on the next game launch
- Enabling this feature is recommended

## Language Support
- Simplified Chinese
- English

## Contact
- Author: Ahao221
- Email: ahao221x@gmail.com
