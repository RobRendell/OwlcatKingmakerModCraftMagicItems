using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;

namespace CraftMagicItems {
    [ComponentName("Weapon Size Change")]
    [AllowMultipleComponents]
    /**
     * Can't just use Owlcat's MeleeWeaponSizeChange rulebook handler because it's annotated with AllowedOn(typeof (BlueprintUnitFact)), which means it doesn't
     * work on BlueprintItemWeapon
     */
    public class WeaponSizeChange : WeaponEnchantmentLogic, IInitiatorRulebookHandler<RuleCalculateWeaponStats> {
        public int SizeCategoryChange;

        public void OnEventAboutToTrigger(RuleCalculateWeaponStats evt) {
            if (SizeCategoryChange > 0)
                evt.IncreaseWeaponSize();
            else if (SizeCategoryChange < 0)
                evt.DecreaseWeaponSize();
        }

        public void OnEventDidTrigger(RuleCalculateWeaponStats evt) {
        }

        public void OnEventAboutToTrigger(RuleCalculateAttackBonusWithoutTarget evt) {
        }

        public void OnEventDidTrigger(RuleCalculateAttackBonusWithoutTarget evt) {
        }
    }
}