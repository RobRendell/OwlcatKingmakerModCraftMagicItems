using Kingmaker;
using Kingmaker.Items;
using Kingmaker.Items.Slots;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UI.Log;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Commands;
using Newtonsoft.Json;

namespace CraftMagicItems {
    public class BondedItemComponent : BuffLogic, IUnitEquipmentHandler {
        [JsonProperty] public ItemEntity ownerItem;

        [JsonProperty] public ItemEntity everyoneElseItem;

        // Switch the bonded object (ownerObject) with the un-enchanted version (everyoneObject) when it's equipped/unequipped by its owner.
        public void HandleEquipmentSlotUpdated(ItemSlot slot, ItemEntity previousItem) {
            var bondedComponent = Main.GetBondedItemComponentForCaster(slot.Owner);
            if (bondedComponent == null || bondedComponent.ownerItem == null
                                        || bondedComponent.everyoneElseItem == null || bondedComponent.ownerItem == bondedComponent.everyoneElseItem) {
                return;
            }
            if (slot.HasItem && slot.Item.Blueprint == bondedComponent.everyoneElseItem.Blueprint) {
                // Things can get confused if you drop an item then save and load.  Assume the matching blueprint means this is actually the same item.
                bondedComponent.everyoneElseItem = slot.Item;
            }
            using (new DisableBattleLog()) {
                if (previousItem == bondedComponent.ownerItem && bondedComponent.everyoneElseItem.Collection == null) {
                    // Removed bonded item - downgrade to everyone else item
                    Game.Instance.Player.Inventory.Remove(bondedComponent.ownerItem);
                    Game.Instance.Player.Inventory.Add(bondedComponent.everyoneElseItem);
                } else if (slot.HasItem && slot.Item == bondedComponent.everyoneElseItem && bondedComponent.ownerItem.Wielder != slot.Owner) {
                    // Equipped bonded item - upgrade to owner's enchanted version
                    Game.Instance.Player.Inventory.Remove(bondedComponent.everyoneElseItem);
                    slot.InsertItem(bondedComponent.ownerItem);
                }
                if (bondedComponent.ownerItem.Collection == bondedComponent.everyoneElseItem.Collection) {
                    // Just did a swap - remove everyoneElseItem
                    Game.Instance.Player.Inventory.Remove(bondedComponent.everyoneElseItem);
                }
            }
        }

        // If the owner of a bonded object casts a spell when not wielding it, they need to make a Concentration check or lose the spell.
        [Harmony12.HarmonyPatch(typeof(UnitUseAbility), "MakeConcentrationCheckIfCastingIsDifficult")]
        // ReSharper disable once UnusedMember.Local
        private static class UnitUseAbilityMakeConcentrationCheckIfCastingIsDifficultPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(UnitUseAbility __instance) {
                var caster = __instance.Executor;
                var bondedComponent = Main.GetBondedItemComponentForCaster(caster.Descriptor);
                if (bondedComponent != null && bondedComponent.ownerItem != null && bondedComponent.ownerItem.Wielder != caster.Descriptor) {
                    Main.AddBattleLogMessage(
                        Main.L10NFormat(caster, "craftMagicItems-logMessage-not-wielding-bonded-item", bondedComponent.ownerItem.Name),
                        new L10NString("craftMagicItems-bonded-item-glossary"));
                    // Concentration checks have no way of overriding the DC, so contrive some fake damage to give a DC of 20 + spell level.
                    var ruleDamage = new RuleDealDamage(caster, caster, null);
                    Main.Accessors.SetRuleDealDamageDamage(ruleDamage, 20);
                    __instance.MakeConcentrationCheck(ruleDamage);
                }
            }
        }
    }
}