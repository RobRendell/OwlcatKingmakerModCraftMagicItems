using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.Controllers.Rest;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.Kingdom;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.UI;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UI.Log;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using Newtonsoft.Json;
using UnityEngine;
using UnityModManagerNet;
using Random = System.Random;

namespace CraftMagicItems {

    public class Settings: UnityModManager.ModSettings {
        public bool CraftingCostsNoGold;
        public bool IgnoreCraftingFeats;
        public bool CraftingTakesNoTime;
        public bool CraftAtFullSpeedWhileAdventuring;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }

    public static class Main {
        private const string OldBlueprintPrefix = "#ScribeScroll";
        private const string BlueprintPrefix = "#CraftMagicItems";
        private static readonly Regex BlueprintRegex = new Regex($"({OldBlueprintPrefix}|{BlueprintPrefix})"
            + @"\(("
            + @"CL=(?<casterLevel>\d+)(?<spellLevelMatch>,SL=(?<spellLevel>\d+))?(?<spellIdMatch>,spellId=\((?<spellId>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\))?"
            + @"|enchantments=\((?<enchantments>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\)(,remove=(?<remove>[0-9a-f;]+))?(,name=(?<name>[^✔]+)✔)?(,ability=(?<ability>null|[0-9a-f]+))?(,activatableAbility=(?<activatableAbility>null|[0-9a-f]+))?"
            + @"|feat=(?<feat>[a-z]+)"
            + @"|(?<timer>timer)"
            + @"|(?<components>(Component\[(?<index>[0-9]+)\].(?<field>[a-zA-Z]+)=(?<value>[0-9a-zA-Z]+),?)+)"
            + @")\)");
        private const string TimerBlueprintGuid = "52e4be2ba79c8c94d907bdbaf23ec15f#CraftMagicItems(timer)";
        private const int CraftingProgressPerDay = 500;
        private static readonly FeatureGroup[] CraftingFeatGroups = { FeatureGroup.Feat, FeatureGroup.WizardFeat };
        private static readonly LocalizedString KnowledgeArcanaLocalized = new L10NString("75941008-1ec4-4085-ab6d-17c18d15b662");
        private static readonly LocalizedString CasterLevelLocalized = new L10NString("dfb34498-61df-49b1-af18-0a84ce47fc98");
        private static readonly LocalizedString BonusLocalized = new L10NString("1d5831d6-3d9a-430b-a9c0-acc141327f56");
        
        private enum OpenSection {
            CraftsSection,
            ProjectsSection,
            FeatsSection,
            CheatsSection
        }

        public static UnityModManager.ModEntry ModEntry;

        private static bool modEnabled = true;
        private static Settings settings;
        private static ItemCraftingData[] itemCraftingData;
        private static OpenSection currentSection = OpenSection.CraftsSection;
        private static int selectedItemTypeIndex;
        private static int selectedSpellcasterIndex;
        private static int selectedSpellbookIndex;
        private static int selectedSpellLevelIndex;
        private static int selectedCasterLevel;
        private static bool selectedShowPreparedSpells;
        private static int selectedItemSlotIndex;
        private static int selectedUpgradeItemIndex;
        private static int selectedRecipeIndex;
        private static int selectedSubRecipeIndex;
        private static int selectedEnchantmentIndex;
        private static string selectedBaseGuid;
        private static string selectedCustomName;
        private static int selectedFeatToLearn;
        private static readonly CustomBlueprintBuilder CustomBlueprintBuilder = new CustomBlueprintBuilder(BlueprintRegex, ApplyBlueprintPatch);
        private static readonly Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> SpellIdToItem = new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();
        private static readonly Dictionary<string, List<BlueprintItemEquipment>> EnchantmentIdToItem = new Dictionary<string, List<BlueprintItemEquipment>>();
        private static readonly Dictionary<string, RecipeData> EnchantmentIdToRecipe = new Dictionary<string, RecipeData>();
        private static readonly Dictionary<string, int> EnchantmentIdToCost = new Dictionary<string, int>();
        private static readonly Random RandomGenerator = new Random();
        private static readonly List<LogDataManager.LogItemData> PendingLogItems = new List<LogDataManager.LogItemData>();

        // ReSharper disable once UnusedMember.Local
        private static void Load(UnityModManager.ModEntry modEntry) {
            ModEntry = modEntry;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            try {
                HarmonyInstance.Create("kingmaker.craftMagicItems").PatchAll(Assembly.GetExecutingAssembly());
            } catch (Exception e) {
                modEntry.Logger.Error($"Exception while patching Kingmaker: {e}");
                throw;
            }
            modEnabled = modEntry.Active;
            CustomBlueprintBuilder.Enabled = modEnabled;
            modEntry.OnSaveGUI = OnSaveGui;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
        }

        private static void OnSaveGui(UnityModManager.ModEntry modEntry) {
            settings.Save(modEntry);
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled) {
            modEnabled = enabled;
            CustomBlueprintBuilder.Enabled = enabled;
            MainMenuStartPatch.ModEnabledChanged();
            return true;
        }

        private static void OnGui(UnityModManager.ModEntry modEntry) {
            if (!modEnabled) {
                RenderLabel("The mod is disabled.  Loading saved games with custom items and feats will cause them to revert to regular versions.");
                return;
            }

            try {
                var mainCharacterValue = Game.Instance?.Player?.MainCharacter.Value;
                if (mainCharacterValue == null || !mainCharacterValue.IsViewActive || (
                        Game.Instance.CurrentMode != GameModeType.Default
                        && Game.Instance.CurrentMode != GameModeType.GlobalMap
                        && Game.Instance.CurrentMode != GameModeType.FullScreenUi
                        && Game.Instance.CurrentMode != GameModeType.Pause
                        && Game.Instance.CurrentMode != GameModeType.EscMode
                        && Game.Instance.CurrentMode != GameModeType.Rest
                        && Game.Instance.CurrentMode != GameModeType.Kingdom
                    )) {
                    RenderLabel("Item crafting is not available in this game state.");
                    return;
                }

                GUILayout.BeginVertical();

                RenderLabel($"Number of custom Craft Magic Items blueprints loaded: {CustomBlueprintBuilder.CustomBlueprintIDs.Count}");

                GetSelectedCaster(true);

                if (RenderToggleSection(ref currentSection, OpenSection.CraftsSection, "Crafting")) {
                    RenderCraftSection();
                }
                if (RenderToggleSection(ref currentSection, OpenSection.ProjectsSection, "Work in Progress")) {
                    RenderProjectsSection();
                }
                if (RenderToggleSection(ref currentSection, OpenSection.FeatsSection, "Feat Reassignment")) {
                    RenderFeatReassignmentSection();
                }
                if (RenderToggleSection(ref currentSection, OpenSection.CheatsSection, "Cheats")) {
                    RenderCheatsSection();
                }
                GUILayout.EndVertical();
            } catch (Exception e) {
                modEntry.Logger.Error($"Error rendering GUI: {e}");
            }
        }

        private static bool RenderToggleSection(ref OpenSection current, OpenSection mySection, string label) {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            bool toggledOn = GUILayout.Toggle(current == mySection, " " + label);
            if (toggledOn) {
                current = mySection;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return toggledOn;
        }
        
        private static T ReadJsonFile<T>(string fileName, params JsonConverter[] converters) {
            try {
                using (var reader = new StreamReader(fileName)) {
                    var json = reader.ReadToEnd();
                    return JsonConvert.DeserializeObject<T>(json, converters);
                }
            } catch (Exception e) {
                ModEntry.Logger.Warning($"Exception reading JSON data from file {fileName}: {e}");
                throw;
            }
        }

        private static CraftingTimerComponent GetCraftingTimerComponentForCaster(UnitDescriptor caster, bool create = false) {
            var timerBlueprint = (BlueprintBuff)ResourcesLibrary.TryGetBlueprint(TimerBlueprintGuid);
            var timer = (Buff)caster.GetFact(timerBlueprint);
            if (timer == null) {
                if (!create) {
                    return null;
                }
                caster.AddFact<Buff>(timerBlueprint);
                timer = (Buff)caster.GetFact(timerBlueprint);
            } else if (timer.Blueprint.AssetGuid.Length == CustomBlueprintBuilder.VanillaAssetIdLength) {
                // Clean up
                caster.RemoveFact(timer);
                return null;
            }
            CraftingTimerComponent result = null;
            timer.CallComponents((CraftingTimerComponent component) => {
                result = component;
            });
            return result;
        }

        private static void RenderCraftSection() {
            var caster = GetSelectedCaster(false);
            if (caster == null) {
                return;
            }
            var itemTypes = itemCraftingData.Where(data => settings.IgnoreCraftingFeats || CasterHasFeat(caster, data.FeatGuid)).ToArray();
            if (!Enumerable.Any(itemTypes)) {
                RenderLabel($"{caster.CharacterName} does not know any crafting feats.");
                return;
            }
            var itemTypeNames = itemTypes.Select(data => new L10NString(data.NameId).ToString()).ToArray();
            RenderSelection(ref selectedItemTypeIndex, "Crafting: ", itemTypeNames, 8, ref selectedCustomName);
            var craftingData = itemTypes[selectedItemTypeIndex];
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                RenderSpellBasedCrafting(caster, spellBased);
            } else {
                RenderRecipeBasedCrafting(caster, craftingData as RecipeBasedItemCraftingData);
            }
            RenderLabel($"Current Money: {Game.Instance.Player.Money}");
        }

