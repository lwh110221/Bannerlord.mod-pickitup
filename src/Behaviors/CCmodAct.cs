using TaleWorlds.MountAndBlade;

namespace PickItUp.Behaviors
{
    public class CCmodAct
    {
        public static bool IsExecutingCinematicAction(Agent agent)
        {
        if (agent == null || !agent.IsActive()) return false;

            // 获取agent的当前动作
            var action0 = agent.GetCurrentAction(0).Name.ToLower();
            var action1 = agent.GetCurrentAction(1).Name.ToLower();

            // Cinematic Combat的所有动作名称
            string[] cinematicActions = new[]
            {
                // 基础动作
                "rotatedexecutioneridle",
                "map_attack_1h_spear_custom",
                
                // 训练动作
                "combat_training_char_a_custom",
                "combat_training_char_b_custom",
                "combat_training_char_c_custom",
                "combat_training_char_d_custom",
                "combat_training_char_e_custom",
                "combat_training_char_f_custom",
                "combat_training_char_a_custom_oh",
                "combat_training_char_b_custom_oh",
                "combat_training_char_c_custom_oh",
                "combat_training_char_d_custom_oh",
                "combat_training_char_e_custom_oh",
                "combat_training_char_f_custom_oh",
                
                // 格挡动作
                "blocked_slashleft_staff_balanced_left_stance_custom",
                "blocked_slashleft_1h_left_stance_custom",
                "blocked_slashright_2h_balanced_custom",
                "blocked_slashright_staff_custom",
                
                // 盾牌动作
                "taunt_hit_shield_1",
                "tripleshieldbash",
                "tripleshieldbash_leftstance",
                "doubleshieldbash",
                "doubleshieldbash_leftstance",
                "anim_shield_bash_execution_custom",
                "anim_shield_bash_execution_victim",
                "shield_taunt_shield_up_vc",
                
                // 愤怒动作
                "rage_cc",
                "rage_cc_leftstance",
                
                // 攻击动作
                "map_attack_1h_1_custom",
                "map_attack_1h_2_custom",
                "map_attack_1h_3_custom",
                "map_attack_2h_1_custom",
                "map_attack_2h_2_custom",
                
                // 长矛和盾牌动作
                "spear_shield_spear_idle",
                
                // 击倒动作
                "combat_knockdown_1",
                "combat_knockdown_2",
                "combat_knockdown_3",
                "combat_knockdown_4",
                "combat_knockdown_5",
                
                // 处决动作系列
                "combat_mercykill_1",
                "combat_mercykill_2",
                "combat_mercykill_3",
                "combat_mercykill_4",
                "combat_mercykill_5",
                "combat_mercykill_6",
                "combat_mercykill_7",
                "combat_mercykill_th_1",
                "combat_mercykill_th_2",
                "combat_mercykill_th_3",
                "combat_mercykill_th_4",
                "combat_mercykill_unarmed_1",
                
                // 击杀动作系列
                "combat_360_killmove",
                "combat_360_killmove_oh",
                "combat_360_killmove_death",
                "combat_slice_knee_killmove",
                "combat_slice_knee_killmove_oh",
                "combat_slice_knee_killmove_death",
                "combat_throw_behind_killmove",
                "combat_throw_behind_killmove_dead",
                "combat_shield_up_stab_in_face_killmove",
                "combat_shield_up_stab_in_face_killmove_dead",
                "combat_single_stab_killmove",
                "combat_single_stab_killmove_death",
                "combat_def_stab_throat_killmove",
                "combat_def_stab_throat_killmove_death",
                "combat_strike_def_stab_killmove",
                "combat_strike_def_stab_death_killmove",
                "combat_turn_weapon_hit_foot_killmove",
                "combat_turn_weapon_hit_foot_killmove_death",
                "combat_slice_hit_hit_killmove",
                "combat_slice_hit_hit_death_killmove",
                "combat_slice_slice_slice_stab_killmove",
                "combat_slice_slice_slice_stab_death_killmove",
                "combat_kick_stab_death_killmove",
                "combat_kick_stab_killmove",
                "combat_elbow_elbow_slice_killmove",
                "combat_elbow_elbow_slice_death_killmove",
                "combat_atk_kick_foot_stab_killmove",
                "combat_atk_kick_foot_stab_killmove_death",
                "combat_atk_stab_right_killmove",
                "combat_atk_stab_right_killmove_death",
                "combat_atk_advance_big_stab_killmove",
                "combat_atk_advance_big_stab_death_killmove",
                "combat_atk_throw_stab_spear_killmove",
                "combat_atk_throw_stab_spear_death_killmove",
                "combat_grab_ram_spear_kick_killmove",
                "combat_grab_ram_spear_kick_killmove_death",
                "combat_atk_def_spear_stab_killmove",
                "combat_atk_def_spear_stab_killmove_death",
                "combat_atk_def_spear_th_stab_killmove",
                "combat_atk_def_spear_th_stab_death_killmove",
                "combat_stab_kick_stab_spear_th_killmove",
                "combat_stab_kick_stab_spear_th_death_killmove",
                "combat_atk_slice_left_right_down_spear_killmove",
                "combat_atk_slice_left_right_spear_death_killmove",
                "combat_up_left_slice_throat_spear_killmove",
                "combat_up_left_slice_throat_spear_death_killmove",
                
                // 双手武器处决动作
                "combat_atk_swshd_th_1_killmove",
                "combat_atk_swshd_th_1_death_killmove",
                "combat_atk_swshd_th_2_killmove",
                "combat_atk_swshd_th_2_death_killmove",
                "combat_atk_th_spshd_1_killmove",
                "combat_atk_th_spshd_1_death_killmove",
                "combat_atk_th_spshd_2_killmove",
                "combat_atk_th_spshd_2_death_killmove",
                "combat_atk_th_swshd_1_killmove",
                "combat_atk_th_swshd_1_death_killmove",
                "combat_atk_th_swshd_2_killmove",
                "combat_atk_th_swshd_2_death_killmove",
                
                // 冲锋动作
                "combat_swshd_charge_atk_1",
                "combat_swshd_charge_def_1",
                "combat_swshd_charge_def_kill_1",
                "combat_swshd_charge_def_death_1",
                "combat_spearshd_charge_def_death_1",
                "combat_spearshd_charge_def_kill_1",
                "combat_spearshd_charge_atk_1",
                "combat_spearshd_charge_def_1",
                "combat_spearshd_charge_atk_1_fix",
                "combat_bluntshd_charge_def_kill_1",
                
                // 匹配战斗动作
                "anim_matched_combat_1",
                "anim_matched_combat_1_l",
                "act_matched_combat_2_atk",
                "act_matched_combat_2_def",
                "act_matched_combat_3_atk",
                "act_matched_combat_3_def",
                "act_matched_combat_4_atk",
                "act_matched_combat_4_def",
                "act_matched_combat_5_atk",
                "act_matched_combat_5_def",
                "act_matched_combat_6_atk",
                "act_matched_combat_6_def",
                "act_matched_combat_7_atk",
                "act_matched_combat_7_def",
                
                // 大型打击动作
                "combat_big_bashes_dead",
                "combat_big_bashes_kill",
                
                // 大师级打击动作
                "twohanded_master_strike_1_affected",
                "twohanded_master_strike_2_affected",
                "twohanded_master_strike_3_affected",
                "twohanded_master_strike_4_affected",
                "twohanded_master_strike_5_affected",
                "twohanded_master_strike_6_affected",
                "twohanded_master_strike_7_affected",
                "twohanded_master_strike_8_affected",
                "twohanded_master_strike_1_affector",
                "twohanded_master_strike_2_affector",
                "twohanded_master_strike_3_affector",
                "twohanded_master_strike_4_affector",
                "twohanded_master_strike_5_affector",
                "twohanded_master_strike_6_affector",
                "twohanded_master_strike_7_affector",
                "twohanded_master_strike_8_affector",
                "onehanded_master_strike_1_affected",
                "onehanded_master_strike_2_affected",
                "onehanded_master_strike_3_affected",
                "onehanded_master_strike_4_affected",
                "onehanded_master_strike_5_affected",
                "onehanded_master_strike_6_affected",
                "onehanded_master_strike_7_affected",
                "onehanded_master_strike_8_affected",
                "onehanded_master_strike_9_affected",
                "onehanded_master_strike_10_affected",
                "onehanded_master_strike_11_affected",
                "onehanded_master_strike_1_affector",
                "onehanded_master_strike_2_affector",
                "onehanded_master_strike_3_affector",
                "onehanded_master_strike_4_affector",
                "onehanded_master_strike_5_affector",
                "onehanded_master_strike_6_affector",
                "onehanded_master_strike_7_affector",
                "onehanded_master_strike_8_affector",
                "onehanded_master_strike_9_affector",
                "onehanded_master_strike_10_affector",
                "onehanded_master_strike_11_affector",
                
                // 闪避动作
                "cc_dodge_right",
                "cc_twohanded_dodge_left",
                "cc_twohanded_dodge_back",
                "cc_onehanded_dodge_right",
                "cc_onehanded_dodge_left",
                "cc_onehanded_dodge_back"
            };

            // 检查是否正在执行任何一个动作
            foreach (var actionName in cinematicActions)
            {
                if (action0.Contains(actionName) || action1.Contains(actionName))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
