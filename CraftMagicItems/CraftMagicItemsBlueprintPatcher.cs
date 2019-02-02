using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Enums.Damage;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CraftMagicItems {
    public class CraftMagicItemsBlueprintPatcher {
        public const string TimerBlueprintGuid = "52e4be2ba79c8c94d907bdbaf23ec15f#CraftMagicItems(timer)";

        public static readonly Regex BlueprintRegex =
            new Regex($"({OldBlueprintPrefix}|{BlueprintPrefix})"
                      + @"\(("
                      + @"CL=(?<casterLevel>\d+)(?<spellLevelMatch>,SL=(?<spellLevel>\d+))?(?<spellIdMatch>,spellId=\((?<spellId>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\))?"
                      + @"|enchantments=\((?<enchantments>|([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\)(,remove=(?<remove>[0-9a-f;]+))?(,name=(?<name>[^✔]+)✔)?"
                      + @"(,ability=(?<ability>null|[0-9a-f]+))?(,activatableAbility=(?<activatableAbility>null|[0-9a-f]+))?(,material=(?<material>[a-zA-Z]+))?"
                      + @"(,visual=(?<visual>null|[0-9a-f]+))?(,CL=(?<casterLevel>[0-9]+))?(,SL=(?<spellLevel>[0-9]+))?(,perDay=(?<perDay>[0-9]+))?"
                      + @"|feat=(?<feat>[-a-z]+)"
                      + @"|(?<timer>timer)"
                      + @"|(?<components>(Component\[(?<index>[0-9]+)\](?<field>[^=]*)?=(?<value>[^,)]+),?)+(,nameId=(?<nameId>[^,)]+))?(,descriptionId=(?<descriptionId>[^,)]+))?)"
                      + @")\)");

        private static readonly ItemsFilter.ItemType[] SlotsWhichShowEnchantments = {
            ItemsFilter.ItemType.Weapon,
            ItemsFilter.ItemType.Armor,
            ItemsFilter.ItemType.Shield
        };

        // TODO remove the ScribeScroll prefix eventually
        private const string OldBlueprintPrefix = "#ScribeScroll";
        public const string BlueprintPrefix = "#CraftMagicItems";

        private readonly CraftMagicItemsAccessors accessors;

        public CraftMagicItemsBlueprintPatcher(CraftMagicItemsAccessors accessors, bool modEnabled) {
            this.accessors = accessors;
            CustomBlueprintBuilder.Initialise(ApplyBlueprintPatch, modEnabled,
                "d8e1ebc1062d8cc42abff78783856b0d#CraftMagicItems(Component[1]=CraftMagicItems.WeaponSizeChange#CraftMagicItems,Component[1].SizeCategoryChange=1)",
                "d8e1ebc1062d8cc42abff78783856b0d#CraftMagicItems(Component[1]=CraftMagicItems.WeaponBaseSizeChange#CraftMagicItems,Component[1].SizeCategoryChange=1)");
        }

        public string BuildCustomSpellItemGuid(string originalGuid, int casterLevel, int spellLevel = -1, string spellId = null) {
            // Check if GUID is already customised by this mod
            var match = BlueprintRegex.Match(originalGuid);
            if (match.Success && match.Groups["casterLevel"].Success) {
                // Remove the existing customisation
                originalGuid = CustomBlueprintBuilder.AssetGuidWithoutMatch(originalGuid, match);
                // Use any values which aren't explicitly overridden
                if (spellLevel == -1 && match.Groups["spellLevelMatch"].Success) {
                    spellLevel = int.Parse(match.Groups["spellLevel"].Value);
                }

                if (spellId == null && match.Groups["spellIdMatch"].Success) {
                    spellId = match.Groups["spellId"].Value;
                }
            }

            return $"{originalGuid}{BlueprintPrefix}(CL={casterLevel}" +
                   $"{(spellLevel == -1 ? "" : $",SL={spellLevel}")}" +
                   $"{(spellId == null ? "" : $",spellId=({spellId})")}" +
                   ")";
        }

        public string BuildCustomRecipeItemGuid(string originalGuid, IEnumerable<string> enchantments, string[] remove = null, string name = null,
            string ability = null, string activatableAbility = null, PhysicalDamageMaterial material = 0, string visual = null, int casterLevel = -1,
            int spellLevel = -1, int perDay = -1) {
            // Check if GUID is already customised by this mod
            var match = BlueprintRegex.Match(originalGuid);
            if (match.Success && match.Groups["enchantments"].Success) {
                var enchantmentsList = enchantments.Concat(match.Groups["enchantments"].Value.Split(';'))
                    .Where(guid => guid.Length > 0).Distinct().ToList();
                var removeList = match.Groups["remove"].Success
                    ? (remove ?? Enumerable.Empty<string>()).Concat(match.Groups["remove"].Value.Split(';')).Distinct().ToList()
                    : remove?.ToList();
                if (removeList != null) {
                    foreach (var guid in removeList.ToArray()) {
                        if (enchantmentsList.Contains(guid)) {
                            enchantmentsList.Remove(guid);
                            removeList.Remove(guid);
                        }
                    }
                }

                enchantments = enchantmentsList;
                remove = removeList?.Count > 0 ? removeList.ToArray() : null;
                if (name == null && match.Groups["name"].Success) {
                    name = match.Groups["name"].Value;
                }

                if (ability == null && match.Groups["ability"].Success) {
                    ability = match.Groups["ability"].Value;
                }

                if (activatableAbility == null && match.Groups["activatableAbility"].Success) {
                    activatableAbility = match.Groups["activatableAbility"].Value;
                }

                if (material == 0 && match.Groups["material"].Success) {
                    Enum.TryParse(match.Groups["material"].Value, out material);
                }

                if (visual == null && match.Groups["visual"].Success) {
                    visual = match.Groups["visual"].Value;
                }

                if (match.Groups["casterLevel"].Success) {
                    casterLevel = Math.Max(casterLevel, int.Parse(match.Groups["casterLevel"].Value));
                }

                if (match.Groups["spellLevel"].Success) {
                    spellLevel = Math.Max(spellLevel, int.Parse(match.Groups["spellLevel"].Value));
                }

                if (perDay == -1 && match.Groups["perDay"].Success) {
                    perDay = Math.Max(perDay, int.Parse(match.Groups["perDay"].Value));
                }

                // Remove the original customisation.
                originalGuid = CustomBlueprintBuilder.AssetGuidWithoutMatch(originalGuid, match);
            }

            return $"{originalGuid}{BlueprintPrefix}(enchantments=({string.Join(";", enchantments)})" +
                   $"{(remove == null || remove.Length == 0 ? "" : ",remove=" + string.Join(";", remove))}" +
                   $"{(name == null ? "" : $",name={name.Replace('✔', '_')}✔")}" +
                   $"{(ability == null ? "" : $",ability={ability}")}" +
                   $"{(activatableAbility == null ? "" : $",activatableAbility={activatableAbility}")}" +
                   $"{(material == 0 ? "" : $",material={material}")}" +
                   $"{(visual == null ? "" : $",visual={visual}")}" +
                   $"{(casterLevel == -1 ? "" : $",CL={casterLevel}")}" +
                   $"{(spellLevel == -1 ? "" : $",SL={spellLevel}")}" +
                   $"{(perDay == -1 ? "" : $",perDay={perDay}")}" +
                   ")";
        }

        private string BuildCustomComponentsItemGuid(string originalGuid, string[] values, string nameId, string descriptionId) {
            var components = "";
            for (var index = 0; index < values.Length; index += 3) {
                components += $"{(index > 0 ? "," : "")}Component[{values[index]}]{values[index + 1]}={values[index + 2]}";
            }

            return
                $"{originalGuid}{BlueprintPrefix}({components}{(nameId == null ? "" : $",nameId={nameId}")}{(descriptionId == null ? "" : $",descriptionId={descriptionId}")})";
        }

        private string BuildCustomFeatGuid(string originalGuid, string feat) {
            return $"{originalGuid}{BlueprintPrefix}(feat={feat})";
        }

        private string ApplyTimerBlueprintPatch(BlueprintBuff blueprint) {
            blueprint.ComponentsArray = new BlueprintComponent[] {ScriptableObject.CreateInstance<CraftingTimerComponent>()};
            accessors.SetBlueprintBuffFlags(blueprint, 2 + 8); // BlueprintBluff.Flags enum is private.  Values are HiddenInUi = 2 + StayOnDeath = 8
            blueprint.FxOnStart = new PrefabLink();
            blueprint.FxOnRemove = new PrefabLink();
            // Set the display name - it's hidden in the UI, but someone might find it in Bag of Tricks.
            accessors.SetBlueprintUnitFactDisplayName(blueprint, new L10NString("craftMagicItems-buff-name"));
            return TimerBlueprintGuid;
        }

        private string ApplyFeatBlueprintPatch(BlueprintFeature blueprint, Match match) {
            var feat = match.Groups["feat"].Value;
            accessors.SetBlueprintUnitFactDisplayName(blueprint, new L10NString($"craftMagicItems-feat-{feat}-displayName"));
            accessors.SetBlueprintUnitFactDescription(blueprint, new L10NString($"craftMagicItems-feat-{feat}-description"));
            var icon = Image2Sprite.Create($"{Main.ModEntry.Path}/Icons/craft-{feat}.png");
            accessors.SetBlueprintUnitFactIcon(blueprint, icon);
            var prerequisite = ScriptableObject.CreateInstance<PrerequisiteCasterLevel>();
            var featGuid = BuildCustomFeatGuid(blueprint.AssetGuid, feat);
            var itemData = Main.ItemCraftingData.First(data => data.FeatGuid == featGuid);
            prerequisite.SetPrerequisiteCasterLevel(itemData.MinimumCasterLevel);
            blueprint.ComponentsArray = new BlueprintComponent[] {prerequisite};
            return featGuid;
        }

        private string ApplySpellItemBlueprintPatch(BlueprintItemEquipmentUsable blueprint, Match match) {
            var casterLevel = int.Parse(match.Groups["casterLevel"].Value);
            blueprint.CasterLevel = casterLevel;
            var spellLevel = -1;
            if (match.Groups["spellLevelMatch"].Success) {
                spellLevel = int.Parse(match.Groups["spellLevel"].Value);
                blueprint.SpellLevel = spellLevel;
            }

            string spellId = null;
            if (match.Groups["spellIdMatch"].Success) {
                spellId = match.Groups["spellId"].Value;
                blueprint.Ability = (BlueprintAbility) ResourcesLibrary.TryGetBlueprint(spellId);
                blueprint.DC = 0;
            }

            if (blueprint.Ability != null && blueprint.Ability.LocalizedSavingThrow != null && blueprint.Ability.LocalizedSavingThrow.IsSet()) {
                blueprint.DC = 10 + blueprint.SpellLevel * 3 / 2;
            }

            accessors.SetBlueprintItemEquipmentUsableCost(blueprint, 0); // Allow the game to auto-calculate the cost
            // Also store the new item blueprint in our spell-to-item lookup dictionary.
            var itemBlueprintsForSpell = Main.FindItemBlueprintsForSpell(blueprint.Ability, blueprint.Type);
            if (itemBlueprintsForSpell == null || !itemBlueprintsForSpell.Contains(blueprint)) {
                Main.AddItemBlueprintForSpell(blueprint.Type, blueprint);
            }

            return BuildCustomSpellItemGuid(blueprint.AssetGuid, casterLevel, spellLevel, spellId);
        }

        private string ApplyRecipeItemBlueprintPatch(BlueprintItemEquipment blueprint, Match match) {
            var priceDelta = blueprint.Cost - Main.RulesRecipeItemCost(blueprint);
            if (blueprint is BlueprintItemShield shield) {
                var armourComponentClone = Object.Instantiate(shield.ArmorComponent);
                ApplyRecipeItemBlueprintPatch(armourComponentClone, match);
                accessors.SetBlueprintItemShieldArmorComponent(shield, armourComponentClone);
                if (shield.WeaponComponent) {
                    var weaponComponentClone = Object.Instantiate(shield.WeaponComponent);
                    ApplyRecipeItemBlueprintPatch(weaponComponentClone, match);
                    accessors.SetBlueprintItemShieldWeaponComponent(shield, weaponComponentClone);
                }
            }

            var initiallyMundane = blueprint.Enchantments.Count == 0 && blueprint.Ability == null && blueprint.ActivatableAbility == null;

            // Copy Enchantments so we leave base blueprint alone
            var enchantmentsCopy = blueprint.Enchantments.ToList();
            accessors.SetBlueprintItemCachedEnchantments(blueprint, enchantmentsCopy);
            // Remove enchantments first, to see if we end up with an item with no abilities.
            string[] removedIds = null;
            var removed = new List<BlueprintItemEnchantment>();
            if (match.Groups["remove"].Success) {
                removedIds = match.Groups["remove"].Value.Split(';');
                foreach (var guid in removedIds) {
                    var enchantment = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(guid);
                    if (!enchantment) {
                        throw new Exception($"Failed to load enchantment {guid}");
                    }

                    removed.Add(enchantment);
                    enchantmentsCopy.Remove(enchantment);
                }
            }

            string ability = null;
            if (match.Groups["ability"].Success) {
                ability = match.Groups["ability"].Value;
                blueprint.Ability = ability == "null" ? null : ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(ability);
                blueprint.RestoreChargesOnRest = (ability != "null");
            }

            string activatableAbility = null;
            if (match.Groups["activatableAbility"].Success) {
                activatableAbility = match.Groups["activatableAbility"].Value;
                blueprint.ActivatableAbility = activatableAbility == "null"
                    ? null
                    : ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(activatableAbility);
            }

            if (!initiallyMundane && enchantmentsCopy.Count == 0
                                  && (blueprint.Ability == null || ability != null) && (blueprint.ActivatableAbility == null || activatableAbility != null)) {
                // We're down to a base item with no abilities - reset priceDelta.
                priceDelta = 0;
            }

            var enchantmentsValue = match.Groups["enchantments"].Value;
            var enchantmentIds = enchantmentsValue.Split(';');
            var enchantmentsForDescription = new List<BlueprintItemEnchantment>();
            if (!string.IsNullOrEmpty(enchantmentsValue)) {
                foreach (var guid in enchantmentIds) {
                    var enchantment = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(guid);
                    if (!enchantment) {
                        throw new Exception($"Failed to load enchantment {guid}");
                    }

                    enchantmentsForDescription.Add(enchantment);

                    if (!(blueprint is BlueprintItemShield) && (Main.GetItemType(blueprint) != ItemsFilter.ItemType.Shield
                                                                || Main.FindSourceRecipe(guid, blueprint) != null)) {
                        enchantmentsCopy.Add(enchantment);
                    }
                }
            }

            PhysicalDamageMaterial material = 0;
            if (match.Groups["material"].Success && blueprint is BlueprintItemWeapon weapon) {
                Enum.TryParse(match.Groups["material"].Value, out material);
                accessors.SetBlueprintItemWeaponDamageType(weapon, TraverseCloneAndSetField(weapon.DamageType, "Physical.Material", material.ToString()));
                accessors.SetBlueprintItemWeaponOverrideDamageType(weapon, true);
            }

            string visual = null;
            if (match.Groups["visual"].Success) {
                visual = match.Groups["visual"].Value;
                // Copy icon from a different item
                var copyFromBlueprint = visual == "null" ? null : ResourcesLibrary.TryGetBlueprint<BlueprintItem>(visual);
                var iconSprite = copyFromBlueprint == null ? null : copyFromBlueprint.Icon;
                accessors.SetBlueprintItemIcon(blueprint, iconSprite);
                if (blueprint is BlueprintItemEquipmentHand && copyFromBlueprint is BlueprintItemEquipmentHand equipmentHand) {
                    accessors.SetBlueprintItemEquipmentHandVisualParameters(equipmentHand, equipmentHand.VisualParameters);
                } else if (blueprint is BlueprintItemArmor && copyFromBlueprint is BlueprintItemArmor armour) {
                    accessors.SetBlueprintItemArmorVisualParameters(armour, armour.VisualParameters);
                }
            }

            var casterLevel = -1;
            if (match.Groups["casterLevel"].Success) {
                casterLevel = int.Parse(match.Groups["casterLevel"].Value);
                blueprint.CasterLevel = casterLevel;
            }

            var spellLevel = -1;
            if (match.Groups["spellLevel"].Success) {
                spellLevel = int.Parse(match.Groups["spellLevel"].Value);
                blueprint.SpellLevel = spellLevel;
            }

            var perDay = -1;
            if (match.Groups["perDay"].Success) {
                perDay = int.Parse(match.Groups["perDay"].Value);
                blueprint.Charges = perDay;
            }

            string name = null;
            if (match.Groups["name"].Success) {
                name = match.Groups["name"].Value;
                accessors.SetBlueprintItemDisplayNameText(blueprint, new FakeL10NString(name));
            }

            if (!SlotsWhichShowEnchantments.Contains(blueprint.ItemType)) {
                accessors.SetBlueprintItemDescriptionText(blueprint,
                    Main.BuildCustomRecipeItemDescription(blueprint, enchantmentsForDescription, removed, ability, casterLevel, perDay));
                accessors.SetBlueprintItemFlavorText(blueprint, new L10NString(""));
            }

            accessors.SetBlueprintItemCost(blueprint, Main.RulesRecipeItemCost(blueprint) + priceDelta);
            return BuildCustomRecipeItemGuid(blueprint.AssetGuid, enchantmentIds, removedIds, name, ability, activatableAbility, material, visual, casterLevel,
                spellLevel, perDay);
        }

        private T CloneObject<T>(T originalObject) {
            var type = originalObject.GetType();
            if (typeof(ScriptableObject).IsAssignableFrom(type)) {
                return (T) (object) Object.Instantiate(originalObject as Object);
            }

            var clone = (T) Activator.CreateInstance(type);
            for (; type != null && type != typeof(Object); type = type.BaseType) {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields) {
                    field.SetValue(clone, field.GetValue(originalObject));
                }
            }

            return clone;
        }

        private T TraverseCloneAndSetField<T>(T original, string field, string value) where T : class {
            if (string.IsNullOrEmpty(field)) {
                value = value.Replace("#", ", ");
                var componentType = Type.GetType(value);
                if (componentType == null) {
                    throw new Exception($"Failed to create object with type {value}");
                }

                var componentObject = typeof(ScriptableObject).IsAssignableFrom(componentType)
                    ? ScriptableObject.CreateInstance(componentType)
                    : Activator.CreateInstance(componentType);

                if (!(componentObject is T component)) {
                    throw new Exception($"Failed to create expected instance with type {value}, " +
                                        $"result is {componentType.FullName}");
                }

                return component;
            } else {
                // Strip leading . off field
                if (field.StartsWith(".")) {
                    field = field.Substring(1);
                }
            }

            var clone = CloneObject(original);
            var fieldNameEnd = field.IndexOf('.');
            if (fieldNameEnd < 0) {
                var fieldAccess = Harmony12.Traverse.Create(clone).Field(field);
                if (!fieldAccess.FieldExists()) {
                    throw new Exception(
                        $"Field {field} does not exist on original of type {clone.GetType().FullName}, available fields: {string.Join(", ", Harmony12.Traverse.Create(clone).Fields())}");
                }

                if (value == "null") {
                    fieldAccess.SetValue(null);
                } else if (typeof(BlueprintScriptableObject).IsAssignableFrom(fieldAccess.GetValueType())) {
                    fieldAccess.SetValue(ResourcesLibrary.TryGetBlueprint(value));
                } else if (fieldAccess.GetValueType() == typeof(LocalizedString)) {
                    fieldAccess.SetValue(new L10NString(value));
                } else if (fieldAccess.GetValueType() == typeof(bool)) {
                    fieldAccess.SetValue(value == "true");
                } else if (fieldAccess.GetValueType() == typeof(int)) {
                    fieldAccess.SetValue(int.Parse(value));
                } else if (fieldAccess.GetValueType().IsEnum) {
                    fieldAccess.SetValue(Enum.Parse(fieldAccess.GetValueType(), value));
                } else {
                    fieldAccess.SetValue(value);
                }
            } else {
                var thisField = field.Substring(0, fieldNameEnd);
                var remainingFields = field.Substring(fieldNameEnd + 1);
                var arrayPos = thisField.IndexOf('[');
                if (arrayPos < 0) {
                    var fieldAccess = Harmony12.Traverse.Create(clone).Field(thisField);
                    if (!fieldAccess.FieldExists()) {
                        throw new Exception(
                            $"Field {thisField} does not exist on original of type {clone.GetType().FullName}, available fields: {string.Join(", ", Harmony12.Traverse.Create(clone).Fields())}");
                    }

                    if (fieldAccess.GetValueType().IsArray) {
                        throw new Exception($"Field {thisField} is an array but overall access {field} did not index the array");
                    }

                    fieldAccess.SetValue(TraverseCloneAndSetField(fieldAccess.GetValue(), remainingFields, value));
                } else {
                    var index = int.Parse(new string(thisField.Skip(arrayPos + 1).TakeWhile(char.IsDigit).ToArray()));
                    thisField = field.Substring(0, arrayPos);
                    var fieldAccess = Harmony12.Traverse.Create(clone).Field(thisField);
                    if (!fieldAccess.FieldExists()) {
                        throw new Exception(
                            $"Field {thisField} does not exist on original of type {clone.GetType().FullName}, available fields: {string.Join(", ", Harmony12.Traverse.Create(clone).Fields())}");
                    }

                    if (!fieldAccess.GetValueType().IsArray) {
                        throw new Exception(
                            $"Field {thisField} is of type {fieldAccess.GetValueType().FullName} but overall access {field} used an array index");
                    }

                    // TODO if I use fieldAccess.GetValue<object[]>().ToArray() to make this universally applicable, the SetValue fails saying it can't
                    // convert object[] to e.g. BlueprintComponent[].  Hard-code to only support BlueprintComponent for array for now.
                    var arrayClone = fieldAccess.GetValue<BlueprintComponent[]>().ToArray();
                    arrayClone[index] = TraverseCloneAndSetField(arrayClone[index], remainingFields, value);
                    fieldAccess.SetValue(arrayClone);
                }
            }

            return clone;
        }

        private string ApplyItemEnchantmentBlueprintPatch(BlueprintItemEnchantment blueprint, Match match) {
            var values = new List<string>();
            // Ensure Components array is not shared with base blueprint
            var componentsCopy = blueprint.ComponentsArray.ToArray();
            var indexCaptures = match.Groups["index"].Captures;
            var fieldCaptures = match.Groups["field"].Captures;
            var valueCaptures = match.Groups["value"].Captures;
            for (var index = 0; index < indexCaptures.Count; ++index) {
                var componentIndex = int.Parse(indexCaptures[index].Value);
                var field = fieldCaptures[index].Value;
                var value = valueCaptures[index].Value;
                values.Add(indexCaptures[index].Value);
                values.Add(field);
                values.Add(value);
                if (componentIndex >= componentsCopy.Length) {
                    var component = TraverseCloneAndSetField<BlueprintComponent>(null, field, value);
                    componentsCopy = componentsCopy.Concat(new[] {component}).ToArray();
                } else {
                    componentsCopy[componentIndex] = TraverseCloneAndSetField(componentsCopy[componentIndex], field, value);
                }
            }

            blueprint.ComponentsArray = componentsCopy;
            string nameId = null;
            if (match.Groups["nameId"].Success) {
                nameId = match.Groups["nameId"].Value;
                accessors.SetBlueprintItemEnchantmentEnchantName(blueprint, new L10NString(nameId));
            }

            string descriptionId = null;
            if (match.Groups["descriptionId"].Success) {
                descriptionId = match.Groups["descriptionId"].Value;
                accessors.SetBlueprintItemEnchantmentDescription(blueprint, new L10NString(descriptionId));
            }

            return BuildCustomComponentsItemGuid(blueprint.AssetGuid, values.ToArray(), nameId, descriptionId);
        }

        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again, or null if we couldn't change the
        // blueprint.
        private string ApplyBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
            switch (blueprint) {
                case BlueprintBuff buff when match.Groups["timer"].Success:
                    return ApplyTimerBlueprintPatch(buff);
                case BlueprintFeature feature when match.Groups["feat"].Success:
                    return ApplyFeatBlueprintPatch(feature, match);
                case BlueprintItemEquipmentUsable usable when match.Groups["casterLevel"].Success:
                    return ApplySpellItemBlueprintPatch(usable, match);
                case BlueprintItemEquipment equipment when match.Groups["enchantments"].Success:
                    return ApplyRecipeItemBlueprintPatch(equipment, match);
                case BlueprintItemEnchantment enchantment when match.Groups["components"].Success:
                    return ApplyItemEnchantmentBlueprintPatch(enchantment, match);
                default: {
                    throw new Exception($"Match of assetId {match.Value} didn't match blueprint type {blueprint.GetType()}");
                }
            }
        }
    }
}