        private static void RenderSpellBasedCrafting(UnitEntityData caster, SpellBasedItemCraftingData craftingData) {
            var spellbooks = caster.Descriptor.Spellbooks.Where(book => book.CasterLevel > 0).ToList();
            if (spellbooks.Count == 0) {
                RenderLabel($"{caster.CharacterName} is not yet able to cast spells.");
            } else if (spellbooks.Count == 1) {
                selectedSpellbookIndex = 0;
            } else {
                var spellBookNames = spellbooks.Select(book => book.Blueprint.Name.ToString()).ToArray();
                RenderSelection(ref selectedSpellbookIndex, "Class: ", spellBookNames, 10);
            }
            var spellbook = spellbooks[selectedSpellbookIndex];
            var maxLevel = Math.Min(spellbook.MaxSpellLevel, craftingData.MaxSpellLevel);
            var spellLevelNames = Enumerable.Range(0, maxLevel + 1).Select(index => $"Level {index}").ToArray();
            RenderSelection(ref selectedSpellLevelIndex, "Select spell level: ", spellLevelNames, 10);
            var spellLevel = selectedSpellLevelIndex;
            if (spellLevel > 0 && !spellbook.Blueprint.Spontaneous) {
                if (settings.CraftingTakesNoTime) {
                    selectedShowPreparedSpells = true;
                } else {
                    GUILayout.BeginHorizontal();
                    selectedShowPreparedSpells = GUILayout.Toggle(selectedShowPreparedSpells, " Show prepared spells only");
                    GUILayout.EndHorizontal();
                }
            } else {
                selectedShowPreparedSpells = false;
            }
            var spellOptions = spellbook.GetSpecialSpells(spellLevel);
            if (selectedShowPreparedSpells) {
                // Prepared spellcaster
                spellOptions = spellOptions.Concat(spellbook.GetMemorizedSpells(spellLevel).Where(slot => slot.Spell != null && slot.Available)
                    .Select(slot => slot.Spell));
            } else {
                // Cantrips/Orisons or Spontaneous spellcaster or showing all known spells
                if (spellLevel > 0 && spellbook.Blueprint.Spontaneous) {
                    var spontaneousSlots = spellbook.GetSpontaneousSlots(spellLevel);
                    RenderLabel($"{caster.CharacterName} can cast {spontaneousSlots} more level {spellLevel} spells today.");
                    if (spontaneousSlots == 0 && settings.CraftingTakesNoTime) {
                        return;
                    }
                }
                spellOptions = spellOptions.Concat(spellbook.GetKnownSpells(spellLevel));
            }
            if (!spellOptions.Any()) {
                RenderLabel($"{caster.CharacterName} does not know any level {spellLevel} spells.");
            } else {
                var minCasterLevel = Math.Max(1, 2 * spellLevel - 1);
                if (minCasterLevel < spellbook.CasterLevel) {
                    selectedCasterLevel = Mathf.RoundToInt(Mathf.Clamp(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Caster level: ", GUILayout.ExpandWidth(false));
                    selectedCasterLevel =
                        Mathf.RoundToInt(GUILayout.HorizontalSlider(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel, GUILayout.Width(300)));
                    GUILayout.Label($"{selectedCasterLevel}", GUILayout.ExpandWidth(false));
                    GUILayout.EndHorizontal();
                } else {
                    selectedCasterLevel = minCasterLevel;
                    RenderLabel($"Caster level: {selectedCasterLevel}");
                }
                foreach (var spell in spellOptions.OrderBy(spell => spell.Name).Distinct()) {
                    if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty) {
                        GUILayout.Label($"Cannot craft {new L10NString(craftingData.NameId)} of {spell.Name} with metamagic applied.");
                    } else if (spell.Blueprint.HasVariants) {
                        // Spells with choices (e.g. Protection from Alignment, which can be Protection from Evil, Good, Chaos or Law)
                        foreach (var variant in spell.Blueprint.Variants) {
                            RenderSpellBasedCraftItemControl(caster, craftingData, spell, variant, spellLevel, selectedCasterLevel);
                        }
                    } else {
                        RenderSpellBasedCraftItemControl(caster, craftingData, spell, spell.Blueprint, spellLevel, selectedCasterLevel);
                    }
                }
            }
        }

        private static string GetSlotStringKey(ItemsFilter.ItemType slot) {
            switch (slot) {
                case ItemsFilter.ItemType.Weapon:
                    return "e5e94f49-4bf6-4813-b4d7-8e4e9ede3d11";
                case ItemsFilter.ItemType.Shield:
                    return "dfa95469-ed91-4fc6-b5ef-89a466c50d71";
                case ItemsFilter.ItemType.Armor:
                    return "99d4ca00-bf3d-4d42-bf9c-aac1f4a407d6";
                case ItemsFilter.ItemType.Ring:
                    return "04d0daf3-ba89-44d5-8b6e-84b544e6748d";
                case ItemsFilter.ItemType.Belt:
                    return "ec07d8b6-9fca-4ba2-82b6-053e84ca9875";
                case ItemsFilter.ItemType.Feet:
                    return "1ea53023-2fd8-4fd7-a5ca-99cbe0d91728";
                case ItemsFilter.ItemType.Gloves:
                    return "628bab11-aeaf-449d-859e-3ccfeb25ebeb";
                case ItemsFilter.ItemType.Head:
                    return "45aa1b41-2392-4bc5-8e9b-400c5926cfce";
                case ItemsFilter.ItemType.Neck:
                    return "71cc03f0-aeb4-4c0b-b2da-9913b9cab8db";
                case ItemsFilter.ItemType.Shoulders:
                    return "823f1224-8a46-4a58-bcd6-2cce97cc1912";
                case ItemsFilter.ItemType.Wrist:
                    return "e43de05a-754c-4fa4-991d-0d33fcf1c767";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        private static string FindSupersededEnchantmentId(BlueprintItem item, string selectedEnchantmentId) {
            if (item != null) {
                var selectedRecipe = EnchantmentIdToRecipe[selectedEnchantmentId];
                foreach (var enchantment in item.Enchantments) {
                    if (EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid) && EnchantmentIdToRecipe[enchantment.AssetGuid] == selectedRecipe) {
                        return enchantment.AssetGuid;
                    }
                }
            }
            return null;
        }
        
        private static bool DoesItemMatchEnchantments(BlueprintItemEquipment item, string selectedEnchantmentId, BlueprintItemEquipment upgradeItem = null) {
            var isNotable = upgradeItem && upgradeItem.IsNotable;
            var ability = upgradeItem ? upgradeItem.Ability : null;
            var activatableAbility = upgradeItem ? upgradeItem.ActivatableAbility : null;
            // If item is notable or has an ability that upgradeItem does not, it's not a match.
            if (item.IsNotable != isNotable || item.Ability != ability || item.ActivatableAbility != activatableAbility) {
                return false;
            }
            var supersededEnchantmentId = FindSupersededEnchantmentId(upgradeItem, selectedEnchantmentId);
            var enchantmentCount = (upgradeItem ? upgradeItem.Enchantments.Count : 0) + (supersededEnchantmentId == null ? 1 : 0);
            if (item.Enchantments.Count != enchantmentCount) {
                return false;
            }
            foreach (var enchantment in item.Enchantments) {
                // If enchantment isn't the new enchantment, isn't superseded and doesn't exist in upgradeItem, then it doesn't match.
                if (enchantment.AssetGuid != selectedEnchantmentId && enchantment.AssetGuid != supersededEnchantmentId
                                                                     && (!upgradeItem || !upgradeItem.Enchantments.Contains(enchantment))) {
                    return false;
                }
            }
            return true;
        }
        
        private static IEnumerable<T> PrependConditional<T>(this IEnumerable<T> target, bool prepend, params T[] items) =>
            prepend ? items.Concat(target ?? throw new ArgumentException(nameof(target))) : target;

        private static void RenderRecipeBasedCrafting(UnitEntityData caster, RecipeBasedItemCraftingData craftingData) {
            // Choose slot/weapon type.
            if (craftingData.Slots.Length > 1) {
                var names = craftingData.Slots.Select(slot => new L10NString(GetSlotStringKey(slot)).ToString()).ToArray();
                RenderSelection(ref selectedItemSlotIndex, "Item type", names, 10, ref selectedCustomName);
            } else {
                selectedItemSlotIndex = 0;
            }
            var selectedSlot = craftingData.Slots[selectedItemSlotIndex];
            // Choose an existing item of that type, or create a new one (if allowed).
            // Allow them to select an existing item of the given type.
            var items = Game.Instance.Player.Inventory.Where(item =>
                item.Blueprint is BlueprintItemEquipment blueprint && blueprint.ItemType == selectedSlot
                                                                   && (item.Wielder == caster.Descriptor || item.Wielder == null)
                ).ToArray();
            var canCreateNew = craftingData.NewItemBaseIDs != null;
            var itemNames = items.Select(item => item.Name).PrependConditional(canCreateNew, new L10NString("craftMagicItems-label-craft-new-item")).ToArray();
            if (itemNames.Length == 0) {
                RenderLabel($"{caster.CharacterName} can not access any items of that type.");
                return;
            }
            RenderSelection(ref selectedUpgradeItemIndex, "Item: ", itemNames, 5, ref selectedCustomName);
            // See existing item details and enchantments.
            var index = selectedUpgradeItemIndex - (canCreateNew ? 1 : 0);
            var upgradeItem = index < 0 ? null : items[index];
            if (upgradeItem != null) {
                RenderLabel(upgradeItem.Description);
            }
            // Pick recipe to apply, but make any with the same ParentNameId appear in a second level menu under their parent name.
            var availableRecipes = craftingData.Recipes
                .Where(recipe => recipe.ParentNameId == null || recipe == craftingData.SubRecipes[recipe.ParentNameId][0])
                .OrderBy(recipe => new L10NString(recipe.ParentNameId ?? recipe.NameId).ToString())
                .ToArray();
            var recipeNames = availableRecipes.Select(recipe => new L10NString(recipe.ParentNameId ?? recipe.NameId).ToString()).ToArray();
            RenderSelection(ref selectedRecipeIndex, "Enchantment: ", recipeNames, 6, ref selectedCustomName);
            var selectedRecipe = availableRecipes[selectedRecipeIndex];
            if (selectedRecipe.ParentNameId != null) {
                var category = recipeNames[selectedRecipeIndex];
                var availableSubRecipes = craftingData.SubRecipes[selectedRecipe.ParentNameId]
                    .OrderBy(recipe => new L10NString(recipe.NameId).ToString())
                    .ToArray();
                recipeNames = availableSubRecipes.Select(recipe => new L10NString(recipe.NameId).ToString()).ToArray();
                RenderSelection(ref selectedSubRecipeIndex, category + ": ", recipeNames, 6, ref selectedCustomName);
                selectedRecipe = availableSubRecipes[selectedSubRecipeIndex];
            }
            // Pick specific enchantment from the recipe
            var availableEnchantments = selectedRecipe.Enchantments;
            var supersededEnchantment = upgradeItem != null ? FindSupersededEnchantmentId(upgradeItem.Blueprint, availableEnchantments[0].AssetGuid) : null;
            if (supersededEnchantment != null) {
                // Don't offer downgrade options.
                var found = false;
                availableEnchantments = availableEnchantments.Where(enchantment => {
                    if (found) {
                        return true;
                    }
                    if (enchantment.AssetGuid == supersededEnchantment) {
                        found = true;
                    }
                    return false;
                }).ToArray();
            }
            if (availableEnchantments.Length > 0) {
                var counter = selectedRecipe.Enchantments.Length - availableEnchantments.Length;
                var enchantmentNames = availableEnchantments.Select(enchantment => {
                    counter++;
                    return enchantment.Name.Empty() ? $"+{counter}" : enchantment.Name;
                });
                RenderSelection(ref selectedEnchantmentIndex, "", enchantmentNames.ToArray(), 6, ref selectedCustomName);
            } else {
                RenderLabel("This item cannot be further upgraded with this enchantment.");
                return;
            }
            var selectedEnchantment = availableEnchantments[selectedEnchantmentIndex];
            var casterLevel = selectedRecipe.CasterLevelStart + selectedRecipe.Enchantments.IndexOf(selectedEnchantment) * selectedRecipe.CasterLevelMultiplier;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prerequisites: ", GUILayout.ExpandWidth(false));
            var prerequisites =
                selectedRecipe.Prerequisites.Aggregate($"{CasterLevelLocalized} {casterLevel}", (current, prerequisite) => current + $", {prerequisite.Name}");
            GUILayout.Label(prerequisites, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            // See if the selected enchantment corresponds to a vanilla blueprint.
            var allItemBlueprintsWithEnchantment = upgradeItem == null ? FindItemBlueprintForEnchantmentId(selectedEnchantment.AssetGuid) : null;
            var matchingItem = allItemBlueprintsWithEnchantment?.FirstOrDefault(blueprint =>
                blueprint.ItemType == selectedSlot && DoesItemMatchEnchantments(blueprint, selectedEnchantment.AssetGuid)
            );
            if (matchingItem) {
                // Crafting an existing blueprint.
                RenderRecipeBasedCraftItemControl(caster, craftingData, selectedRecipe, casterLevel, matchingItem, upgradeItem);
            } else if (upgradeItem != null) {
                // Upgrading to a custom blueprint
                RenderCustomNameField(upgradeItem.Blueprint.Name);
                var supersededEnchantmentId = FindSupersededEnchantmentId(upgradeItem.Blueprint, selectedEnchantment.AssetGuid);
                var customRecipeItemGuid = BuildCustomRecipeItemGuid(upgradeItem.Blueprint.AssetGuid, new List<string> {selectedEnchantment.AssetGuid},
                    supersededEnchantmentId == null ? null : new List<string> {supersededEnchantmentId}, selectedCustomName == upgradeItem.Blueprint.Name ? null : selectedCustomName);
                var customItem = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(customRecipeItemGuid);
                RenderRecipeBasedCraftItemControl(caster, craftingData, selectedRecipe, casterLevel, customItem, upgradeItem);
            } else {
                // Crafting a new custom blueprint from scratch.
                BlueprintItemEquipment baseBlueprint;
                if (selectedBaseGuid != null) {
                    baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                    if (!baseBlueprint || baseBlueprint.ItemType != selectedSlot) {
                        selectedBaseGuid = null;
                    }
                }
                selectedBaseGuid = selectedBaseGuid ?? RandomBaseBlueprintId(craftingData,
                    guid => ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(guid)?.ItemType == selectedSlot);
                baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                RenderCustomNameField($"{new L10NString(selectedRecipe.NameId)} {new L10NString(GetSlotStringKey(selectedSlot))}");
                var customRecipeItemGuid = BuildCustomRecipeItemGuid(selectedBaseGuid, new List<string> {selectedEnchantment.AssetGuid},
                    baseBlueprint ? baseBlueprint.Enchantments.Select(enchantment => enchantment.AssetGuid) : null, selectedCustomName, "null", "null");
                var customItem = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(customRecipeItemGuid);
                if (customItem == null) {
                    RenderLabel($"Null custom item from looking up blueprint ID {customRecipeItemGuid}");
                } else {
                    RenderRecipeBasedCraftItemControl(caster, craftingData, selectedRecipe, casterLevel, customItem);
                }
            }
        }

        private static int GetMaterialComponentMultiplier(ItemCraftingData craftingData) {
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                return spellBased.Charges;
            } else {
                return 0;
            }
        }
        
        private static void RenderProjectsSection() {
            var caster = GetSelectedCaster(false);
            if (caster == null) {
                return;
            }
            var craftingProjects = GetCraftingTimerComponentForCaster(caster.Descriptor);
            if (craftingProjects == null || craftingProjects.CraftingProjects.Count == 0) {
                RenderLabel($"{caster.CharacterName} is not currently working on any crafting projects.");
                return;
            }
            RenderLabel($"{caster.CharacterName} currently has {craftingProjects.CraftingProjects.Count} crafting projects in progress.");
            var firstItem = true;
            foreach (var project in craftingProjects.CraftingProjects.ToArray()) {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"   {project.ItemBlueprint.Name} is {100 * project.Progress / project.TargetCost}% complete.  {project.LastMessage}", GUILayout.Width(600f));
                if (GUILayout.Button("<color=red>✖</color>", GUILayout.ExpandWidth(false))) {
                    craftingProjects.CraftingProjects.Remove(project);
                    // Refund gold and material components.  Yes, the player could exploit this by toggling "crafting costs no gold" between
                    // queueing up a crafting project and cancelling it, but there are much more direct ways to cheat with mods, so I don't
                    // particularly care.
                    if (!settings.CraftingCostsNoGold) {
                        Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                        Game.Instance.Player.GainMoney(project.TargetCost);
                        var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
                        BuildCostString(out var cost, craftingData, project.TargetCost, project.Prerequisites);
                        var factor = GetMaterialComponentMultiplier(craftingData);
                        if (factor > 0) {
                            foreach (var prerequisiteSpell in project.Prerequisites) {
                                if (prerequisiteSpell.MaterialComponent.Item) {
                                    var number = prerequisiteSpell.MaterialComponent.Count * factor;
                                    Game.Instance.Player.Inventory.Add(prerequisiteSpell.MaterialComponent.Item, number);
                                }
                            }
                        }
                        AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-crafting-cancelled"), project.ItemBlueprint.Name, cost));
                    }

                }
                if (firstItem) {
                    firstItem = false;
                } else if (GUILayout.Button("Move To Top", GUILayout.ExpandWidth(false))) {
                    craftingProjects.CraftingProjects.Remove(project);
                    craftingProjects.CraftingProjects.Insert(0, project);
                }
                GUILayout.EndHorizontal();
            }
        }

        private static void RenderFeatReassignmentSection() {
            var caster = GetSelectedCaster(false);
            if (caster == null) {
                return;
            }
            var missingFeats = itemCraftingData.Where(data => !CasterHasFeat(caster, data.FeatGuid)).ToArray();
            if (missingFeats.Length == 0) {
                RenderLabel($"{caster.CharacterName} already knows all crafting feats.");
                return;
            }
            RenderLabel("Use this section to reassign previous feat choices for this character to crafting feats.  <color=red>Warning:</color> This is a one-way assignment!");
            RenderSelection(ref selectedFeatToLearn, "Crafting Feat to learn",
                missingFeats.Select(data => new L10NString(data.NameId).ToString()).ToArray(), 8);
            var learnFeatData = missingFeats[selectedFeatToLearn];
            var learnFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(learnFeatData.FeatGuid);
            var removedFeatIndex = 0;
            foreach (var feature in caster.Descriptor.Progression.Features) {
                if (!feature.Blueprint.HideInUI && feature.Blueprint.HasGroup(CraftingFeatGroups)
                                                && (feature.SourceProgression != null || feature.SourceRace != null)) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Feat: {feature.Name}", GUILayout.ExpandWidth(false));
                    if (GUILayout.Button($"<- {learnFeat.Name}", GUILayout.ExpandWidth(false))) {
                        var currentRank = feature.Rank;
                        caster.Descriptor.Progression.ReplaceFeature(feature.Blueprint, learnFeat);
                        if (currentRank == 1) {
                            foreach (var addFact in feature.SelectComponents((AddFacts addFacts) => true)) {
                                addFact.OnFactDeactivate();
                            }
                            caster.Descriptor.Progression.Features.RemoveFact(feature);
                        }
                        var addedFeature = caster.Descriptor.Progression.Features.AddFeature(learnFeat);
                        addedFeature.Source = feature.Source;
                        var mFacts = Traverse.Create(caster.Descriptor.Progression.Features).Field("m_Facts").GetValue<List<Fact>>();
                        if (removedFeatIndex < mFacts.Count) {
                            // Move the new feat to the place in the list originally occupied by the removed one.
                            mFacts.Remove(addedFeature);
                            mFacts.Insert(removedFeatIndex, addedFeature);
                        }
                        ActionBarManager.Instance.HandleAbilityRemoved(null);
                    }
                    GUILayout.EndHorizontal();
                }
                removedFeatIndex++;
            }
        }

