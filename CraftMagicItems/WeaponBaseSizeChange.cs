using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Enums;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;

namespace CraftMagicItems {
    [ComponentName("Weapon Base Size Change")]
    [AllowMultipleComponents]
    /**
     * Weapon size changes in RuleCalculateWeaponStats cannot stack, so do base size changes in a postfix patch
     */
    public class WeaponBaseSizeChange : WeaponEnchantmentLogic, IInitiatorRulebookHandler<RuleCalculateWeaponStats> {
        public int SizeCategoryChange;

        public void OnEventAboutToTrigger(RuleCalculateWeaponStats evt) {
        }

        public void OnEventDidTrigger(RuleCalculateWeaponStats evt) {
        }

        public void OnEventAboutToTrigger(RuleCalculateAttackBonusWithoutTarget evt) {
        }

        public void OnEventDidTrigger(RuleCalculateAttackBonusWithoutTarget evt) {
        }

        [Harmony12.HarmonyPatch(typeof(RuleCalculateWeaponStats), "WeaponSize", Harmony12.MethodType.Getter)]
        // ReSharper disable once UnusedMember.Local
        private static class RuleCalculateWeaponStatsWeaponSizePatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(RuleCalculateWeaponStats __instance, ref Size __result) {
                var adjustment = 0;
                foreach (var enchantment in __instance.Weapon.Enchantments) {
                    var baseSizeChange = enchantment.Blueprint.GetComponent<WeaponBaseSizeChange>();
                    if (baseSizeChange) {
                        adjustment += baseSizeChange.SizeCategoryChange;
                    }
                }

                if (adjustment > 0) {
                    __result += 1;
                } else if (adjustment < 0) {
                    __result -= 1;
                }
            }
        }
    }
}