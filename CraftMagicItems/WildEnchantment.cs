using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Designers;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;

namespace CraftMagicItems {
    public class WildFact : BlueprintBuff {
        public WildFact() {
            Main.Accessors.SetBlueprintBuffFlags(this, 2 + 8); // Enum is private... 2 = HiddenInUi, 8 = StayOnDeath
            Stacking = StackingType.Replace;
            Frequency = DurationRate.Rounds;
            FxOnStart = new PrefabLink();
            FxOnRemove = new PrefabLink();
            Main.Accessors.SetBlueprintUnitFactDisplayName(this, new L10NString("craftMagicItems-enchantment-wild-name"));
            Main.Accessors.SetBlueprintUnitFactDescription(this, new L10NString("craftMagicItems-enchantment-wild-description"));
        }
    }

    public class WildEnchantmentLogic : AddUnitFactEquipment {
        private const string WildShapeTurnBackAbilityGuid = "a2cb181ee69860b46b82844a3a8569b8";

        private ModifiableValue.Modifier modifier;

        public WildEnchantmentLogic() {
            name = "AddUnitFactEquipment-WildFact";
        }

        private bool IsOwnerWildShaped() {
            var wildShapeTurnBackAbility = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(WildShapeTurnBackAbilityGuid);
            return Owner.Owner.HasFact(wildShapeTurnBackAbility);
        }

        private void ApplyModifier() {
            // Apply AC modifier
            if (Owner.Owner == null || modifier != null) {
                return;
            }
            var stat = Owner.Owner.Stats.GetStat(StatType.AC);
            switch (Owner) {
                case ItemEntityArmor armor: {
                    var acBonus = armor.Blueprint.ArmorBonus + GameHelper.GetItemEnhancementBonus(armor);
                    modifier = stat.AddItemModifier(acBonus, armor, ModifierDescriptor.Armor);
                    break;
                }
                case ItemEntityShield shield: {
                    var acBonus = shield.Blueprint.ArmorComponent.ArmorBonus + GameHelper.GetItemEnhancementBonus(shield.ArmorComponent);
                    modifier = stat.AddItemModifier(acBonus, shield, ModifierDescriptor.Shield);
                    break;
                }
            }
        }

        public override void OnFactActivate() {
            base.OnFactActivate();
            if (!IsOwnerWildShaped() && modifier != null) {
                modifier.Remove();
                modifier = null;
            }
        }

        public override void OnFactDeactivate() {
            if (IsOwnerWildShaped()) {
                ApplyModifier();
            } else {
                base.OnFactDeactivate();
            }
        }

        public override void PostLoad() {
            base.PostLoad();
            if (IsOwnerWildShaped()) {
                ApplyModifier();
            }
        }
    }

    public class WildEnchantment : BlueprintItemEnchantment {
        private const string WildEnchantmentGuid = "dd0e096412423d646929d9b945fd6d4c#CraftMagicItems(wildEnchantment)";
        private const string WildFactGuid = "28384b1d7e25c8743b8bbfc56211ac8c#CraftMagicItems(wildFact)";

        private static bool initialised;

        public WildEnchantment() {
            Main.Accessors.SetBlueprintItemEnchantmentEnchantName(this, new L10NString("craftMagicItems-enchantment-wild-name"));
            Main.Accessors.SetBlueprintItemEnchantmentDescription(this, new L10NString("craftMagicItems-enchantment-wild-description"));
            Main.Accessors.SetBlueprintItemEnchantmentPrefix(this, new L10NString(""));
            Main.Accessors.SetBlueprintItemEnchantmentSuffix(this, new L10NString(""));
            Main.Accessors.SetBlueprintItemEnchantmentEnchantmentCost(this, 1);
            Main.Accessors.SetBlueprintItemEnchantmentEnchantmentIdentifyDC(this, 5);
        }

        [Harmony12.HarmonyPatch(typeof(MainMenu), "Start")]
        // ReSharper disable once UnusedMember.Local
        private static class MainMenuStartPatch {
            private static void AddBlueprint(string guid, BlueprintScriptableObject blueprint) {
                Main.Accessors.SetBlueprintScriptableObjectAssetGuid(blueprint, guid);
                ResourcesLibrary.LibraryObject.BlueprintsByAssetId?.Add(guid, blueprint);
                ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(blueprint);
            }

            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                if (!initialised) {
                    initialised = true;
                    var blueprintWildEnchantment = CreateInstance<WildEnchantment>();
                    AddBlueprint(WildEnchantmentGuid, blueprintWildEnchantment);
                    var blueprintWildFact = CreateInstance<WildFact>();
                    AddBlueprint(WildFactGuid, blueprintWildFact);
                    var wildEnchantmentLogic = CreateInstance<WildEnchantmentLogic>();
                    wildEnchantmentLogic.Blueprint = blueprintWildFact;
                    blueprintWildEnchantment.ComponentsArray = new BlueprintComponent[] {wildEnchantmentLogic};
                }
            }
        }
    }
}