        private static void RenderCheatsSection() {
            RenderCheckbox(ref settings.CraftingCostsNoGold, "Crafting costs no gold and no material components.");
            RenderCheckbox(ref settings.IgnoreCraftingFeats, "Crafting does not require characters to take crafting feats.");
            RenderCheckbox(ref settings.CraftingTakesNoTime, "Crafting takes no time to complete.");
            RenderCheckbox(ref settings.CraftAtFullSpeedWhileAdventuring, "Characters craft at full speed while adventuring (instead of 25% speed)");

        }

        private static void RenderLabel(string label) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.EndHorizontal();
        }

        private static bool IsPlayerInCapital() {
            // Detect if the player is in the capital, or in kingdom management from the throne room.
            return (Game.Instance.CurrentlyLoadedArea != null && Game.Instance.CurrentlyLoadedArea.IsCapital) ||
                   (Game.Instance.CurrentMode == GameModeType.Kingdom && KingdomTimelineManager.CanAdvanceTime());
        }

        private static UnitEntityData GetSelectedCaster(bool render) {
            // Only allow remote companions if the player is in the capital.
            var remote = IsPlayerInCapital();
            var partySpellCasters = UIUtility.GetGroup(remote).Where(character => character.IsPlayerFaction
                                                      && !character.Descriptor.IsPet
                                                      && character.Descriptor.Spellbooks != null
                                                      && character.Descriptor.Spellbooks.Any()
                                                      && !character.Descriptor.State.IsFinallyDead)
                                                      .ToArray();
            if (partySpellCasters.Length == 0) {
                if (render) {
                    RenderLabel("No characters with spells available.");
                }
                return null;
            }

            if (render) {
                var partyNames = partySpellCasters.Select(entity => entity.CharacterName).ToArray();
                RenderSelection(ref selectedSpellcasterIndex, "Caster: ", partyNames, 8);
            }
            return partySpellCasters[selectedSpellcasterIndex];
        }

        private static void RenderSelection(ref int index, string label, string[] options, int xCount) {
            var dummy = "";
            RenderSelection(ref index, label, options, xCount, ref dummy);
        }

        private static void RenderSelection(ref int index, string label, string[] options, int xCount, ref string emptyOnChange) {
            if (index >= options.Length) {
                index = 0;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            var newIndex = GUILayout.SelectionGrid(index, options, xCount);
            if (index != newIndex) {
                emptyOnChange = "";
            }
            index = newIndex;
            GUILayout.EndHorizontal();
        }

        private static void RenderCustomNameField(string defaultValue) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name: ", GUILayout.ExpandWidth(false));
            if (string.IsNullOrEmpty(selectedCustomName)) {
                selectedCustomName = defaultValue;
            }
            selectedCustomName = GUILayout.TextField(selectedCustomName, GUILayout.Width(300));
            GUILayout.EndHorizontal();
        }

        private static void RenderCheckbox(ref bool value, string label) {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"{(value ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>")} {label}", GUILayout.ExpandWidth(false))) {
                value = !value;
            }
            GUILayout.EndHorizontal();
        }

        private static void AddItemBlueprintForSpell(UsableItemType itemType, BlueprintItemEquipment itemBlueprint) {
            if (!SpellIdToItem.ContainsKey(itemType)) {
                SpellIdToItem.Add(itemType, new Dictionary<string, List<BlueprintItemEquipment>>());
            }
            if (!SpellIdToItem[itemType].ContainsKey(itemBlueprint.Ability.AssetGuid)) {
                SpellIdToItem[itemType][itemBlueprint.Ability.AssetGuid] = new List<BlueprintItemEquipment>();
            }
            SpellIdToItem[itemType][itemBlueprint.Ability.AssetGuid].Add(itemBlueprint);
        }

        private static List<BlueprintItemEquipment> FindItemBlueprintsForSpell(BlueprintScriptableObject spell, UsableItemType itemType) {
            if (!SpellIdToItem.ContainsKey(itemType)) {
                var allUsableItems = Resources.FindObjectsOfTypeAll<BlueprintItemEquipmentUsable>();
                foreach (var item in allUsableItems) {
                    if (item.Type == itemType) {
                        AddItemBlueprintForSpell(itemType, item);
                    }
                }
            }
            return SpellIdToItem[itemType].ContainsKey(spell.AssetGuid) ? SpellIdToItem[itemType][spell.AssetGuid] : null;
        }

        private static void AddItemIdForEnchantment(BlueprintItemEquipment itemBlueprint) {
            foreach (var enchantment in itemBlueprint.Enchantments) {
                if (!EnchantmentIdToItem.ContainsKey(enchantment.AssetGuid)) {
                    EnchantmentIdToItem[enchantment.AssetGuid] = new List<BlueprintItemEquipment>();
                }
                EnchantmentIdToItem[enchantment.AssetGuid].Add(itemBlueprint);
            }
        }

        private static IEnumerable<BlueprintItemEquipment> FindItemBlueprintForEnchantmentId(string assetGuid) {
            return EnchantmentIdToItem.ContainsKey(assetGuid) ? EnchantmentIdToItem[assetGuid] : null;
        }
        
        private static bool CasterHasFeat(UnitEntityData caster, string featGuid) {
            var feat = ResourcesLibrary.TryGetBlueprint(featGuid) as BlueprintFeature;
            foreach (var feature in caster.Descriptor.Progression.Features) {
                if (feature.Blueprint == feat) {
                    return true;
                }
            }
            return false;
        }

        private static string RandomBaseBlueprintId(ItemCraftingData itemData, Func<string, bool> selector = null) {
            var blueprintIds = selector == null ? itemData.NewItemBaseIDs : itemData.NewItemBaseIDs.Where(selector).ToArray();
            return blueprintIds[RandomGenerator.Next(blueprintIds.Length)];
        }

        private static void CraftItem(BlueprintItem blueprint, string typeName) {
            var craftingData = itemCraftingData.First(data => data.Name == typeName);
            var item = blueprint.CreateEntity();
            item.IsIdentified = true; // Mark the item as identified.
            Game.Instance.Player.Inventory.Add(item);
            if (craftingData is SpellBasedItemCraftingData fromSpellData) {
                item.Charges = fromSpellData.Charges; // Set the charges, since wand blueprints have random values.
                switch (fromSpellData.UsableItemType) {
                    case UsableItemType.Scroll:
                        Game.Instance.UI.Common.UISound.Play(UISoundType.NewInformation);
                        break;
                    case UsableItemType.Potion:
                        Game.Instance.UI.Common.UISound.PlayItemSound(SlotAction.Take, item, false);
                        break;
                    default:
                        Game.Instance.UI.Common.UISound.Play(UISoundType.SettlementBuildStart);
                        break;
                }
            } else {
                Game.Instance.UI.Common.UISound.Play(UISoundType.SettlementBuildStart);
            }
        }

        private static int CalculateSpellBasedGoldCost(SpellBasedItemCraftingData craftingData, int spellLevel, int casterLevel) {
            return spellLevel == 0 ? craftingData.BaseItemGoldCost * casterLevel / 8 : craftingData.BaseItemGoldCost * spellLevel * casterLevel / 4;
        }
        
        private static bool BuildCostString(out string cost, ItemCraftingData craftingData, int goldCost, params BlueprintAbility[] spellBlueprintArray) {
            var canAfford = true;
            if (settings.CraftingCostsNoGold) {
                cost = new L10NString("craftMagicItems-label-cost-free");
            } else {
                canAfford = (Game.Instance.Player.Money >= goldCost);
                var notAffordGold = canAfford ? "" : new L10NString("craftMagicItems-label-cost-gold-too-much");
                cost = string.Format(new L10NString("craftMagicItems-label-cost-gold"), goldCost, notAffordGold);
                var itemTotals = new Dictionary<BlueprintItem, int>();
                foreach (var spellBlueprint in spellBlueprintArray) {
                    if (spellBlueprint.MaterialComponent.Item) {
                        var count = spellBlueprint.MaterialComponent.Count * GetMaterialComponentMultiplier(craftingData);
                        if (count > 0) {
                            if (itemTotals.ContainsKey(spellBlueprint.MaterialComponent.Item)) {
                                itemTotals[spellBlueprint.MaterialComponent.Item] += count;
                            } else {
                                itemTotals[spellBlueprint.MaterialComponent.Item] = count;
                            }
                        }
                    }
                }
                foreach (var pair in itemTotals) {
                    var notAffordItems = "";
                    if (!Game.Instance.Player.Inventory.Contains(pair.Key, pair.Value)) {
                        canAfford = false;
                        notAffordItems = new L10NString("craftMagicItems-label-cost-items-too-much");
                    }
                    cost += string.Format(new L10NString("craftMagicItems-label-cost-gold-and-items"), pair.Value,
                        pair.Key.Name, notAffordItems);
                }
            }
            return canAfford;
        }

        private static void RenderSpellBasedCraftItemControl(UnitEntityData caster, SpellBasedItemCraftingData craftingData, AbilityData spell, BlueprintAbility spellBlueprint, int spellLevel, int casterLevel) {
            var itemBlueprintList = FindItemBlueprintsForSpell(spellBlueprint, craftingData.UsableItemType);
            if (itemBlueprintList == null && craftingData.NewItemBaseIDs == null) {
                GUILayout.Label(string.Format(new L10NString("craftMagicItems-label-no-item-exists"), new L10NString(craftingData.NamePrefixId),
                    spellBlueprint.Name));
                return;
            }
            var existingItemBlueprint = itemBlueprintList?.Find(bp => bp.SpellLevel == spellLevel && bp.CasterLevel == casterLevel);
            var goldCost = CalculateSpellBasedGoldCost(craftingData, spellLevel, casterLevel);
            var canAfford = BuildCostString(out var cost, craftingData, goldCost, spellBlueprint);
            var custom = (existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength)
                ? new L10NString("craftMagicItems-label-custom").ToString() : "";
            var label = string.Format(new L10NString("craftMagicItems-label-craft-spell-item"), custom, new L10NString(craftingData.NamePrefixId),
                spellBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                BlueprintItem itemBlueprint;
                if (itemBlueprintList == null) {
                    // No items for that spell exist at all - create a custom blueprint with casterLevel, spellLevel and spellId
                    var blueprintId = BuildCustomSpellItemGuid(RandomBaseBlueprintId(craftingData), casterLevel, spellLevel, spell.Blueprint.AssetGuid);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else if (existingItemBlueprint == null) {
                    // No item for this spell & caster level - create a custom blueprint with casterLevel and optionally SpellLevel
                    var blueprintId = BuildCustomSpellItemGuid(itemBlueprintList[0].AssetGuid, casterLevel, itemBlueprintList[0].SpellLevel == spellLevel ? -1 : spellLevel);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else {
                    // Item with matching spell, level and caster level exists.  Use that.
                    itemBlueprint = existingItemBlueprint;
                }
                if (itemBlueprint == null) {
                    throw new Exception(
                        $"Unable to build blueprint for spellId {spell.Blueprint.AssetGuid}, spell level {spellLevel}, caster level {casterLevel}");
                }
                // Pay gold and material components up front.
                if (!settings.CraftingCostsNoGold) {
                    Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                    AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-begin-crafting"), cost, itemBlueprint.Name), itemBlueprint.CreateEntity());
                    Game.Instance.Player.SpendMoney(goldCost);
                    if (spell.RequireMaterialComponent) {
                        Game.Instance.Player.Inventory.Remove(spellBlueprint.MaterialComponent.Item, spellBlueprint.MaterialComponent.Count * craftingData.Charges);
                    }
                }
                if (settings.CraftingTakesNoTime) {
                    spell.SpendFromSpellbook();
                    CraftItem(itemBlueprint, craftingData.Name);
                } else {
                    var craftingProjects = GetCraftingTimerComponentForCaster(caster.Descriptor, true);
                    GameLogContext.Count = (goldCost + CraftingProgressPerDay - 1) / CraftingProgressPerDay;
                    craftingProjects.AddProject(new CraftingProjectData(goldCost, casterLevel, itemBlueprint, craftingData.Name, new [] { spell.Blueprint },
                        new L10NString("craftMagicItems-startMessage")));
                    currentSection = OpenSection.ProjectsSection;
                }
            }
        }

        private static void RenderRecipeBasedCraftItemControl(UnitEntityData caster, ItemCraftingData craftingData, RecipeData recipe, int casterLevel, BlueprintItem itemBlueprint, ItemEntity upgradeItem = null) {
            var goldCost = (itemBlueprint.Cost - (upgradeItem?.Blueprint.Cost ?? 0)) / 4;
            var canAfford = BuildCostString(out var cost, craftingData, goldCost, recipe.Prerequisites);
            var custom = (itemBlueprint.AssetGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength)
                ? new L10NString("craftMagicItems-label-custom").ToString() : "";
            var label = upgradeItem == null
                ? string.Format(new L10NString("craftMagicItems-label-craft-item"), custom, itemBlueprint.Name, cost)
                : string.Format(new L10NString("craftMagicItems-label-upgrade-item"), upgradeItem.Blueprint.Name, custom, itemBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                // Pay gold and material components up front.
                if (!settings.CraftingCostsNoGold) {
                    Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                    AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-begin-crafting"), cost, itemBlueprint.Name), itemBlueprint.CreateEntity());
                    Game.Instance.Player.SpendMoney(goldCost);
                    var factor = GetMaterialComponentMultiplier(craftingData);
                    if (factor > 0) {
                        foreach (var prerequisite in recipe.Prerequisites) {
                            if (prerequisite.MaterialComponent.Item != null) {
                                Game.Instance.Player.Inventory.Remove(prerequisite.MaterialComponent.Item, prerequisite.MaterialComponent.Count * factor);
                            }
                        }
                    }
                }
                if (settings.CraftingTakesNoTime) {
                    if (upgradeItem != null) {
                        Game.Instance.Player.Inventory.Remove(upgradeItem);
                    }
                    CraftItem(itemBlueprint, craftingData.Name);
                } else {
                    var craftingProjects = GetCraftingTimerComponentForCaster(caster.Descriptor, true);
                    GameLogContext.Count = (goldCost + CraftingProgressPerDay - 1) / CraftingProgressPerDay;
                    craftingProjects.AddProject(new CraftingProjectData(goldCost, casterLevel, itemBlueprint, craftingData.Name, recipe.Prerequisites,
                        new L10NString("craftMagicItems-startMessage")));
                    currentSection = OpenSection.ProjectsSection;
                }
            }
        }

        private static LocalizedString BuildCustomRecipeItemDescription(BlueprintItem blueprint) {
            var description = new L10NString("craftMagicItems-custom-description-start").ToString();
            foreach (var enchantment in blueprint.Enchantments) {
                description += "\n * ";
                if (!string.IsNullOrEmpty(enchantment.Name)) {
                    description += enchantment.Name;
                } else if (EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid)) {
                    var recipe = EnchantmentIdToRecipe[enchantment.AssetGuid];
                    var index = recipe.Enchantments.IndexOf(enchantment);
                    description += string.Format(new L10NString("craftMagicItems-custom-description-enchantment-template").ToString(), index + 1,
                        new L10NString(recipe.BonusTypeId), BonusLocalized, new L10NString(recipe.NameId));
                }
            }
            return new FakeL10NString(description);
        }

        private static string BuildCustomSpellItemGuid(string originalGuid, int casterLevel, int spellLevel = -1, string spellId = null) {
            if (originalGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength) {
                // Check if GUID is already customised by this mod
                var match = BlueprintRegex.Match(originalGuid);
                if (match.Success && match.Groups["casterLevel"].Success) {
                    // Remove the existing customisation
                    originalGuid = originalGuid.Substring(0, match.Index) + originalGuid.Substring(match.Index + match.Length);
                    // Use any values which aren't explicitly overridden
                    if (spellLevel == -1 && match.Groups["spellLevelMatch"].Success) {
                        spellLevel = int.Parse(match.Groups["spellLevel"].Value);
                    }
                    if (spellId == null && match.Groups["spellIdMatch"].Success) {
                        spellId = match.Groups["spellId"].Value;
                    }
                }
            }
            var spellLevelString = (spellLevel == -1 ? "" : $",SL={spellLevel}");
            var spellIdString = (spellId == null ? "" : $",spellId=({spellId})");
            return $"{originalGuid}{BlueprintPrefix}(CL={casterLevel}{spellLevelString}{spellIdString})";
        }

        private static string BuildCustomRecipeItemGuid(string originalGuid, IEnumerable<string> enchantments, IEnumerable<string> remove = null, string name = null, string ability = null, string activatableAbility = null) {
            if (originalGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength) {
                // Check if GUID is already customised by this mod
                var match = BlueprintRegex.Match(originalGuid);
                if (match.Success && match.Groups["enchantments"].Success) {
                    var enchantmentsList = enchantments.Concat(match.Groups["enchantments"].Value.Split(';')).Distinct().ToList();
                    var removeList = match.Groups["remove"].Success
                        ? (remove ?? new List<string>()).Concat(match.Groups["remove"].Value.Split(';')).Distinct().ToList()
                        : remove?.ToList();
                    if (removeList != null) {
                        foreach (var guid in removeList.ToArray()) {
                            if (enchantmentsList.Contains(guid)) {
                                enchantmentsList.Remove(guid);
                                removeList.Remove(guid);
                            }
                        }
                        if (enchantmentsList.Count == 0) {
                            return originalGuid;
                        }
                    }
                    enchantments = enchantmentsList;
                    remove = removeList?.Count == 0 ? null : removeList;
                    if (name == null && match.Groups["name"].Success) {
                        name = match.Groups["name"].Value;
                    }
                    if (ability == null && match.Groups["ability"].Success) {
                        ability = match.Groups["ability"].Value;
                    }
                    if (activatableAbility == null && match.Groups["activatableAbility"].Success) {
                        activatableAbility = match.Groups["activatableAbility"].Value;
                    }
                    // Remove and original customisation.
                    originalGuid = originalGuid.Substring(0, match.Index) + originalGuid.Substring(match.Index + match.Length);
                }
            }
            return $"{originalGuid}{BlueprintPrefix}(enchantments=({enchantments.Join(null, ";")})" +
                   $"{(remove == null ? "" : ",remove="+remove.Join(null, ";"))}" +
                   $"{(name == null ? "" : $",name={name.Replace('✔', '_')}✔")}" +
                   $"{(ability == null ? "" : $",ability={ability}")}" +
                   $"{(activatableAbility == null ? "" : $",activatableAbility={activatableAbility}")})";
        }

        private static string BuildCustomComponentsItemGuid(string originalGuid, params string[] values) {
            var components = "";
            for (var index = 0; index < values.Length; index += 3) {
                components += $"{(index > 0 ? "," : "")}Component[{values[index]}].{values[index + 1]}={values[index + 2]}";
            }
            return $"{originalGuid}{BlueprintPrefix}({components})";
        }
        
        private static string BuildCustomFeatGuid(string originalGuid, string feat) {
            return $"{originalGuid}{BlueprintPrefix}(feat={feat})";
        }

        private static string ApplyTimerBlueprintPatch(BlueprintBuff blueprint) {
            blueprint.ComponentsArray = new BlueprintComponent[] { ScriptableObject.CreateInstance<CraftingTimerComponent>() };
            Traverse.Create(blueprint).Field("m_Flags").SetValue(2 + 8); // BlueprintBluff.Flags enum is private.  Values are HiddenInUi = 2 + StayOnDeath = 8
            blueprint.FxOnStart = new PrefabLink();
            blueprint.FxOnRemove = new PrefabLink();
            // Set the display name - it's hidden in the UI, but someone might find it in Bag of Tricks.
            Traverse.Create(blueprint).Field("m_DisplayName").SetValue(new L10NString("craftMagicItems-buff-name"));
            return TimerBlueprintGuid;
        }

        private static string ApplyFeatBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
            var feat = match.Groups["feat"].Value;
            Traverse.Create(blueprint).Field("m_DisplayName").SetValue(new L10NString($"craftMagicItems-feat-{feat}-displayName"));
            Traverse.Create(blueprint).Field("m_Description").SetValue(new L10NString($"craftMagicItems-feat-{feat}-description"));
            var icon = Image2Sprite.Create($"{ModEntry.Path}/Icons/craft-{feat}.png");
            Traverse.Create(blueprint).Field("m_Icon").SetValue(icon);
            blueprint.ComponentsArray = null;
            return BuildCustomFeatGuid(blueprint.AssetGuid, feat);
        }

        private static string ApplySpellItemBlueprintPatch(BlueprintItemEquipmentUsable blueprint, Match match) {
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
                blueprint.Ability = (BlueprintAbility)ResourcesLibrary.TryGetBlueprint(spellId);
                blueprint.DC = 0;
            }
            if (blueprint.Ability != null && blueprint.Ability.LocalizedSavingThrow != null && blueprint.Ability.LocalizedSavingThrow.IsSet()) {
                blueprint.DC = 10 + spellLevel * 3 / 2;
            }
            Traverse.Create(blueprint).Field("m_Cost").SetValue(0); // Allow the game to auto-calculate the cost
            // Also store the new item blueprint in our spell-to-item lookup dictionary.
            var itemBlueprintsForSpell = FindItemBlueprintsForSpell(blueprint.Ability, blueprint.Type);
            if (itemBlueprintsForSpell == null || !itemBlueprintsForSpell.Contains(blueprint)) {
                AddItemBlueprintForSpell(blueprint.Type, blueprint);
            }
            return BuildCustomSpellItemGuid(blueprint.AssetGuid, casterLevel, spellLevel, spellId);
        }

        private static int RulesRecipeItemCost(BlueprintItem blueprint) {
            var mostExpensiveEnchantmentCost = 0;
            var cost = 0;
            foreach (var enchantment in blueprint.Enchantments) {
                var enchantmentCost = EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid) ? EnchantmentIdToCost[enchantment.AssetGuid] : 0;
                cost += enchantmentCost;
                if (mostExpensiveEnchantmentCost < enchantmentCost) {
                    mostExpensiveEnchantmentCost = enchantmentCost;
                }
            }
            return (3 * cost - mostExpensiveEnchantmentCost) / 2;
        }
        
        private static string ApplyRecipeItemBlueprintPatch(BlueprintItemEquipment blueprint, Match match) {
            var priceDelta = blueprint.Cost - RulesRecipeItemCost(blueprint);
            // Ensure Enchantments is not shared with base blueprint
            var enchantmentsCopy = blueprint.Enchantments.ToList();
            Traverse.Create(blueprint).Field("m_CachedEnchantments").SetValue(enchantmentsCopy);
            var enchantments = match.Groups["enchantments"].Value.Split(';');
            foreach (var guid in enchantments) {
                var enchantment = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(guid);
                if (!enchantment) {
                    throw new Exception($"Failed to load enchantment {guid}");
                }
                blueprint.Enchantments.Add(enchantment);
            }
            string[] remove = null;
            if (match.Groups["remove"].Success) {
                remove = match.Groups["remove"].Value.Split(';');
                foreach (var guid in remove) {
                    var enchantment = ResourcesLibrary.TryGetBlueprint<BlueprintItemEnchantment>(guid);
                    if (!enchantment) {
                        throw new Exception($"Failed to load enchantment {guid}");
                    }
                    blueprint.Enchantments.Remove(enchantment);
                }
            }
            string ability = null;
            if (match.Groups["ability"].Success) {
                ability = match.Groups["ability"].Value;
                blueprint.Ability = ability == "null" ? null : ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(ability);
            }
            string activatableAbility = null;
            if (match.Groups["activatableAbility"].Success) {
                activatableAbility = match.Groups["activatableAbility"].Value;
                blueprint.ActivatableAbility = activatableAbility == "null"
                    ? null
                    : ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(activatableAbility);
            }
            string name = null;
            if (match.Groups["name"].Success) {
                name = match.Groups["name"].Value;
                Traverse.Create(blueprint).Field("m_DisplayNameText").SetValue(new FakeL10NString(name));
                Traverse.Create(blueprint).Field("m_DescriptionText").SetValue(BuildCustomRecipeItemDescription(blueprint));
            }
            Traverse.Create(blueprint).Field("m_Cost").SetValue(RulesRecipeItemCost(blueprint) + priceDelta);
            return BuildCustomRecipeItemGuid(blueprint.AssetGuid, enchantments, remove, name, ability, activatableAbility);
        }
        
        private static string ApplyComponentsBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
            var values = new List<string>();
            // Ensure Components array is not shared with base blueprint
            var componentsCopy = blueprint.ComponentsArray.ToArray();
            var indexCaptures = match.Groups["index"].Captures;
            var fieldCaptures = match.Groups["field"].Captures;
            var valueCaptures = match.Groups["value"].Captures;
            for (var index = 0; index < indexCaptures.Count; ++index) {
                values.Add(indexCaptures[index].Value);
                values.Add(fieldCaptures[index].Value);
                values.Add(valueCaptures[index].Value);
                var componentIndex = int.Parse(indexCaptures[index].Value);
                var field = fieldCaptures[index].Value;
                var value = valueCaptures[index].Value;
                var component = (BlueprintComponent)CustomBlueprintBuilder.CloneObject(componentsCopy[componentIndex]);
                var fieldAccess = Traverse.Create(component).Field(field);
                if (fieldAccess == null) {
                    throw new Exception($"Field {field} does not exist on component");
                }
                if (fieldAccess.GetValueType() == typeof(int)) {
                    fieldAccess.SetValue(int.Parse(value));
                } else if (fieldAccess.GetValueType().IsEnum) {
                    fieldAccess.SetValue(Enum.Parse(fieldAccess.GetValueType(), value));
                } else {
                    fieldAccess.SetValue(value);
                }
                componentsCopy[componentIndex] = component;
            }
            blueprint.ComponentsArray = componentsCopy;
            return BuildCustomComponentsItemGuid(blueprint.AssetGuid, values.ToArray());
        }

        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again, or null if we couldn't change the
        // blueprint.
        private static string ApplyBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
            string result;
            if (match.Groups["timer"].Success) {
                result = ApplyTimerBlueprintPatch((BlueprintBuff)blueprint);
            } else if (match.Groups["feat"].Success) {
                result = ApplyFeatBlueprintPatch((BlueprintFeature)blueprint, match);
            } else if (match.Groups["casterLevel"].Success) {
                result = ApplySpellItemBlueprintPatch((BlueprintItemEquipmentUsable)blueprint, match);
            } else if (match.Groups["enchantments"].Success) {
                result = ApplyRecipeItemBlueprintPatch((BlueprintItemEquipment) blueprint, match);
            } else if (match.Groups["components"].Success) {
                result = ApplyComponentsBlueprintPatch(blueprint, match);
            } else {
                throw new NotImplementedException($"Match of assetId {match.Value} didn't matching any sub-type");
            }
            return result;
        }

        private static bool ReverseEngineerEnchantmentCost(BlueprintItemEquipment blueprint, string enchantmentId) {
            if (blueprint.IsNotable || blueprint.Ability != null || blueprint.ActivatableAbility != null) {
                return false;
            }
            var mostExpensiveEnchantmentCost = 0;
            var costSum = 0;
            foreach (var enchantment in blueprint.Enchantments) {
                if (enchantment.AssetGuid == enchantmentId) {
                    continue;
                }
                if (!EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid)) {
                    return false;
                }
                var enchantmentCost = EnchantmentIdToCost[enchantment.AssetGuid];
                costSum += enchantmentCost;
                if (mostExpensiveEnchantmentCost < enchantmentCost) {
                    mostExpensiveEnchantmentCost = enchantmentCost;
                }
            }
            var remainder = blueprint.Cost - 3 * costSum / 2;
            if (remainder >= mostExpensiveEnchantmentCost) {
                // enchantment is most expensive enchantment
                EnchantmentIdToCost[enchantmentId] = remainder;
            } else {
                // mostExpensiveEnchantmentCost is the most expensive enchantment
                EnchantmentIdToCost[enchantmentId] = (2 * remainder + mostExpensiveEnchantmentCost) / 3;
            }
            return true;
        }

        [HarmonyPatch(typeof(MainMenu), "Start")]
        private static class MainMenuStartPatch {
            private static ObjectIDGenerator idGenerator = new ObjectIDGenerator();
            private static bool mainMenuStarted;

            private static void InitialiseCraftingData() {
                // Read the crafting data now that ResourcesLibrary is loaded.
                itemCraftingData = ReadJsonFile<ItemCraftingData[]>($"{ModEntry.Path}/Data/ItemTypes.json", new CraftingTypeConverter());
                // Initialise lookup tables.
                foreach (var itemData in itemCraftingData) {
                    if (itemData is RecipeBasedItemCraftingData recipeBased) {
                        recipeBased.Recipes = ReadJsonFile<RecipeData[]>($"{ModEntry.Path}/Data/{recipeBased.RecipeFileName}");
                        foreach (var recipe in recipeBased.Recipes) {
                            for (var index = 0; index < recipe.Enchantments.Length; ++index) {
                                var enchantment = recipe.Enchantments[index];
                                EnchantmentIdToRecipe[enchantment.AssetGuid] = recipe;
                                EnchantmentIdToCost[enchantment.AssetGuid] =
                                    recipe.CostFactor * (recipe.CostType == RecipeCostType.Level ? index + 1 : (index + 1) * (index + 1));
                            }
                            if (recipe.ParentNameId != null) {
                                recipeBased.SubRecipes = recipeBased.SubRecipes ?? new Dictionary<string, List<RecipeData>>();
                                if (!recipeBased.SubRecipes.ContainsKey(recipe.ParentNameId)) {
                                    recipeBased.SubRecipes[recipe.ParentNameId] = new List<RecipeData>();
                                }
                                recipeBased.SubRecipes[recipe.ParentNameId].Add(recipe);
                            }
                        }
                    }
                }
                var allUsableItems = Resources.FindObjectsOfTypeAll<BlueprintItemEquipment>();
                foreach (var item in allUsableItems) {
                    if (item.Enchantments != null) {
                        AddItemIdForEnchantment(item);
                    }
                }
                var allEnchantments = Resources.FindObjectsOfTypeAll<BlueprintEquipmentEnchantment>().Where(enchantment => !(enchantment is BlueprintArmorEnchantment)).ToArray();
                // BlueprintEnchantment.EnchantmentCost seems to be full of nonsense values - attempt to set cost of each enchantment by using the prices of
                // items with enchantments.
                foreach (var enchantment in allEnchantments) {
                    if (!EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid) && EnchantmentIdToItem.ContainsKey(enchantment.AssetGuid)) {
                        var itemsWithEnchantment = EnchantmentIdToItem[enchantment.AssetGuid];
                        foreach (var item in itemsWithEnchantment) {
                            if (DoesItemMatchEnchantments(item, enchantment.AssetGuid)) {
                                EnchantmentIdToCost[enchantment.AssetGuid] = item.Cost;
                                break;
                            }
                        }
                    }
                }
                foreach (var enchantment in allEnchantments) {
                    if (!EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid) && EnchantmentIdToItem.ContainsKey(enchantment.AssetGuid)) {
                        var itemsWithEnchantment = EnchantmentIdToItem[enchantment.AssetGuid];
                        foreach (var item in itemsWithEnchantment) {
                            if (ReverseEngineerEnchantmentCost(item, enchantment.AssetGuid)) {
                                break;
                            }
                        }
                    }
                }
            }

            private static void AddCraftingFeats(BlueprintProgression progression) {
                foreach (var levelEntry in progression.LevelEntries) {
                    foreach (var featureBase in levelEntry.Features) {
                        var selection = featureBase as BlueprintFeatureSelection;
                        if (selection != null && (CraftingFeatGroups.Contains(selection.Group) || CraftingFeatGroups.Contains(selection.Group2))) {
                            // Use ObjectIDGenerator to detect which shared lists we've added the feats to.
                            idGenerator.GetId(selection.AllFeatures, out var firstTime);
                            if (firstTime) {
                                foreach (var data in itemCraftingData) {
                                    var featBlueprint = ResourcesLibrary.TryGetBlueprint(data.FeatGuid) as BlueprintFeature;
                                    var list = selection.AllFeatures.ToList();
                                    list.Add(featBlueprint);
                                    selection.AllFeatures = list.ToArray();
                                    idGenerator.GetId(selection.AllFeatures, out firstTime);
                                }
                            }
                        }
                    }
                }
            }

            private static void AddAllCraftingFeats() {
                if (modEnabled && idGenerator != null) {
                    // Add crafting feats to general feat selection
                    AddCraftingFeats(Game.Instance.BlueprintRoot.Progression.FeatsProgression);
                    // ... and to relevant class feat selections.
                    foreach (var characterClass in Game.Instance.BlueprintRoot.Progression.CharacterClasses) {
                        AddCraftingFeats(characterClass.Progression);
                    }
                    idGenerator = null;
                }
            }

            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                mainMenuStarted = true;
                if (idGenerator != null) {
                    InitialiseCraftingData();
                    AddAllCraftingFeats();
                }
            }

            public static void ModEnabledChanged() {
                if (!modEnabled) {
                    // If the mod is disabled, reset idGenerator in case they re-enable it and we need to re-add crafting feats. 
                    idGenerator = new ObjectIDGenerator();
                } else if (mainMenuStarted) {
                    // If the mod is enabled and we're past the Start of main menu, add the crafting feats now.
                    AddAllCraftingFeats();
                }
            }
        }

        // Fix a bug in UI - ActionBarManager.Update does not refresh the Groups (spells/Actions/Belt)
        [HarmonyPatch(typeof(ActionBarManager), "Update")]
        private static class ActionBarManagerUpdatePatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(ActionBarManager __instance) {
                var mNeedReset = Traverse.Create(__instance).Field("m_NeedReset").GetValue<bool>();
                if (mNeedReset) {
                    var mSelected = Traverse.Create(__instance).Field("m_Selected").GetValue<UnitEntityData>();
                    __instance.Group.Set(mSelected);
                }
            }
        }

        // Load Variant spells into m_KnownSpellLevels
        [HarmonyPatch(typeof(Spellbook), "PostLoad")]
        private static class SpellbookPostLoadPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(Spellbook __instance) {
                var mKnownSpells = Traverse.Create(__instance).Field("m_KnownSpells")
                    .GetValue<List<AbilityData>[]>();
                var mKnownSpellLevels = Traverse.Create(__instance).Field("m_KnownSpellLevels")
                    .GetValue<Dictionary<BlueprintAbility, int>>();
                for (var level = 0; level < mKnownSpells.Length; ++level) {
                    foreach (var spell in mKnownSpells[level]) {
                        if (spell.Blueprint.HasVariants) {
                            foreach (var variant in spell.Blueprint.Variants) {
                                mKnownSpellLevels[variant] = level;
                            }
                        }
                    }
                    
                }
            }
        }

        private static void AddBattleLogMessage(string message, object tooltip = null, Color? color = null) {
            var data = new LogDataManager.LogItemData(message, color ?? GameLogStrings.Instance.DefaultColor, tooltip, PrefixIcon.None);
            if (Game.Instance.UI.BattleLogManager) {
                Game.Instance.UI.BattleLogManager.LogView.AddLogEntry(data);
            } else {
                PendingLogItems.Add(data);
            }
        }

        [HarmonyPatch(typeof(LogDataManager.LogItemData), "UpdateSize")]
        private static class LogItemDataUpdateSizePatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix() {
                // Avoid null pointer exception when BattleLogManager not set.
                return Game.Instance.UI.BattleLogManager != null;
            }
        }

        [HarmonyPatch(typeof(BattleLogManager), "Initialize")]
        private static class BattleLogManagerInitializePatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                if (Enumerable.Any(PendingLogItems)) {
                    foreach (var item in PendingLogItems) {
                        item.UpdateSize();
                        Game.Instance.UI.BattleLogManager.LogView.AddLogEntry(item);
                    }
                    PendingLogItems.Clear();
                }
            }
        }

        private static AbilityData FindCasterSpellSlot(UnitDescriptor caster, BlueprintAbility spellBlueprint, bool mustHavePrepared) {
            foreach (var spellBook in caster.Spellbooks) {
                var spellLevel = spellBook.GetSpellLevel(spellBlueprint);
                if (spellLevel > spellBook.MaxSpellLevel || spellLevel < 0) {
                    // TODO casting spells from an item?
                    continue;
                }
                if (mustHavePrepared && spellLevel > 0) {
                    if (spellBook.Blueprint.Spontaneous) {
                        // Spontaneous spellcaster must be able to cast at least one spell of the required level.
                        // TODO need to ensure that spontaneous casters don't double-dip on spell slots
                        if (spellBook.GetSpontaneousSlots(spellLevel) <= 0) {
                            continue;
                        }
                    } else {
                        // Prepared spellcaster must have memorized the spell...
                        var spellSlot = spellBook.GetMemorizedSpells(spellLevel).FirstOrDefault(slot =>
                            slot.Available && slot.Spell?.Blueprint == spellBlueprint);
                        if (spellSlot == null && spellBook.GetSpontaneousConversionSpells(spellLevel).Contains(spellBlueprint)) {
                            // ... or be able to convert, in which case any available spell of the given level will do.
                            spellSlot = spellBook.GetMemorizedSpells(spellLevel).FirstOrDefault(slot => slot.Available);
                        }
                        if (spellSlot == null) {
                            continue;
                        }
                        return spellSlot.Spell;
                    }
                }
                return spellBook.GetKnownSpells(spellLevel).First(spell => spell.Blueprint == spellBlueprint ||
                                                                           (spell.Blueprint.HasVariants && spell.Blueprint.Variants.Contains(spellBlueprint)));
            }
            return null;
        }
        
        private static int FindPrerequisiteSpells(CraftingProjectData project, UnitDescriptor caster, bool mustPrepare, out List<BlueprintAbility> missingSpells, out List<AbilityData> spellsToCast) {
            spellsToCast = new List<AbilityData>();
            missingSpells = new List<BlueprintAbility>();
            if (project.Prerequisites != null) {
                foreach (var spellBlueprint in project.Prerequisites) {
                    var spell = FindCasterSpellSlot(caster, spellBlueprint, mustPrepare);
                    if (spell != null) {
                        spellsToCast.Add(spell);
                    } else {
                        missingSpells.Add(spellBlueprint);
                    }
                }
            }
            return missingSpells.Count;
        }

        private static void WorkOnProjects(UnitDescriptor caster) {
            if (!caster.IsPlayerFaction || caster.State.IsDead || caster.State.IsFinallyDead) {
                return;
            }
            var withPlayer = Game.Instance.Player.PartyCharacters.Exists(character => character.Value.Descriptor == caster);
            var playerInCapital = IsPlayerInCapital();
            // Only update characters in the capital when the player is also there.
            if (!withPlayer && !playerInCapital) {
                // Character is back in the capital - skipping them for now.
                return;
            }
            var timer = GetCraftingTimerComponentForCaster(caster);
            if (timer == null || timer.CraftingProjects.Count == 0) {
                // Character is not doing any crafting
                return;
            }
            // Set GameLogContext so the caster will be used when generating localized strings.
            GameLogContext.SourceUnit = caster.Unit;
            // Round up the number of days, so caster makes some progress on a new project the first time they rest.
            var interval = Game.Instance.Player.GameTime.Subtract(timer.LastUpdated);
            var daysAvailableToCraft = (int)Math.Ceiling(interval.TotalDays);
            if (daysAvailableToCraft <= 0) {
                AddBattleLogMessage(new L10NString("craftMagicItems-logMessage-not-full-day"));
                return;
            }
            // Time passes for this character even if they end up making no progress on their projects.  LastUpdated can go up to
            // a day into the future, due to the rounding up of daysAvailableToCraft.
            timer.LastUpdated += TimeSpan.FromDays(daysAvailableToCraft);
            var isAdventuring = withPlayer && !playerInCapital;
            // Work on projects sequentially, skipping any that can't progress due to missing prerequisites or having too high a DC.
            foreach (var project in timer.CraftingProjects.ToList()) {
                var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
                if (project.UpgradeItem?.Wielder != null) {
                    // Check that the caster can get at the item they're upgrading.
                    var wielderWithParty = Game.Instance.Player.PartyCharacters.Contains(project.UpgradeItem.Wielder.Unit);
                    if (isAdventuring != wielderWithParty) {
                        project.AddMessage(string.Format(new L10NString("craftMagicItems-logMessage-missing-upgrade-item"),
                            project.UpgradeItem.Blueprint.Name, project.ItemBlueprint.Name));
                        AddBattleLogMessage(project.LastMessage);
                        continue;
                    }
                }
                var missing = FindPrerequisiteSpells(project, caster, isAdventuring, out var missingSpells, out var spellsToCast);
                if (missing > 0) {
                    foreach (var spellBlueprint in missingSpells) {
                        if (craftingData.PrerequisitesMandatory) {
                            project.AddMessage(string.Format(new L10NString("craftMagicItems-logMessage-missing-prerequisite"),
                                project.ItemBlueprint.Name, spellBlueprint.Name));
                            AddBattleLogMessage(project.LastMessage);
                        } else {
                            AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-missing-spell"), spellBlueprint.Name));
                        }
                    }
                    if (craftingData.PrerequisitesMandatory) {
                        // IF the item type has mandatory prerequisites and some are missing, move on to the next project.
                        continue;
                    }
                }
                var dc = 5 + project.CasterLevel + 5 * missing;
                var highestSpellbook = caster.Spellbooks.Aggregate((highest, book) => book.CasterLevel > highest.CasterLevel ? book : highest);
                var casterLevel = highestSpellbook.CasterLevel;
                if (casterLevel < project.CasterLevel) {
                    // Rob's ruling... if you're below the prerequisite caster level, you're considered to be missing a prerequisite for each
                    // level you fall short.
                    var casterLevelPenalty = 5 * (project.CasterLevel - casterLevel);
                    AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-low-caster-level"), project.CasterLevel,
                        casterLevelPenalty));
                    dc += casterLevelPenalty;
                }
                var skillCheck = 10 + caster.Stats.SkillKnowledgeArcana.ModifiedValue;
                if (skillCheck < dc) {
                    // Can't succeed by taking 10... move on to the next project.
                    project.AddMessage(string.Format(new L10NString("craftMagicItems-logMessage-dc-too-high"),
                        project.ItemBlueprint.Name, KnowledgeArcanaLocalized, skillCheck, dc));
                    AddBattleLogMessage(project.LastMessage);
                    continue;
                }
                // Cleared the last hurdle, so caster is going to make progress on this project
                if (isAdventuring) {
                    // Actually cast the spells if we're adventuring.
                    foreach (var spell in spellsToCast) {
                        AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-expend-spell"), spell.Name));
                        spell.SpendFromSpellbook();
                    }
                }
                // Each +5 to the DC increases the crafting rate by 1 multiple, but you only work at 1/4 speed if you're crafting while adventuring.
                var progressPerDay = CraftingProgressPerDay * (1 + (skillCheck - dc)/5) / (!isAdventuring || settings.CraftAtFullSpeedWhileAdventuring ? 1 : 4);
                var daysUntilProjectFinished = (int) Math.Ceiling(1.0 * (project.TargetCost - project.Progress) / progressPerDay);
                var daysCrafting = Math.Min(daysUntilProjectFinished, daysAvailableToCraft);
                daysAvailableToCraft -= daysCrafting;
                project.Progress += daysCrafting * progressPerDay;
                if (project.Progress >= project.TargetCost) {
                    // Completed the project!
                    AddBattleLogMessage(string.Format(new L10NString("craftMagicItems-logMessage-crafting-complete"), project.ItemBlueprint.Name));
                    if (project.UpgradeItem != null) {
                        Game.Instance.Player.Inventory.Remove(project.UpgradeItem);
                    }
                    CraftItem(project.ItemBlueprint, project.ItemType);
                    timer.CraftingProjects.Remove(project);
                } else {
                    project.AddMessage(string.Format(new L10NString("craftMagicItems-logMessage-made-progress"),
                        100 * daysCrafting * progressPerDay / project.TargetCost, project.ItemBlueprint.Name,
                        KnowledgeArcanaLocalized, skillCheck, dc));
                    AddBattleLogMessage(project.LastMessage);
                }
                if (daysAvailableToCraft <= 0) {
                    return;
                }
            }
            if (daysAvailableToCraft > 0) {
                // They didn't use up all available days - reset the time they can start crafting to now.
                timer.LastUpdated = Game.Instance.Player.GameTime;
            }
        }
        
        [HarmonyPatch(typeof(RestController), "ApplyRest")]
        private static class RestControllerApplyRestPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(UnitDescriptor unit) {
                WorkOnProjects(unit);
            }
        }

        [HarmonyPatch(typeof(LoadingProcess), "TickLoading")]
        private static class LoadingProcessTickLoadingPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(LoadingProcess __instance, ref bool __state) {
                __state = __instance.IsLoadingInProcess;
            }

            // ReSharper disable once UnusedMember.Local
            private static void Postfix(LoadingProcess __instance, bool __state) {
                if (!modEnabled && __state && !__instance.IsLoadingInProcess) {
                    // Load of a saved game just happened and mod is disabled - clean up crafting timer "buff" from all casters.
                    var casterList = UIUtility.GetGroup(true).Where(character => character.IsPlayerFaction
                                                                                 && !character.Descriptor.IsPet
                                                                                 && character.Descriptor.Spellbooks != null
                                                                                 && character.Descriptor.Spellbooks.Any());
                    foreach (var caster in casterList) {
                        GetCraftingTimerComponentForCaster(caster.Descriptor);
                    }
                }
            }
        }
    }
}
