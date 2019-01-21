using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Root;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.Controllers.Rest;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Designers.TempMapCode.Capital;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.Kingdom;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem;
using Kingmaker.UI;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UI.Log;
using Kingmaker.UI.Tooltip;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Alignments;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using Newtonsoft.Json;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace CraftMagicItems {
    public class Settings : UnityModManager.ModSettings {
        public bool CraftingCostsNoGold;
        public bool IgnoreCraftingFeats;
        public bool CraftingTakesNoTime;
        public float CraftingPriceScale = 1;
        public bool CraftAtFullSpeedWhileAdventuring;
        public bool IgnoreFeatCasterLevelRestriction;
        public bool IgnorePlusTenItemMaximum;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }

    public static class Main {
        // TODO remove the OldBlueprintPrefix eventually
        private const string OldBlueprintPrefix = "#ScribeScroll";
        private const string BlueprintPrefix = "#CraftMagicItems";

        private static readonly Regex BlueprintRegex =
            new Regex($"({OldBlueprintPrefix}|{BlueprintPrefix})"
                      + @"\(("
                      + @"CL=(?<casterLevel>\d+)(?<spellLevelMatch>,SL=(?<spellLevel>\d+))?(?<spellIdMatch>,spellId=\((?<spellId>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\))?"
                      + @"|enchantments=\((?<enchantments>|([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\)(,remove=(?<remove>[0-9a-f;]+))?(,name=(?<name>[^✔]+)✔)?"
                      + @"(,ability=(?<ability>null|[0-9a-f]+))?(,activatableAbility=(?<activatableAbility>null|[0-9a-f]+))?(,material=(?<material>[a-zA-Z]+))?"
                      + @"(,visual=(?<visual>null|[0-9a-f]+))?"
                      + @"|feat=(?<feat>[-a-z]+)"
                      + @"|(?<timer>timer)"
                      + @"|(?<components>(Component\[(?<index>[0-9]+)\](?<field>[^=]*)?=(?<value>[^,)]+),?)+(,nameId=(?<nameId>[^,)]+))?(,descriptionId=(?<descriptionId>[^,)]+))?)"
                      + @")\)");

        private const int MagicCraftingProgressPerDay = 500;
        private const int MundaneCraftingProgressPerDay = 5;
        private const int MissingPrerequisiteDCModifier = 5;
        private const int AdventuringProgressPenalty = 4;
        private const int MasterworkCost = 300;
        private const int WeaponPlusCost = 2000;
        private const int ArmourPlusCost = 1000;

        private static readonly string[] CraftingPriceStrings = {
            "100% (Owlcat prices)",
            "200% (Tabletop prices)",
            "Custom"
        };

        private static readonly FeatureGroup[] CraftingFeatGroups = {FeatureGroup.Feat, FeatureGroup.WizardFeat};
        private const string TimerBlueprintGuid = "52e4be2ba79c8c94d907bdbaf23ec15f#CraftMagicItems(timer)";
        private const string MasterworkGuid = "6b38844e2bffbac48b63036b66e735be";
        private const string MartialWeaponProficiencies = "203992ef5b35c864390b4e4a1e200629";
        private static readonly LocalizedString CasterLevelLocalized = new L10NString("dfb34498-61df-49b1-af18-0a84ce47fc98");
        private static readonly LocalizedString CharacterUsedItemLocalized = new L10NString("be7942ed-3af1-4fc7-b20b-41966d2f80b7");
        private static readonly LocalizedString ShieldBashLocalized = new L10NString("314ff56d-e93b-4915-8ca4-24a7670ad436");

        private static readonly ItemsFilter.ItemType[] SlotsWhichShowEnchantments = {
            ItemsFilter.ItemType.Weapon,
            ItemsFilter.ItemType.Armor,
            ItemsFilter.ItemType.Shield
        };

        private enum OpenSection {
            CraftMagicItemsSection,
            CraftMundaneItemsSection,
            ProjectsSection,
            FeatsSection,
            CheatsSection
        }

        public static UnityModManager.ModEntry ModEntry;

        private static bool modEnabled = true;
        public static Settings ModSettings;
        private static ItemCraftingData[] itemCraftingData;
        private static OpenSection currentSection = OpenSection.CraftMagicItemsSection;
        private static int selectedCustomPriceScaleIndex;
        private static int selectedItemTypeIndex;
        private static int selectedItemSubTypeIndex;
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
        private static BlueprintItem upgradingBlueprint;
        private static int selectedFeatToLearn;
        private static UnitEntityData currentCaster;
        private static readonly CustomBlueprintBuilder CustomBlueprintBuilder = new CustomBlueprintBuilder(BlueprintRegex, ApplyBlueprintPatch);

        private static readonly Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> SpellIdToItem =
            new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();

        private static readonly Dictionary<string, List<ItemCraftingData>> SubCraftingData = new Dictionary<string, List<ItemCraftingData>>();
        private static readonly Dictionary<string, List<BlueprintItemEquipment>> EnchantmentIdToItem = new Dictionary<string, List<BlueprintItemEquipment>>();
        private static readonly Dictionary<string, List<RecipeData>> EnchantmentIdToRecipe = new Dictionary<string, List<RecipeData>>();
        private static readonly Dictionary<string, int> EnchantmentIdToCost = new Dictionary<string, int>();
        private static readonly Random RandomGenerator = new Random();
        private static readonly List<LogDataManager.LogItemData> PendingLogItems = new List<LogDataManager.LogItemData>();
        private static readonly Dictionary<ItemEntity, CraftingProjectData> ItemUpgradeProjects = new Dictionary<ItemEntity, CraftingProjectData>();
        private static readonly List<CraftingProjectData> ItemCreationProjects = new List<CraftingProjectData>();

        // ReSharper disable once UnusedMember.Local
        private static void Load(UnityModManager.ModEntry modEntry) {
            ModEntry = modEntry;
            ModSettings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            selectedCustomPriceScaleIndex = Mathf.Abs(ModSettings.CraftingPriceScale - 1f) < 0.001 ? 0 :
                Mathf.Abs(ModSettings.CraftingPriceScale - 2f) < 0.001 ? 1 : 2;
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
            ModSettings.Save(modEntry);
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

                GetSelectedCrafter(true);

                if (RenderToggleSection(ref currentSection, OpenSection.CraftMagicItemsSection, "Craft Magic Items")) {
                    RenderCraftMagicItemsSection();
                }

                if (RenderToggleSection(ref currentSection, OpenSection.CraftMundaneItemsSection, "Craft Mundane Items")) {
                    RenderCraftMundaneItemsSection();
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

        private static string L10NFormat(string key, params object[] args) {
            // Set GameLogContext so the caster will be used when generating localized strings.
            GameLogContext.SourceUnit = currentCaster ?? GetSelectedCrafter(false);
            var template = new L10NString(key);
            return string.Format(template.ToString(), args);
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

        public static T ReadJsonFile<T>(string fileName, params JsonConverter[] converters) {
            try {
                var serializer = new JsonSerializer();
                foreach (var converter in converters) {
                    serializer.Converters.Add(converter);
                }

                using (var reader = new StreamReader(fileName))
                using (var textReader = new JsonTextReader(reader)) {
                    return serializer.Deserialize<T>(textReader);
                }
            } catch (Exception e) {
                ModEntry.Logger.Warning($"Exception reading JSON data from file {fileName}: {e}");
                throw;
            }
        }

        private static CraftingTimerComponent GetCraftingTimerComponentForCaster(UnitDescriptor caster, bool create = false) {
            var timerBlueprint = (BlueprintBuff) ResourcesLibrary.TryGetBlueprint(TimerBlueprintGuid);
            var timerBuff = (Buff) caster.GetFact(timerBlueprint);
            if (timerBuff == null) {
                if (!create) {
                    return null;
                }

                caster.AddFact<Buff>(timerBlueprint);
                timerBuff = (Buff) caster.GetFact(timerBlueprint);
            } else if (timerBuff.Blueprint.AssetGuid.Length == CustomBlueprintBuilder.VanillaAssetIdLength && CustomBlueprintBuilder.Downgrade) {
                // Clean up
                caster.RemoveFact(timerBuff);
                return null;
            }

            return timerBuff.SelectComponents<CraftingTimerComponent>().First();
        }

        private static void RenderCraftMagicItemsSection() {
            var caster = GetSelectedCrafter(false);
            if (caster == null) {
                return;
            }

            var itemTypes = itemCraftingData
                .Where(data => data.FeatGuid != null && (ModSettings.IgnoreCraftingFeats || CasterHasFeat(caster, data.FeatGuid)))
                .ToArray();
            if (!Enumerable.Any(itemTypes)) {
                RenderLabel($"{caster.CharacterName} does not know any crafting feats.");
                return;
            }

            var itemTypeNames = itemTypes.Select(data => new L10NString(data.NameId).ToString()).ToArray();
            RenderSelection(ref selectedItemTypeIndex, "Crafting: ", itemTypeNames, 6, ref selectedCustomName);
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
                return;
            }

            if (spellbooks.Count == 1) {
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
                if (ModSettings.CraftingTakesNoTime) {
                    selectedShowPreparedSpells = true;
                } else {
                    GUILayout.BeginHorizontal();
                    selectedShowPreparedSpells = GUILayout.Toggle(selectedShowPreparedSpells, " Show prepared spells only");
                    GUILayout.EndHorizontal();
                }
            } else {
                selectedShowPreparedSpells = false;
            }

            List<AbilityData> spellOptions;
            if (selectedShowPreparedSpells) {
                // Prepared spellcaster
                spellOptions = spellbook.GetMemorizedSpells(spellLevel).Where(slot => slot.Spell != null && slot.Available).Select(slot => slot.Spell)
                    .ToList();
            } else {
                // Cantrips/Orisons or Spontaneous spellcaster or showing all known spells
                if (spellLevel > 0 && spellbook.Blueprint.Spontaneous) {
                    var spontaneousSlots = spellbook.GetSpontaneousSlots(spellLevel);
                    RenderLabel($"{caster.CharacterName} can cast {spontaneousSlots} more level {spellLevel} spells today.");
                    if (spontaneousSlots == 0 && ModSettings.CraftingTakesNoTime) {
                        return;
                    }
                }

                spellOptions = spellbook.GetSpecialSpells(spellLevel).Concat(spellbook.GetKnownSpells(spellLevel)).ToList();
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

                if (RenderCraftingSkillInformation(caster, StatType.SkillKnowledgeArcana, 5 + selectedCasterLevel, selectedCasterLevel) < 0) {
                    if (ModSettings.CraftingTakesNoTime) {
                        RenderLabel($"This project would be too hard for {caster.CharacterName} if \"Crafting Takes No Time\" cheat was disabled.");
                    } else {
                        RenderLabel($"This project will be too hard for {caster.CharacterName}");
                        return;
                    }
                }

                if (selectedShowPreparedSpells && spellbook.GetSpontaneousConversionSpells(spellLevel).Any()) {
                    var firstSpell = spellbook.Blueprint.Spontaneous
                        ? spellbook.GetKnownSpells(spellLevel).First(spell => true)
                        : spellbook.GetMemorizedSpells(spellLevel).FirstOrDefault(slot => slot.Spell != null && slot.Available)?.Spell;
                    if (firstSpell != null) {
                        foreach (var spontaneousBlueprint in spellbook.GetSpontaneousConversionSpells(spellLevel)) {
                            // Only add spontaneous spells that aren't already in the list.
                            if (!spellOptions.Any(spell => spell.Blueprint == spontaneousBlueprint)) {
                                spellOptions.Add(new AbilityData(firstSpell, spontaneousBlueprint));
                            }
                        }
                    }
                }

                foreach (var spell in spellOptions.OrderBy(spell => spell.Name).GroupBy(spell => spell.Name).Select(group => group.First())) {
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
                case ItemsFilter.ItemType.Usable:
                    return "6f22a0fb-f0d5-47c2-aa03-a6c299e85251";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            }
        }

        private static IEnumerable<BlueprintItemEnchantment> GetEnchantments(BlueprintItem blueprint, RecipeData sourceRecipe = null) {
            if (blueprint is BlueprintItemShield shield) {
                // A shield can be treated as armour or as a weapon... assume armour unless being used by a recipe which applies to weapons.
                var weaponRecipe = sourceRecipe?.OnlyForSlots?.Contains(ItemsFilter.ItemType.Weapon) ?? false;
                return weaponRecipe
                    ? shield.WeaponComponent != null ? shield.WeaponComponent.Enchantments : Enumerable.Empty<BlueprintItemEnchantment>()
                    : shield.ArmorComponent.Enchantments;
            }

            return blueprint.Enchantments;
        }

        private static ItemsFilter.ItemType GetItemType(BlueprintItem blueprint) {
            return (blueprint is BlueprintItemArmor armour && armour.IsShield
                    || blueprint is BlueprintItemWeapon weapon && (
                        weapon.Category == WeaponCategory.WeaponLightShield
                        || weapon.Category == WeaponCategory.WeaponHeavyShield
                        || weapon.Category == WeaponCategory.SpikedLightShield
                        || weapon.Category == WeaponCategory.SpikedHeavyShield))
                ? ItemsFilter.ItemType.Shield
                : blueprint.ItemType;
        }

        private static RecipeData FindSourceRecipe(string selectedEnchantmentId, BlueprintItem blueprint) {
            if (!EnchantmentIdToRecipe.ContainsKey(selectedEnchantmentId)) {
                return null;
            }

            var slot = GetItemType(blueprint);
            var recipes = EnchantmentIdToRecipe[selectedEnchantmentId];
            return recipes.FirstOrDefault(recipe => (recipe.OnlyForSlots == null || recipe.OnlyForSlots.Contains(slot))
                                                    && (blueprint == null || RecipeAppliesToBlueprint(recipe, blueprint, true)));
        }

        private static string FindSupersededEnchantmentId(BlueprintItem blueprint, string selectedEnchantmentId) {
            if (blueprint != null) {
                var selectedRecipe = FindSourceRecipe(selectedEnchantmentId, blueprint);
                foreach (var enchantment in GetEnchantments(blueprint, selectedRecipe)) {
                    if (FindSourceRecipe(enchantment.AssetGuid, blueprint) == selectedRecipe) {
                        return enchantment.AssetGuid;
                    }
                }

                // Special case - enchanting a masterwork item supersedes the masterwork quality
                if (IsMasterwork(blueprint)) {
                    return MasterworkGuid;
                }
            }

            return null;
        }

        private static bool DoesItemMatchEnchantments(BlueprintItemEquipment blueprint, string selectedEnchantmentId,
            BlueprintItemEquipment upgradeItem = null) {
            var isNotable = upgradeItem && upgradeItem.IsNotable;
            var ability = upgradeItem ? upgradeItem.Ability : null;
            var activatableAbility = upgradeItem ? upgradeItem.ActivatableAbility : null;
            // If item is notable or has an ability that upgradeItem does not, it's not a match.
            if (blueprint.IsNotable != isNotable || blueprint.Ability != ability || blueprint.ActivatableAbility != activatableAbility) {
                return false;
            }

            var supersededEnchantmentId = FindSupersededEnchantmentId(upgradeItem, selectedEnchantmentId);
            var enchantmentCount = (upgradeItem ? GetEnchantments(upgradeItem).Count() : 0) + (supersededEnchantmentId == null ? 1 : 0);
            if (GetEnchantments(blueprint).Count() != enchantmentCount) {
                return false;
            }

            if (GetEnchantments(blueprint).Any(enchantment => enchantment.AssetGuid != selectedEnchantmentId
                                                              && enchantment.AssetGuid != supersededEnchantmentId
                                                              && (!upgradeItem || !GetEnchantments(upgradeItem).Contains(enchantment)))) {
                return false;
            }

            if (upgradeItem != null) {
                // If upgradeItem is armour or a shield or a weapon, item is not a match if it's not the same type of armour/shield/weapon
                switch (upgradeItem) {
                    case BlueprintItemArmor upgradeItemArmour when !(blueprint is BlueprintItemArmor itemArmour) || itemArmour.Type != upgradeItemArmour.Type:
                    case BlueprintItemShield upgradeItemShield when !(blueprint is BlueprintItemShield itemShield) || itemShield.Type != upgradeItemShield.Type:
                    case BlueprintItemWeapon upgradeItemWeapon when !(blueprint is BlueprintItemWeapon itemWeapon) || itemWeapon.Type != upgradeItemWeapon.Type
                                                                                                                   || itemWeapon.DamageType.Physical.Material !=
                                                                                                                   upgradeItemWeapon.DamageType.Physical
                                                                                                                       .Material:
                        return false;
                }

                // Special handler for heavy shield, because the game data has some very messed up shields in it.
                if (blueprint.AssetGuid == "6989ca8e0d28af643b908468ead16922") {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<T> PrependConditional<T>(this IEnumerable<T> target, bool prepend, params T[] items) {
            return prepend ? items.Concat(target ?? throw new ArgumentException(nameof(target))) : target;
        }

        private static string BuildCommaList(this IEnumerable<string> list, bool or) {
            var array = list.ToArray();
            if (array.Length < 2) {
                return array.Join();
            }

            var commaList = "";
            for (var index = 0; index < array.Length - 1; ++index) {
                if (index > 0) {
                    commaList += ", " + array[index];
                } else {
                    commaList += array[index];
                }
            }

            var key = or ? "craftMagicItems-logMessage-comma-list-or" : "craftMagicItems-logMessage-comma-list-and";
            return L10NFormat(key, commaList, array[array.Length - 1]);
        }

        private static bool IsMasterwork(BlueprintItem blueprint) {
            return GetEnchantments(blueprint).Any(enchantment => enchantment.AssetGuid == MasterworkGuid);
        }

        // Use instead of UIUtility.IsMagicItem.
        private static bool IsEnchanted(BlueprintItem blueprint, RecipeData recipe = null) {
            if (blueprint == null) {
                return false;
            }

            switch (blueprint) {
                case BlueprintItemArmor armor:
                case BlueprintItemWeapon weapon:
                    return ItemPlusEquivalent(blueprint) > 0;
                case BlueprintItemShield shield:
                    var isWeaponEnchantmentRecipe = recipe?.OnlyForSlots?.Contains(ItemsFilter.ItemType.Weapon) ?? false;
                    return !isWeaponEnchantmentRecipe && ItemPlusEquivalent(shield.ArmorComponent) > 0
                           || isWeaponEnchantmentRecipe && ItemPlusEquivalent(shield.WeaponComponent) > 0;
                case BlueprintItemEquipmentUsable usable:
                    return !usable.SpendCharges || usable.RestoreChargesOnRest;
                default:
                    return GetEnchantments(blueprint).Any();
            }
        }

        private static bool CanEnchant(ItemEntity item) {
            // The game has no masterwork armour or shields, so I guess you can enchant any of them.
            return IsEnchanted(item.Blueprint)
                   || item.Blueprint is BlueprintItemArmor
                   || item.Blueprint is BlueprintItemShield
                   || IsMasterwork(item.Blueprint);
        }

        private static bool IsMetalArmour(BlueprintArmorType armourType) {
            // Rely on the fact that the only light armour that is metal is a Chain Shirt, and the only medium armour that is not metal is Hide.
            return (armourType.ProficiencyGroup != ArmorProficiencyGroup.Light || armourType.AssetGuid == "7467b0ab8641d8f43af7fc46f2108a1a")
                   && (armourType.ProficiencyGroup != ArmorProficiencyGroup.Medium || armourType.AssetGuid != "7a01292cef39bf2408f7fae7a9f47594");
        }

        private static bool ItemMatchesRestrictions(BlueprintItem blueprint, IEnumerable<ItemRestrictions> restrictions) {
            if (restrictions != null) {
                var weapon = blueprint as BlueprintItemWeapon;
                var armour = blueprint as BlueprintItemArmor;
                foreach (var restriction in restrictions) {
                    switch (restriction) {
                        case ItemRestrictions.WeaponMelee when weapon == null || weapon.AttackType != AttackType.Melee:
                        case ItemRestrictions.WeaponRanged when weapon == null || weapon.AttackType != AttackType.Ranged:
                        case ItemRestrictions.WeaponBludgeoning when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Bludgeoning) == 0:
                        case ItemRestrictions.WeaponPiercing when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Piercing) == 0:
                        case ItemRestrictions.WeaponSlashing when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Slashing) == 0:
                        case ItemRestrictions.WeaponNotBludgeoning
                            when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Bludgeoning) != 0:
                        case ItemRestrictions.WeaponNotPiercing when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Piercing) != 0:
                        case ItemRestrictions.WeaponNotSlashing when weapon == null || (weapon.DamageType.Physical.Form & PhysicalDamageForm.Slashing) != 0:
                        case ItemRestrictions.WeaponFinessable when weapon == null || !weapon.Category.HasSubCategory(WeaponSubCategory.Finessable):
                        case ItemRestrictions.WeaponLight when weapon == null || !weapon.IsLight:
                        case ItemRestrictions.WeaponNotLight when weapon == null || weapon.IsLight:
                        case ItemRestrictions.WeaponMetal when weapon == null || !weapon.Category.HasSubCategory(WeaponSubCategory.Metal):
                        case ItemRestrictions.ArmourMetal when armour == null || !IsMetalArmour(armour.Type):
                        case ItemRestrictions.ArmourNotMetal when armour == null || IsMetalArmour(armour.Type):
                        case ItemRestrictions.ArmourLight when armour == null || armour.Type.ProficiencyGroup != ArmorProficiencyGroup.Light:
                        case ItemRestrictions.ArmourMedium when armour == null || armour.Type.ProficiencyGroup != ArmorProficiencyGroup.Medium:
                        case ItemRestrictions.ArmourHeavy when armour == null || armour.Type.ProficiencyGroup != ArmorProficiencyGroup.Heavy:
                        case ItemRestrictions.EnhancmentBonus2 when ItemPlus(blueprint) < 2:
                        case ItemRestrictions.EnhancmentBonus3 when ItemPlus(blueprint) < 3:
                        case ItemRestrictions.EnhancmentBonus4 when ItemPlus(blueprint) < 4:
                        case ItemRestrictions.EnhancmentBonus5 when ItemPlus(blueprint) < 5:
                            return false;
                    }
                }
            }

            return true;
        }

        private static bool RecipeAppliesToBlueprint(RecipeData recipe, BlueprintItem blueprint, bool skipEnchantedCheck = false) {
            return blueprint == null
                   || (skipEnchantedCheck || recipe.CanApplyToMundaneItem || IsEnchanted(blueprint, recipe))
                   && ItemMatchesRestrictions(blueprint, recipe.Restrictions)
                   // Weapons with special materials can't apply recipes which apply special materials
                   && (!(blueprint is BlueprintItemWeapon weapon) || recipe.Material == 0 || weapon.DamageType.Physical.Material == 0)
                   // Shields make this complicated.  A shield's armour component can match a recipe which is for shields but not weapons. 
                   && (recipe.OnlyForSlots == null || recipe.OnlyForSlots.Contains(blueprint.ItemType)
                                                   || blueprint is BlueprintItemArmor armor && armor.IsShield
                                                                                            && recipe.OnlyForSlots.Contains(ItemsFilter.ItemType.Shield)
                                                                                            && !recipe.OnlyForSlots.Contains(ItemsFilter.ItemType.Weapon))
                   // ... also, a top-level shield object should not match a weapon recipe if it has no weapon component.
                   && !(recipe.OnlyForSlots != null && blueprint is BlueprintItemShield shield
                                                    && shield.WeaponComponent == null
                                                    && recipe.OnlyForSlots.Contains(ItemsFilter.ItemType.Weapon))
                ;
        }

        private static ItemEntity BuildItemEntity(BlueprintItem blueprint, ItemCraftingData craftingData) {
            var item = blueprint.CreateEntity();
            item.IsIdentified = true;
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                item.Charges = spellBased.Charges; // Set the charges, since wand blueprints have random values.
            }

            if (item is ItemEntityShield shield) {
                shield.ArmorComponent.IsIdentified = item.IsIdentified;
                if (shield.WeaponComponent != null) {
                    shield.WeaponComponent.IsIdentified = item.IsIdentified;
                }
            }

            item.PostLoad();
            return item;
        }

        private static bool DoesBlueprintMatchSlot(BlueprintItemEquipment blueprint, ItemsFilter.ItemType slot) {
            return blueprint.ItemType == slot || slot == ItemsFilter.ItemType.Usable && blueprint is BlueprintItemEquipmentUsable;
        }

        private static void RenderRecipeBasedCrafting(UnitEntityData caster, RecipeBasedItemCraftingData craftingData) {
            // Choose slot/weapon type.
            if (craftingData.Slots.Length > 1) {
                var names = craftingData.Slots.Select(slot => new L10NString(GetSlotStringKey(slot)).ToString()).ToArray();
                RenderSelection(ref selectedItemSlotIndex, "Item type", names, 10, ref selectedCustomName);
            } else {
                selectedItemSlotIndex = 0;
            }

            var selectedSlot = craftingData.Slots[selectedItemSlotIndex];
            var playerInCapital = IsPlayerInCapital();
            // Choose an existing or in-progress item of that type, or create a new one (if allowed).
            var items = Game.Instance.Player.Inventory
                .Concat(ItemCreationProjects.Select(project => project.ResultItem))
                .Where(item => item.Blueprint is BlueprintItemEquipment blueprint
                               && DoesBlueprintMatchSlot(blueprint, selectedSlot)
                               && CanEnchant(item)
                               && (item.Wielder == null
                                   || playerInCapital
                                   || Game.Instance.Player.PartyCharacters.Contains(item.Wielder.Unit)))
                .Select(item => {
                    while (ItemUpgradeProjects.ContainsKey(item)) {
                        item = ItemUpgradeProjects[item].ResultItem;
                    }

                    return item;
                })
                .OrderBy(item => item.Name)
                .ToArray();
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
                .Where(recipe => (recipe.ParentNameId == null || recipe == craftingData.SubRecipes[recipe.ParentNameId][0])
                                 && (recipe.OnlyForSlots == null || recipe.OnlyForSlots.Contains(selectedSlot))
                                 && RecipeAppliesToBlueprint(recipe, upgradeItem?.Blueprint))
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
                var existingIndex = availableEnchantments.FindIndex(enchantment => enchantment.AssetGuid == supersededEnchantment);
                availableEnchantments = availableEnchantments.Skip(existingIndex + 1).ToArray();
            }

            if (availableEnchantments.Length > 0 && selectedRecipe.Enchantments.Length > 1) {
                var bonusMultiplier = selectedRecipe.BonusMultiplier > 0 ? selectedRecipe.BonusMultiplier : 1;
                var counter = bonusMultiplier * (selectedRecipe.Enchantments.Length - availableEnchantments.Length);
                var enchantmentNames = availableEnchantments.Select(enchantment => {
                    counter += bonusMultiplier;
                    return enchantment.Name.Empty() ? $"+{counter}" : enchantment.Name;
                });
                RenderSelection(ref selectedEnchantmentIndex, "", enchantmentNames.ToArray(), 6);
            } else if (availableEnchantments.Length == 1) {
                selectedEnchantmentIndex = 0;
            } else {
                RenderLabel("This item cannot be further upgraded with this enchantment.");
                return;
            }

            var selectedEnchantment = availableEnchantments[selectedEnchantmentIndex];
            var casterLevel = selectedRecipe.CasterLevelStart + selectedRecipe.Enchantments.IndexOf(selectedEnchantment) * selectedRecipe.CasterLevelMultiplier;
            if (!string.IsNullOrEmpty(selectedEnchantment.Description)) {
                RenderLabel(selectedEnchantment.Description);
            }

            if (upgradeItem?.Blueprint.ItemType == ItemsFilter.ItemType.Shield && (selectedRecipe.OnlyForSlots?.Contains(ItemsFilter.ItemType.Weapon) ?? false)
                                                                               && selectedRecipe.CanApplyToMundaneItem) {
                RenderLabel("<color=red><b>Warning:</b></color> placing weapon enchantments on a shield is only useful if you are able to shield-bash, using " +
                            "the shield as a weapon.  To improve the shield's bonus to AC, use \"Shield enhancment\" instead.");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Prerequisites: ", GUILayout.ExpandWidth(false));
            var prerequisites = $"{CasterLevelLocalized} {casterLevel}";
            if (selectedRecipe.PrerequisiteSpells.Length > 0) {
                prerequisites += $"; {selectedRecipe.PrerequisiteSpells.Select(ability => ability.Name).BuildCommaList(selectedRecipe.AnyPrerequisite)}";
            }

            if (selectedRecipe.CrafterPrerequisites != null) {
                prerequisites += "; " + L10NFormat("craftMagicItems-crafter-prerequisite-required", selectedRecipe.CrafterPrerequisites
                                     .Select(prerequisite => new L10NString($"craftMagicItems-crafter-prerequisite-{prerequisite}").ToString())
                                     .BuildCommaList(false));
            }

            GUILayout.Label(prerequisites, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            if (RenderCraftingSkillInformation(caster, StatType.SkillKnowledgeArcana, 5 + casterLevel, casterLevel,
                    selectedRecipe.PrerequisiteSpells, selectedRecipe.AnyPrerequisite, selectedRecipe.CrafterPrerequisites) < 0) {
                if (ModSettings.CraftingTakesNoTime) {
                    RenderLabel($"This project would be too hard for {caster.CharacterName} if \"Crafting Takes No Time\" cheat was disabled.");
                } else {
                    RenderLabel($"This project will be too hard for {caster.CharacterName}");
                    return;
                }
            }

            // See if the selected enchantment (plus optional mundane base item) corresponds to a vanilla blueprint.
            var allItemBlueprintsWithEnchantment =
                IsEnchanted(upgradeItem?.Blueprint) ? null : FindItemBlueprintForEnchantmentId(selectedEnchantment.AssetGuid);
            var matchingItem = allItemBlueprintsWithEnchantment?.FirstOrDefault(blueprint =>
                DoesBlueprintMatchSlot(blueprint, selectedSlot)
                && DoesItemMatchEnchantments(blueprint, selectedEnchantment.AssetGuid, upgradeItem?.Blueprint as BlueprintItemEquipment)
            );
            BlueprintItemEquipment itemToCraft;
            var itemGuid = "[not set]";
            if (matchingItem) {
                // Crafting an existing blueprint.
                itemToCraft = matchingItem;
            } else if (upgradeItem != null) {
                // Upgrading to a custom blueprint
                RenderCustomNameField(upgradeItem.Blueprint.Name);
                IEnumerable<string> enchantments;
                string supersededEnchantmentId;
                if (selectedRecipe.EnchantmentsCumulative) {
                    enchantments = availableEnchantments.Take(selectedEnchantmentIndex + 1).Select(enchantment => enchantment.AssetGuid);
                    supersededEnchantmentId = null;
                } else {
                    enchantments = new List<string> {selectedEnchantment.AssetGuid};
                    supersededEnchantmentId = FindSupersededEnchantmentId(upgradeItem.Blueprint, selectedEnchantment.AssetGuid);
                }

                itemGuid = BuildCustomRecipeItemGuid(upgradeItem.Blueprint.AssetGuid, enchantments,
                    supersededEnchantmentId == null ? null : new[] {supersededEnchantmentId},
                    selectedCustomName == upgradeItem.Blueprint.Name ? null : selectedCustomName);
                itemToCraft = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(itemGuid);
            } else {
                // Crafting a new custom blueprint from scratch.
                BlueprintItemEquipment baseBlueprint;
                if (selectedBaseGuid != null) {
                    baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                    if (!baseBlueprint || !DoesBlueprintMatchSlot(baseBlueprint, selectedSlot)) {
                        selectedBaseGuid = null;
                    }
                }

                selectedBaseGuid = selectedBaseGuid ?? RandomBaseBlueprintId(craftingData,
                                       guid => DoesBlueprintMatchSlot(ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(guid), selectedSlot));
                baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                RenderCustomNameField($"{new L10NString(selectedRecipe.NameId)} {new L10NString(GetSlotStringKey(selectedSlot))}");
                var enchantmentsToRemove = GetEnchantments(baseBlueprint, selectedRecipe).Select(enchantment => enchantment.AssetGuid).ToArray();
                itemGuid = BuildCustomRecipeItemGuid(selectedBaseGuid, new List<string> {selectedEnchantment.AssetGuid}, enchantmentsToRemove,
                    selectedCustomName ?? "[custom item]", "null", "null");
                itemToCraft = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(itemGuid);
            }

            if (!itemToCraft) {
                RenderLabel($"Error: null custom item from looking up blueprint ID {itemGuid}");
            } else {
                if (IsItemLegalEnchantmentLevel(itemToCraft)) {
                    RenderRecipeBasedCraftItemControl(caster, craftingData, selectedRecipe, casterLevel, itemToCraft, upgradeItem);
                } else {
                    RenderLabel($"This would result in {itemToCraft.Name} having an equivalent enhancement bonus of more than +10");
                }
            }
        }

        private static int RenderCraftingSkillInformation(UnitEntityData caster, StatType skill, int dc, int casterLevel = 0,
            BlueprintAbility[] prerequisiteSpells = null, bool anyPrerequisite = false, CrafterPrerequisiteType[] crafterPrerequisites = null,
            bool render = true) {
            RenderLabel($"Base Crafting DC: {dc}");
            var missing = CheckSpellPrerequisites(prerequisiteSpells, anyPrerequisite, caster.Descriptor, false, out var missingSpells, out var spellsToCast);
            missing += GetMissingCrafterPrerequisites(crafterPrerequisites, caster.Descriptor).Count;
            if (missing > 0 && render) {
                RenderLabel(
                    $"{caster.CharacterName} is unable to meet {missing} of the prerequisites, raising the DC by {MissingPrerequisiteDCModifier * missing}");
            }

            var crafterCasterLevel = caster.Descriptor.Spellbooks.Aggregate(0, (highest, book) => book.CasterLevel > highest ? book.CasterLevel : highest);
            if (crafterCasterLevel < casterLevel) {
                var casterLevelShortfall = casterLevel - crafterCasterLevel;
                if (render) {
                    RenderLabel(L10NFormat("craftMagicItems-logMessage-low-caster-level", casterLevel, MissingPrerequisiteDCModifier * casterLevelShortfall));
                }

                missing += casterLevelShortfall;
            }

            if (missing > 0) {
                dc += MissingPrerequisiteDCModifier * missing;
            }

            var skillCheck = 10 + caster.Stats.GetStat(skill).ModifiedValue;
            if (render) {
                RenderLabel(L10NFormat("craftMagicItems-logMessage-made-progress-check", LocalizedTexts.Instance.Stats.GetText(skill), skillCheck, dc));
            }

            return skillCheck - dc;
        }

        private static int GetMaterialComponentMultiplier(ItemCraftingData craftingData) {
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                return spellBased.Charges;
            }

            return 0;
        }

        private static void CancelCraftingProject(CraftingProjectData project) {
            // Refund gold and material components.
            if (!ModSettings.CraftingCostsNoGold) {
                Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                var goldRefund = project.GoldSpent >= 0 ? project.GoldSpent : project.TargetCost;
                Game.Instance.Player.GainMoney(goldRefund);
                var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
                BuildCostString(out var cost, craftingData, goldRefund, project.Prerequisites);
                var factor = GetMaterialComponentMultiplier(craftingData);
                if (factor > 0) {
                    foreach (var prerequisiteSpell in project.Prerequisites) {
                        if (prerequisiteSpell.MaterialComponent.Item) {
                            var number = prerequisiteSpell.MaterialComponent.Count * factor;
                            Game.Instance.Player.Inventory.Add(prerequisiteSpell.MaterialComponent.Item, number);
                        }
                    }
                }

                AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-crafting-cancelled", project.ResultItem.Name, cost));
            }

            var timer = GetCraftingTimerComponentForCaster(project.Crafter.Descriptor);
            timer.CraftingProjects.Remove(project);
            if (project.UpgradeItem == null) {
                ItemCreationProjects.Remove(project);
            } else {
                ItemUpgradeProjects.Remove(project.UpgradeItem);
                if (ItemUpgradeProjects.ContainsKey(project.ResultItem)) {
                    CancelCraftingProject(ItemUpgradeProjects[project.ResultItem]);
                }
            }
        }

        private static int CalculateBaseMundaneCraftingDC(RecipeBasedItemCraftingData craftingData, BlueprintItem blueprint, UnitDescriptor crafter) {
            var dc = craftingData.MundaneBaseDC;
            switch (blueprint) {
                case BlueprintItemArmor armor:
                    return dc + armor.ArmorBonus;
                case BlueprintItemShield shield:
                    return dc + shield.ArmorComponent.ArmorBonus;
                case BlueprintItemWeapon weapon:
                    if (weapon.Category.HasSubCategory(WeaponSubCategory.Exotic)) {
                        var martialWeaponProficiencies = ResourcesLibrary.TryGetBlueprint(MartialWeaponProficiencies);
                        if (martialWeaponProficiencies != null && martialWeaponProficiencies.GetComponents<AddProficiencies>()
                                .Any(addProficiencies => addProficiencies.RaceRestriction != null
                                                         && addProficiencies.RaceRestriction == crafter.Progression.Race
                                                         && addProficiencies.WeaponProficiencies.Contains(weapon.Category))) {
                            // The crafter treats this exotic weapon as if it's martial.  Hard code the difference in DC.
                            dc -= 3;
                        }
                    }

                    break;
            }

            return dc;
        }

        private static int CalculateMundaneCraftingDC(RecipeBasedItemCraftingData craftingData, BlueprintItem blueprint, UnitDescriptor crafter,
            RecipeData recipe = null) {
            var dc = CalculateBaseMundaneCraftingDC(craftingData, blueprint, crafter);
            return dc + (recipe?.MundaneDC ?? blueprint.Enchantments
                             .Select(enchantment => FindSourceRecipe(enchantment.AssetGuid, blueprint)?.MundaneDC ?? 0)
                             .DefaultIfEmpty(0)
                             .Max()
                   );
        }

        private static void RenderCraftMundaneItemsSection() {
            var crafter = GetSelectedCrafter(false);

            // Choose crafting data
            var itemTypes = itemCraftingData
                .Where(data => data.FeatGuid == null
                               && (data.ParentNameId == null || SubCraftingData[data.ParentNameId][0] == data))
                .ToArray();
            var itemTypeNames = itemTypes.Select(data => new L10NString(data.ParentNameId ?? data.NameId).ToString()).ToArray();
            if (upgradingBlueprint == null) {
                RenderSelection(ref selectedItemTypeIndex, "Crafting: ", itemTypeNames, 6, ref selectedCustomName);
            }

            var selectedCraftingData = itemTypes[selectedItemTypeIndex];
            if (selectedCraftingData.ParentNameId != null) {
                itemTypeNames = SubCraftingData[selectedCraftingData.ParentNameId].Select(data => new L10NString(data.NameId).ToString()).ToArray();
                if (upgradingBlueprint == null) {
                    RenderSelection(ref selectedItemSubTypeIndex, $"{new L10NString(selectedCraftingData.ParentNameId)}: ", itemTypeNames, 6);
                }

                selectedCraftingData = SubCraftingData[selectedCraftingData.ParentNameId][selectedItemSubTypeIndex];
            }

            if (!(selectedCraftingData is RecipeBasedItemCraftingData craftingData)) {
                RenderLabel("Unable to find mundane crafting recipe.");
                return;
            }

            BlueprintItem baseBlueprint;

            if (upgradingBlueprint != null) {
                baseBlueprint = upgradingBlueprint;
                RenderLabel($"Applying upgrades to {baseBlueprint.Name}");
            } else {
                // Choose mundane item of selected type to create
                var blueprints = craftingData.NewItemBaseIDs
                    .Select(ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>)
                    .Where(blueprint => blueprint != null
                                        && (!(blueprint is BlueprintItemWeapon weapon) || !weapon.Category.HasSubCategory(WeaponSubCategory.Disabled)))
                    .OrderBy(blueprint => blueprint.Name)
                    .ToArray();
                var blueprintNames = blueprints.Select(item => item.Name).ToArray();
                if (blueprintNames.Length == 0) {
                    RenderLabel("No known items of that type.");
                    return;
                }

                RenderSelection(ref selectedUpgradeItemIndex, "Item: ", blueprintNames, 5, ref selectedCustomName);
                baseBlueprint = blueprints[selectedUpgradeItemIndex];
                // See existing item details and enchantments.
                RenderLabel(baseBlueprint.Description);
            }

            // Assume only one slot type per crafting data
            var selectedSlot = craftingData.Slots[0];

            // Pick recipe to apply.
            var availableRecipes = craftingData.Recipes
                .Where(recipe => (recipe.OnlyForSlots == null || recipe.OnlyForSlots.Contains(selectedSlot))
                                 && RecipeAppliesToBlueprint(recipe, baseBlueprint)
                                 && (recipe.Enchantments.Length != 1 || !baseBlueprint.Enchantments.Contains(recipe.Enchantments[0])))
                .OrderBy(recipe => new L10NString(recipe.NameId).ToString())
                .ToArray();
            var recipeNames = availableRecipes.Select(recipe => new L10NString(recipe.NameId).ToString()).ToArray();
            RenderSelection(ref selectedRecipeIndex, "Craft: ", recipeNames, 6, ref selectedCustomName);
            var selectedRecipe = availableRecipes.Any() ? availableRecipes[selectedRecipeIndex] : null;
            var selectedEnchantment = selectedRecipe?.Enchantments.Length == 1 ? selectedRecipe.Enchantments[0] : null;
            if (selectedRecipe != null && selectedRecipe.Material != 0) {
                RenderLabel(GetWeaponMaterialDescription(selectedRecipe.Material));
            } else if (selectedEnchantment != null && !string.IsNullOrEmpty(selectedEnchantment.Description)) {
                RenderLabel(selectedEnchantment.Description);
            }

            var dc = craftingData.MundaneEnhancementsStackable
                ? CalculateMundaneCraftingDC(craftingData, baseBlueprint, crafter.Descriptor)
                : CalculateMundaneCraftingDC(craftingData, baseBlueprint, crafter.Descriptor, selectedRecipe);
            if (RenderCraftingSkillInformation(crafter, StatType.SkillKnowledgeWorld, dc) < 0) {
                if (ModSettings.CraftingTakesNoTime) {
                    RenderLabel($"This project would be too hard for {crafter.CharacterName} if \"Crafting Takes No Time\" cheat was disabled.");
                } else {
                    RenderLabel($"This project will be too hard for {crafter.CharacterName}");
                    return;
                }
            }

            // Upgrading to a custom blueprint, rather than use the standard mithral/adamantine blueprints.
            var enchantments = selectedEnchantment == null ? Enumerable.Empty<string>() : new List<string> {selectedEnchantment.AssetGuid};
            var upgradeName = selectedRecipe != null && selectedRecipe.Material != 0
                ? new L10NString(selectedRecipe.NameId).ToString()
                : selectedEnchantment == null
                    ? null
                    : selectedEnchantment.Name;
            var name = upgradeName == null ? baseBlueprint.Name : $"{upgradeName} {baseBlueprint.Name}";
            var visual = ApplyVisualMapping(selectedRecipe, baseBlueprint);
            var itemGuid = selectedRecipe == null
                ? baseBlueprint.AssetGuid
                : BuildCustomRecipeItemGuid(baseBlueprint.AssetGuid, enchantments, null, name, null, null, selectedRecipe.Material, visual);
            var itemToCraft = selectedRecipe == null ? baseBlueprint : ResourcesLibrary.TryGetBlueprint<BlueprintItem>(itemGuid);

            if (!itemToCraft) {
                RenderLabel($"Error: null custom item from looking up blueprint ID {itemGuid}");
            } else {
                if (upgradingBlueprint != null && GUILayout.Button($"Cancel {baseBlueprint.Name}", GUILayout.ExpandWidth(false))) {
                    upgradingBlueprint = null;
                }

                if (craftingData.MundaneEnhancementsStackable) {
                    if (upgradeName != null && GUILayout.Button($"Add {upgradeName} to {baseBlueprint.Name}", GUILayout.ExpandWidth(false))) {
                        upgradingBlueprint = itemToCraft;
                    }

                    RenderRecipeBasedCraftItemControl(crafter, craftingData, null, 0, baseBlueprint);
                } else {
                    RenderRecipeBasedCraftItemControl(crafter, craftingData, selectedRecipe, 0, itemToCraft);
                }
            }

            RenderLabel($"Current Money: {Game.Instance.Player.Money}");
        }

        private static string GetWeaponMaterialDescription(PhysicalDamageMaterial material) {
            switch (material) {
                case PhysicalDamageMaterial.Silver:
                    return new L10NString("craftMagicItems-material-silver-weapon-description").ToString();
                case PhysicalDamageMaterial.ColdIron:
                    return new L10NString("craftMagicItems-material-coldIron-weapon-description").ToString();
                case PhysicalDamageMaterial.Adamantite:
                    return new L10NString("craftMagicItems-material-adamantite-weapon-description").ToString();
                default:
                    return "";
            }
        }

        private static string ApplyVisualMapping(RecipeData recipe, BlueprintItem blueprint) {
            if (recipe?.VisualMappings != null) {
                foreach (var mapping in recipe.VisualMappings) {
                    if (mapping.StartsWith(blueprint.AssetGuid)) {
                        return mapping.Split(':')[1];
                    }
                }
            }

            return null;
        }

        private static void RenderProjectsSection() {
            var caster = GetSelectedCrafter(false);
            if (caster == null) {
                return;
            }

            var timer = GetCraftingTimerComponentForCaster(caster.Descriptor);
            if (timer == null || timer.CraftingProjects.Count == 0) {
                RenderLabel($"{caster.CharacterName} is not currently working on any crafting projects.");
                return;
            }

            RenderLabel($"{caster.CharacterName} currently has {timer.CraftingProjects.Count} crafting projects in progress.");
            var firstItem = true;
            foreach (var project in timer.CraftingProjects.ToArray()) {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"   <b>{project.ResultItem.Name}</b> is {100 * project.Progress / project.TargetCost}% complete.  {project.LastMessage}",
                    GUILayout.Width(600f));
                if (GUILayout.Button("<color=red>✖</color>", GUILayout.ExpandWidth(false))) {
                    CancelCraftingProject(project);
                }

                if (firstItem) {
                    firstItem = false;
                } else if (GUILayout.Button("Move To Top", GUILayout.ExpandWidth(false))) {
                    timer.CraftingProjects.Remove(project);
                    timer.CraftingProjects.Insert(0, project);
                }

                GUILayout.EndHorizontal();
            }
        }

        private static void RenderFeatReassignmentSection() {
            var caster = GetSelectedCrafter(false);
            if (caster == null) {
                return;
            }

            var casterLevel = caster.Descriptor.Spellbooks.Aggregate(0, (highest, book) => book.CasterLevel > highest ? book.CasterLevel : highest);
            var missingFeats = itemCraftingData
                .Where(data => data.FeatGuid != null && !CasterHasFeat(caster, data.FeatGuid) && data.MinimumCasterLevel <= casterLevel)
                .ToArray();
            if (missingFeats.Length == 0) {
                RenderLabel($"{caster.CharacterName} does not currently qualify for any crafting feats.");
                return;
            }

            RenderLabel(
                "Use this section to reassign previous feat choices for this character to crafting feats.  <color=red>Warning:</color> This is a one-way assignment!");
            RenderSelection(ref selectedFeatToLearn, "Feat to learn",
                missingFeats.Select(data => new L10NString(data.NameId).ToString()).ToArray(), 6);
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
            RenderCheckbox(ref ModSettings.CraftingCostsNoGold, "Crafting costs no gold and no material components.");
            if (!ModSettings.CraftingCostsNoGold) {
                RenderSelection(ref selectedCustomPriceScaleIndex, "Crafting Cost: ", CraftingPriceStrings, 4);
                if (selectedCustomPriceScaleIndex == 2) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Custom Cost Factor: ", GUILayout.ExpandWidth(false));
                    ModSettings.CraftingPriceScale = GUILayout.HorizontalSlider(ModSettings.CraftingPriceScale * 100, 0, 500, GUILayout.Width(300)) / 100;
                    GUILayout.Label(Mathf.Round(ModSettings.CraftingPriceScale * 100).ToString(CultureInfo.InvariantCulture));
                    GUILayout.EndHorizontal();
                } else {
                    ModSettings.CraftingPriceScale = 1 + selectedCustomPriceScaleIndex;
                }

                if (selectedCustomPriceScaleIndex != 0) {
                    RenderLabel(
                        "<b>Note:</b> The sale price of custom crafted items will also be scaled by this factor, but vanilla items crafted by this mod" +
                        " will continue to use Owlcat's sale price, creating a price difference between the cost of crafting and sale price.");
                }
            }

            RenderCheckbox(ref ModSettings.IgnoreCraftingFeats, "Crafting does not require characters to take crafting feats.");
            RenderCheckbox(ref ModSettings.CraftingTakesNoTime, "Crafting takes no time to complete.");
            RenderCheckbox(ref ModSettings.CraftAtFullSpeedWhileAdventuring, "Characters craft at full speed while adventuring (instead of 25% speed).");
            RenderCheckbox(ref ModSettings.IgnorePlusTenItemMaximum, "Ignore the rule that limits arms and armor to a maximum of +10 equivalent.");
            RenderCheckbox(ref ModSettings.IgnoreFeatCasterLevelRestriction, "Ignore the crafting feat Caster Level prerequisites when learning feats.");
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

        private static UnitEntityData GetSelectedCrafter(bool render) {
            currentCaster = null;
            // Only allow remote companions if the player is in the capital.
            var remote = IsPlayerInCapital();
            var characters = UIUtility.GetGroup(remote).Where(character => character.IsPlayerFaction
                                                                           && !character.Descriptor.IsPet
                                                                           && !character.Descriptor.State.IsDead
                                                                           && !character.Descriptor.State.IsFinallyDead)
                .ToArray();
            if (characters.Length == 0) {
                if (render) {
                    RenderLabel("No living characters available.");
                }

                return null;
            }

            if (render) {
                var partyNames = characters.Select(entity => entity.CharacterName).ToArray();
                var indexBefore = selectedSpellcasterIndex;
                RenderSelection(ref selectedSpellcasterIndex, "Crafter: ", partyNames, 8);
                if (indexBefore != selectedSpellcasterIndex) {
                    upgradingBlueprint = null;
                }
            }

            return characters[selectedSpellcasterIndex];
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
            if (selectedCustomName.Trim().Length == 0) {
                selectedCustomName = null;
            }

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
            if (itemBlueprint is BlueprintItemShield shield) {
                AddItemIdForEnchantment(shield.ArmorComponent);
                AddItemIdForEnchantment(shield.WeaponComponent);
            } else if (itemBlueprint != null) {
                foreach (var enchantment in GetEnchantments(itemBlueprint)) {
                    if (!EnchantmentIdToItem.ContainsKey(enchantment.AssetGuid)) {
                        EnchantmentIdToItem[enchantment.AssetGuid] = new List<BlueprintItemEquipment>();
                    }

                    EnchantmentIdToItem[enchantment.AssetGuid].Add(itemBlueprint);
                }
            }
        }

        private static void AddRecipeForEnchantment(string enchantmentId, RecipeData recipe) {
            if (!EnchantmentIdToRecipe.ContainsKey(enchantmentId)) {
                EnchantmentIdToRecipe.Add(enchantmentId, new List<RecipeData>());
            }

            if (!EnchantmentIdToRecipe[enchantmentId].Contains(recipe)) {
                EnchantmentIdToRecipe[enchantmentId].Add(recipe);
            }
        }

        private static IEnumerable<BlueprintItemEquipment> FindItemBlueprintForEnchantmentId(string assetGuid) {
            return EnchantmentIdToItem.ContainsKey(assetGuid) ? EnchantmentIdToItem[assetGuid] : null;
        }

        private static bool CasterHasFeat(UnitEntityData caster, string featGuid) {
            if (featGuid != null) {
                var feat = ResourcesLibrary.TryGetBlueprint(featGuid) as BlueprintFeature;
                foreach (var feature in caster.Descriptor.Progression.Features) {
                    if (feature.Blueprint == feat) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string RandomBaseBlueprintId(ItemCraftingData itemData, Func<string, bool> selector = null) {
            var blueprintIds = selector == null ? itemData.NewItemBaseIDs : itemData.NewItemBaseIDs.Where(selector).ToArray();
            return blueprintIds[RandomGenerator.Next(blueprintIds.Length)];
        }

        private static void CraftItem(ItemEntity resultItem, ItemEntity upgradeItem = null) {
            if (upgradeItem != null) {
                Game.Instance.Player.Inventory.Remove(upgradeItem);
            }

            Game.Instance.Player.Inventory.Add(resultItem);

            if (resultItem is ItemEntityUsable usable) {
                switch (usable.Blueprint.Type) {
                    case UsableItemType.Scroll:
                        Game.Instance.UI.Common.UISound.Play(UISoundType.NewInformation);
                        break;
                    case UsableItemType.Potion:
                        Game.Instance.UI.Common.UISound.PlayItemSound(SlotAction.Take, resultItem, false);
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
            if (ModSettings.CraftingCostsNoGold) {
                cost = new L10NString("craftMagicItems-label-cost-free");
            } else {
                canAfford = (Game.Instance.Player.Money >= goldCost);
                var notAffordGold = canAfford ? "" : new L10NString("craftMagicItems-label-cost-gold-too-much");
                cost = L10NFormat("craftMagicItems-label-cost-gold", goldCost, notAffordGold);
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

                    cost += L10NFormat("craftMagicItems-label-cost-gold-and-items", pair.Value, pair.Key.Name, notAffordItems);
                }
            }

            return canAfford;
        }

        private static void AddNewProject(UnitDescriptor casterDescriptor, CraftingProjectData project) {
            var craftingProjects = GetCraftingTimerComponentForCaster(casterDescriptor, true);
            craftingProjects.AddProject(project);
            if (project.UpgradeItem == null) {
                ItemCreationProjects.Add(project);
            } else {
                ItemUpgradeProjects[project.UpgradeItem] = project;
            }
        }

        private static void CalculateProjectEstimate(CraftingProjectData project) {
            var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
            StatType craftingSkill;
            int dc;
            int progressRate;
            if (IsMundaneCraftingData(craftingData)) {
                craftingSkill = StatType.SkillKnowledgeWorld;
                var recipeBasedItemCraftingData = (RecipeBasedItemCraftingData) craftingData;
                dc = CalculateMundaneCraftingDC(recipeBasedItemCraftingData, project.ResultItem.Blueprint, project.Crafter.Descriptor);
                progressRate = MundaneCraftingProgressPerDay;
            } else {
                craftingSkill = StatType.SkillKnowledgeArcana;
                dc = 5 + project.CasterLevel;
                progressRate = MagicCraftingProgressPerDay;
            }

            var skillMargin = RenderCraftingSkillInformation(project.Crafter, craftingSkill, dc, project.CasterLevel, project.Prerequisites,
                project.AnyPrerequisite, project.CrafterPrerequisites, false);
            var progressPerDayCapital = (int) (progressRate * (1 + (float) skillMargin / 5));
            GameLogContext.Count = (project.TargetCost + progressPerDayCapital - 1) / progressPerDayCapital;
            if (ModSettings.CraftAtFullSpeedWhileAdventuring) {
                project.AddMessage(new L10NString("craftMagicItems-time-estimate-single-rate"));
            } else {
                var progressPerDayAdventuring = (int) (progressRate * (1 + (float) skillMargin / 5) / AdventuringProgressPenalty);
                var adventuringDayCount = (project.TargetCost + progressPerDayAdventuring - 1) / progressPerDayAdventuring;
                project.AddMessage(adventuringDayCount == 1
                    ? new L10NString("craftMagicItems-time-estimate-one-day")
                    : L10NFormat("craftMagicItems-time-estimate-adventuring-capital", adventuringDayCount));
            }

            AddBattleLogMessage(project.LastMessage);
        }

        private static void RenderSpellBasedCraftItemControl(UnitEntityData caster, SpellBasedItemCraftingData craftingData, AbilityData spell,
            BlueprintAbility spellBlueprint, int spellLevel, int casterLevel) {
            var itemBlueprintList = FindItemBlueprintsForSpell(spellBlueprint, craftingData.UsableItemType);
            if (itemBlueprintList == null && craftingData.NewItemBaseIDs == null) {
                GUILayout.Label(L10NFormat("craftMagicItems-label-no-item-exists", new L10NString(craftingData.NamePrefixId), spellBlueprint.Name));
                return;
            }

            var existingItemBlueprint = itemBlueprintList?.Find(bp => bp.SpellLevel == spellLevel && bp.CasterLevel == casterLevel);
            var requiredProgress = CalculateSpellBasedGoldCost(craftingData, spellLevel, casterLevel);
            var goldCost = (int) Mathf.Round(requiredProgress * ModSettings.CraftingPriceScale);
            var canAfford = BuildCostString(out var cost, craftingData, goldCost, spellBlueprint);
            var custom = (existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength)
                ? new L10NString("craftMagicItems-label-custom").ToString()
                : "";
            var label = L10NFormat("craftMagicItems-label-craft-spell-item", custom, new L10NString(craftingData.NamePrefixId), spellBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                BlueprintItem itemBlueprint;
                if (itemBlueprintList == null) {
                    // No items for that spell exist at all - create a custom blueprint with casterLevel, spellLevel and spellId
                    var blueprintId = BuildCustomSpellItemGuid(RandomBaseBlueprintId(craftingData), casterLevel, spellLevel, spellBlueprint.AssetGuid);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else if (existingItemBlueprint == null) {
                    // No item for this spell & caster level - create a custom blueprint with casterLevel and optionally SpellLevel
                    var blueprintId = BuildCustomSpellItemGuid(itemBlueprintList[0].AssetGuid, casterLevel,
                        itemBlueprintList[0].SpellLevel == spellLevel ? -1 : spellLevel);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else {
                    // Item with matching spell, level and caster level exists.  Use that.
                    itemBlueprint = existingItemBlueprint;
                }

                if (itemBlueprint == null) {
                    throw new Exception(
                        $"Unable to build blueprint for spellId {spellBlueprint.AssetGuid}, spell level {spellLevel}, caster level {casterLevel}");
                }

                // Pay gold and material components up front.
                if (ModSettings.CraftingCostsNoGold) {
                    goldCost = 0;
                } else {
                    Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                    Game.Instance.Player.SpendMoney(goldCost);
                    if (spellBlueprint.MaterialComponent.Item != null) {
                        Game.Instance.Player.Inventory.Remove(spellBlueprint.MaterialComponent.Item,
                            spellBlueprint.MaterialComponent.Count * craftingData.Charges);
                    }
                }

                var resultItem = BuildItemEntity(itemBlueprint, craftingData);
                AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-begin-crafting", cost, itemBlueprint.Name), resultItem);
                if (ModSettings.CraftingTakesNoTime) {
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-expend-spell", spell.Name));
                    spell.SpendFromSpellbook();
                    CraftItem(resultItem);
                } else {
                    var project = new CraftingProjectData(caster, requiredProgress, goldCost, casterLevel, resultItem, craftingData.Name, null,
                        new[] {spellBlueprint});
                    AddNewProject(caster.Descriptor, project);
                    CalculateProjectEstimate(project);
                    currentSection = OpenSection.ProjectsSection;
                }
            }
        }

        private static bool IsMundaneCraftingData(ItemCraftingData craftingData) {
            return craftingData.FeatGuid == null;
        }

        private static void RenderRecipeBasedCraftItemControl(UnitEntityData caster, ItemCraftingData craftingData, RecipeData recipe, int casterLevel,
            BlueprintItem itemBlueprint, ItemEntity upgradeItem = null) {
            var requiredProgress = (itemBlueprint.Cost - (upgradeItem?.Blueprint.Cost ?? 0)) / 4;
            var goldCost = (int) Mathf.Round(requiredProgress * ModSettings.CraftingPriceScale);
            if (IsMundaneCraftingData(craftingData)) {
                // For mundane crafting, the gold cost is less, and the cost of the recipes don't increase the required progress.
                goldCost = Math.Max(1, (goldCost * 2 + 2) / 3);
                var recipeCost = 0;
                foreach (var enchantment in itemBlueprint.Enchantments) {
                    var enchantmentRecipe = FindSourceRecipe(enchantment.AssetGuid, itemBlueprint);
                    recipeCost += enchantmentRecipe?.CostFactor ?? 0;
                }

                if (itemBlueprint is BlueprintItemWeapon weapon && weapon.DamageType.Physical.Material != 0) {
                    recipeCost += GetSpecialMaterialCost(weapon.DamageType.Physical.Material, weapon);
                }

                requiredProgress = Math.Max(1, requiredProgress - recipeCost / 4);
            }

            var canAfford = BuildCostString(out var cost, craftingData, goldCost, recipe?.PrerequisiteSpells ?? new BlueprintAbility[0]);
            var custom = (itemBlueprint.AssetGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength)
                ? new L10NString("craftMagicItems-label-custom").ToString()
                : "";
            var label = upgradeItem == null
                ? L10NFormat("craftMagicItems-label-craft-item", custom, itemBlueprint.Name, cost)
                : L10NFormat("craftMagicItems-label-upgrade-item", upgradeItem.Blueprint.Name, custom, itemBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                // Pay gold and material components up front.
                if (ModSettings.CraftingCostsNoGold) {
                    goldCost = 0;
                } else {
                    Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                    Game.Instance.Player.SpendMoney(goldCost);
                    var factor = GetMaterialComponentMultiplier(craftingData);
                    if (factor > 0 && recipe != null) {
                        foreach (var prerequisite in recipe.PrerequisiteSpells) {
                            if (prerequisite.MaterialComponent.Item != null) {
                                Game.Instance.Player.Inventory.Remove(prerequisite.MaterialComponent.Item, prerequisite.MaterialComponent.Count * factor);
                            }
                        }
                    }
                }

                var resultItem = BuildItemEntity(itemBlueprint, craftingData);
                AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-begin-crafting", cost, itemBlueprint.Name), resultItem);
                if (ModSettings.CraftingTakesNoTime) {
                    CraftItem(resultItem, upgradeItem);
                } else {
                    var project = new CraftingProjectData(caster, requiredProgress, goldCost, casterLevel, resultItem, craftingData.Name, recipe?.Name,
                        recipe?.PrerequisiteSpells ?? new BlueprintAbility[0], recipe?.AnyPrerequisite ?? false, upgradeItem,
                        recipe?.CrafterPrerequisites ?? new CrafterPrerequisiteType[0]);
                    AddNewProject(caster.Descriptor, project);
                    CalculateProjectEstimate(project);
                    currentSection = OpenSection.ProjectsSection;
                }

                // Reset base GUID for next item
                selectedBaseGuid = null;
                // And stop upgrading the item, if relevant.
                upgradingBlueprint = null;
            }
        }

        private static LocalizedString BuildCustomRecipeItemDescription(BlueprintItem blueprint, IEnumerable<BlueprintItemEnchantment> enchantments,
            IList<BlueprintItemEnchantment> removed) {
            var allKnown = blueprint.Enchantments.All(enchantment => EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid));
            var description = allKnown
                ? new L10NString("craftMagicItems-custom-description-start")
                : blueprint.Description + new L10NString("craftMagicItems-custom-description-additional");
            foreach (var enchantment in allKnown ? blueprint.Enchantments : enchantments) {
                var recipe = FindSourceRecipe(enchantment.AssetGuid, blueprint);
                if (!string.IsNullOrEmpty(enchantment.Name)) {
                    description += "\n * " + enchantment.Name;
                } else if (recipe.Enchantments.Length > 1) {
                    var bonus = (recipe.Enchantments.IndexOf(enchantment) + 1) * (recipe.BonusMultiplier > 0 ? recipe.BonusMultiplier : 1);
                    var bonusString = recipe.BonusTypeId != null
                        ? L10NFormat("craftMagicItems-custom-description-bonus-to", new L10NString(recipe.BonusTypeId), new L10NString(recipe.NameId))
                        : recipe.BonusToId != null
                            ? L10NFormat("craftMagicItems-custom-description-bonus-to", new L10NString(recipe.NameId), new L10NString(recipe.BonusToId))
                            : L10NFormat("craftMagicItems-custom-description-bonus", new L10NString(recipe.NameId));
                    var upgradeFrom = allKnown ? null : removed.FirstOrDefault(remove => FindSourceRecipe(remove.AssetGuid, blueprint) == recipe);
                    if (upgradeFrom == null) {
                        description += "\n * " + L10NFormat("craftMagicItems-custom-description-enchantment-template", bonus, bonusString);
                    } else {
                        var oldBonus = recipe.Enchantments.IndexOf(upgradeFrom) + 1;
                        description += "\n * " + L10NFormat("craftMagicItems-custom-description-enchantment-upgrade-template", bonusString, oldBonus, bonus);
                    }
                } else {
                    description += "\n * " + new L10NString(recipe.NameId);
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

        private static string BuildCustomRecipeItemGuid(string originalGuid, IEnumerable<string> enchantments, string[] remove = null, string name = null,
            string ability = null, string activatableAbility = null, PhysicalDamageMaterial material = 0, string visual = null) {
            if (originalGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength) {
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

                        if (enchantmentsList.Count == 0) {
                            return originalGuid;
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

                    // Remove and original customisation.
                    originalGuid = originalGuid.Substring(0, match.Index) + originalGuid.Substring(match.Index + match.Length);
                }
            }

            return $"{originalGuid}{BlueprintPrefix}(enchantments=({enchantments.Join(null, ";")})" +
                   $"{(remove == null || remove.Length == 0 ? "" : ",remove=" + remove.Join(null, ";"))}" +
                   $"{(name == null ? "" : $",name={name.Replace('✔', '_')}✔")}" +
                   $"{(ability == null ? "" : $",ability={ability}")}" +
                   $"{(activatableAbility == null ? "" : $",activatableAbility={activatableAbility}")}" +
                   $"{(material == 0 ? "" : $",material={material}")}" +
                   $"{(visual == null ? "" : $",visual={visual}")}" +
                   ")";
        }

        private static string BuildCustomComponentsItemGuid(string originalGuid, string[] values, string nameId, string descriptionId) {
            var components = "";
            for (var index = 0; index < values.Length; index += 3) {
                components += $"{(index > 0 ? "," : "")}Component[{values[index]}]{values[index + 1]}={values[index + 2]}";
            }

            return
                $"{originalGuid}{BlueprintPrefix}({components}{(nameId == null ? "" : $",nameId={nameId}")}{(descriptionId == null ? "" : $",descriptionId={descriptionId}")})";
        }

        private static string BuildCustomFeatGuid(string originalGuid, string feat) {
            return $"{originalGuid}{BlueprintPrefix}(feat={feat})";
        }

        private static string ApplyTimerBlueprintPatch(BlueprintBuff blueprint) {
            blueprint.ComponentsArray = new BlueprintComponent[] {ScriptableObject.CreateInstance<CraftingTimerComponent>()};
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
            var prerequisite = ScriptableObject.CreateInstance<PrerequisiteCasterLevel>();
            var featGuid = BuildCustomFeatGuid(blueprint.AssetGuid, feat);
            var itemData = itemCraftingData.First(data => data.FeatGuid == featGuid);
            prerequisite.SetPrerequisiteCasterLevel(itemData.MinimumCasterLevel);
            blueprint.ComponentsArray = new BlueprintComponent[] {prerequisite};
            return featGuid;
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
                blueprint.Ability = (BlueprintAbility) ResourcesLibrary.TryGetBlueprint(spellId);
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

        private static int ItemPlus(BlueprintItem blueprint) {
            switch (blueprint) {
                case BlueprintItemWeapon weapon:
                    foreach (var enchantment in weapon.Enchantments) {
                        var weaponBonus = enchantment.GetComponent<WeaponEnhancementBonus>();
                        if (weaponBonus != null) {
                            return weaponBonus.EnhancementBonus;
                        }
                    }

                    break;
                case BlueprintItemArmor armour:
                    foreach (var enchantment in armour.Enchantments) {
                        var armourBonus = enchantment.GetComponent<ArmorEnhancementBonus>();
                        if (armourBonus != null) {
                            return armourBonus.EnhancementValue;
                        }
                    }

                    break;
                case BlueprintItemShield shield:
                    return Math.Max(ItemPlus(shield.ArmorComponent), ItemPlus(shield.WeaponComponent));
            }

            return 0;
        }

        private static int ItemPlusEquivalent(BlueprintItem blueprint) {
            if (blueprint == null || blueprint.Enchantments == null) {
                return 0;
            }

            var enhancementLevel = 0;
            var cumulative = new Dictionary<RecipeData, int>();
            foreach (var enchantment in blueprint.Enchantments) {
                if (EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid)) {
                    var recipe = FindSourceRecipe(enchantment.AssetGuid, blueprint);
                    if (recipe != null && recipe.CostType == RecipeCostType.EnhancementLevelSquared) {
                        var level = recipe.Enchantments.IndexOf(enchantment) + 1;
                        if (recipe.EnchantmentsCumulative) {
                            cumulative[recipe] = cumulative.ContainsKey(recipe) ? Math.Max(level, cumulative[recipe]) : level;
                        } else {
                            enhancementLevel += recipe.CostFactor * level;
                        }
                    }
                }
            }

            foreach (var recipeLevelPair in cumulative) {
                enhancementLevel += recipeLevelPair.Key.CostFactor * recipeLevelPair.Value;
            }

            return enhancementLevel;
        }

        private static bool IsItemLegalEnchantmentLevel(BlueprintItem blueprint) {
            if (blueprint == null || ModSettings.IgnorePlusTenItemMaximum) {
                return true;
            }

            if (blueprint is BlueprintItemShield shield) {
                return IsItemLegalEnchantmentLevel(shield.ArmorComponent) && IsItemLegalEnchantmentLevel(shield.WeaponComponent);
            }

            var plusEquivalent = ItemPlusEquivalent(blueprint);
            return plusEquivalent <= 10;
        }

        private static int GetEnchantmentCost(string enchantmentId, BlueprintItem blueprint) {
            var recipe = FindSourceRecipe(enchantmentId, blueprint);
            if (recipe != null) {
                var index = recipe.Enchantments.FindIndex(enchantment => enchantment.AssetGuid == enchantmentId);
                switch (recipe.CostType) {
                    case RecipeCostType.CasterLevel:
                        return recipe.CostFactor * (recipe.CasterLevelStart + index * recipe.CasterLevelMultiplier);
                    case RecipeCostType.LevelSquared:
                        return recipe.CostFactor * (index + 1) * (index + 1);
                    default:
                        return 0;
                }
            }

            return EnchantmentIdToCost.ContainsKey(enchantmentId) ? EnchantmentIdToCost[enchantmentId] : 0;
        }

        private static int GetSpecialMaterialCost(PhysicalDamageMaterial material, BlueprintItemWeapon weapon) {
            switch (material) {
                case PhysicalDamageMaterial.Adamantite:
                    return 3000 - MasterworkCost; // Cost of masterwork is subsumed by the cost of adamantite
                case PhysicalDamageMaterial.ColdIron:
                    var enhancementLevel = ItemPlusEquivalent(weapon);
                    var baseWeapon =
                        ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(weapon.AssetGuid.Substring(0, CustomBlueprintBuilder.VanillaAssetIdLength));
                    // Silver weapons cost double, including the masterwork component and for enchanting the first +1
                    return (baseWeapon == null ? 0 : baseWeapon.Cost) +
                           (enhancementLevel > 0 ? WeaponPlusCost + MasterworkCost : IsMasterwork(weapon) ? MasterworkCost : 0);
                case PhysicalDamageMaterial.Silver when weapon.Category.HasSubCategory(WeaponSubCategory.Light):
                    return 20;
                case PhysicalDamageMaterial.Silver when weapon.Category.HasSubCategory(WeaponSubCategory.TwoHanded) || weapon.Double:
                    return 180;
                case PhysicalDamageMaterial.Silver:
                    return 90;
                default:
                    return 0;
            }
        }

        private static int RulesRecipeItemCost(BlueprintItem blueprint) {
            if (blueprint == null) {
                return 0;
            }

            if (blueprint is BlueprintItemShield shield) {
                return RulesRecipeItemCost(shield.ArmorComponent) + RulesRecipeItemCost(shield.WeaponComponent);
            }

            var mostExpensiveEnchantmentCost = 0;
            var cost = 0;
            foreach (var enchantment in blueprint.Enchantments) {
                var recipe = FindSourceRecipe(enchantment.AssetGuid, blueprint);
                if (recipe != null && recipe.CostType != RecipeCostType.EnhancementLevelSquared && RecipeAppliesToBlueprint(recipe, blueprint)) {
                    var enchantmentCost = GetEnchantmentCost(enchantment.AssetGuid, blueprint);
                    cost += enchantmentCost;
                    if (mostExpensiveEnchantmentCost < enchantmentCost) {
                        mostExpensiveEnchantmentCost = enchantmentCost;
                    }
                }
            }

            if (blueprint is BlueprintItemArmor || blueprint is BlueprintItemWeapon) {
                if (blueprint is BlueprintItemWeapon weapon && weapon.DamageType.Physical.Material != 0) {
                    cost += GetSpecialMaterialCost(weapon.DamageType.Physical.Material, weapon);
                }

                var enhancementLevel = ItemPlusEquivalent(blueprint);
                var factor = blueprint is BlueprintItemWeapon ? WeaponPlusCost : ArmourPlusCost;
                return cost + enhancementLevel * enhancementLevel * factor;
            }

            // Usable (belt slot) items cost double.
            return (3 * cost - mostExpensiveEnchantmentCost) / (blueprint is BlueprintItemEquipmentUsable ? 1 : 2);
        }

        private static string ApplyRecipeItemBlueprintPatch(BlueprintItemEquipment blueprint, Match match) {
            var priceDelta = blueprint.Cost - RulesRecipeItemCost(blueprint);
            if (blueprint is BlueprintItemShield shield) {
                var armourComponentClone = Object.Instantiate(shield.ArmorComponent);
                ApplyRecipeItemBlueprintPatch(armourComponentClone, match);
                Traverse.Create(shield).Field("m_ArmorComponent").SetValue(armourComponentClone);
                if (shield.WeaponComponent) {
                    var weaponComponentClone = Object.Instantiate(shield.WeaponComponent);
                    ApplyRecipeItemBlueprintPatch(weaponComponentClone, match);
                    Traverse.Create(shield).Field("m_WeaponComponent").SetValue(weaponComponentClone);
                }
            }

            var initiallyMundane = blueprint.Enchantments.Count == 0 && blueprint.Ability == null && blueprint.ActivatableAbility == null;

            // Copy Enchantments so we leave base blueprint alone
            var enchantmentsCopy = blueprint.Enchantments.ToList();
            Traverse.Create(blueprint).Field("m_CachedEnchantments").SetValue(enchantmentsCopy);
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
            }

            string activatableAbility = null;
            if (match.Groups["activatableAbility"].Success) {
                activatableAbility = match.Groups["activatableAbility"].Value;
                blueprint.ActivatableAbility = activatableAbility == "null"
                    ? null
                    : ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(activatableAbility);
            }

            if (!initiallyMundane && enchantmentsCopy.Count == 0 && blueprint.Ability == null && blueprint.ActivatableAbility == null) {
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

                    if (!(blueprint is BlueprintItemShield) && (GetItemType(blueprint) != ItemsFilter.ItemType.Shield
                                                                || FindSourceRecipe(guid, blueprint) != null)) {
                        enchantmentsCopy.Add(enchantment);
                    }
                }
            }

            PhysicalDamageMaterial material = 0;
            if (match.Groups["material"].Success && blueprint is BlueprintItemWeapon weapon) {
                Enum.TryParse(match.Groups["material"].Value, out material);
                Traverse.Create(weapon).Field("m_DamageType").SetValue(TraverseCloneAndSetField(weapon.DamageType, "Physical.Material", material.ToString()));
                Traverse.Create(weapon).Field("m_OverrideDamageType").SetValue(true);
            }

            string visual = null;
            if (match.Groups["visual"].Success) {
                visual = match.Groups["visual"].Value;
                // Copy icon from a different item
                var copyFromBlueprint = visual == "null" ? null : ResourcesLibrary.TryGetBlueprint<BlueprintItem>(visual);
                var iconSprite = copyFromBlueprint == null ? null : copyFromBlueprint.Icon;
                Traverse.Create(blueprint).Field("m_Icon").SetValue(iconSprite);
                if (blueprint is BlueprintItemEquipmentHand && copyFromBlueprint is BlueprintItemEquipmentHand equipmentHand) {
                    Traverse.Create(blueprint).Field("m_VisualParameters").SetValue(equipmentHand.VisualParameters);
                } else if (blueprint is BlueprintItemArmor && copyFromBlueprint is BlueprintItemArmor armour) {
                    Traverse.Create(blueprint).Field("m_VisualParameters").SetValue(armour.VisualParameters);
                }
            }

            string name = null;
            if (match.Groups["name"].Success) {
                name = match.Groups["name"].Value;
                Traverse.Create(blueprint).Field("m_DisplayNameText").SetValue(new FakeL10NString(name));
            }

            if (!SlotsWhichShowEnchantments.Contains(blueprint.ItemType)) {
                Traverse.Create(blueprint).Field("m_DescriptionText")
                    .SetValue(BuildCustomRecipeItemDescription(blueprint, enchantmentsForDescription, removed));
                Traverse.Create(blueprint).Field("m_FlavorText").SetValue(new L10NString(""));
            }

            Traverse.Create(blueprint).Field("m_Cost").SetValue(RulesRecipeItemCost(blueprint) + priceDelta);
            return BuildCustomRecipeItemGuid(blueprint.AssetGuid, enchantmentIds, removedIds, name, ability, activatableAbility, material, visual);
        }

        private static T CloneObject<T>(T originalObject) {
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

        private static T TraverseCloneAndSetField<T>(T original, string field, string value) where T : class {
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
                // Strip leading . of field
                if (field.StartsWith(".")) {
                    field = field.Substring(1);
                }
            }

            var clone = CloneObject(original);
            var fieldNameEnd = field.IndexOf('.');
            if (fieldNameEnd < 0) {
                var fieldAccess = Traverse.Create(clone).Field(field);
                if (!fieldAccess.FieldExists()) {
                    throw new Exception(
                        $"Field {field} does not exist on original of type {clone.GetType().FullName}, available fields: {Traverse.Create(clone).Fields().Join()}");
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
                    var fieldAccess = Traverse.Create(clone).Field(thisField);
                    if (!fieldAccess.FieldExists()) {
                        throw new Exception(
                            $"Field {thisField} does not exist on original of type {clone.GetType().FullName}, available fields: {Traverse.Create(clone).Fields().Join()}");
                    }

                    if (fieldAccess.GetValueType().IsArray) {
                        throw new Exception($"Field {thisField} is an array but overall access {field} did not index the array");
                    }

                    fieldAccess.SetValue(TraverseCloneAndSetField(fieldAccess.GetValue(), remainingFields, value));
                } else {
                    var index = int.Parse(new string(thisField.Skip(arrayPos + 1).TakeWhile(char.IsDigit).ToArray()));
                    thisField = field.Substring(0, arrayPos);
                    var fieldAccess = Traverse.Create(clone).Field(thisField);
                    if (!fieldAccess.FieldExists()) {
                        throw new Exception(
                            $"Field {thisField} does not exist on original of type {clone.GetType().FullName}, available fields: {Traverse.Create(clone).Fields().Join()}");
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

        private static string ApplyItemEnchantmentBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
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
                Traverse.Create(blueprint).Field("m_EnchantName").SetValue(new L10NString(nameId));
            }

            string descriptionId = null;
            if (match.Groups["descriptionId"].Success) {
                descriptionId = match.Groups["descriptionId"].Value;
                Traverse.Create(blueprint).Field("m_Description").SetValue(new L10NString(descriptionId));
            }

            return BuildCustomComponentsItemGuid(blueprint.AssetGuid, values.ToArray(), nameId, descriptionId);
        }

        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again, or null if we couldn't change the
        // blueprint.
        private static string ApplyBlueprintPatch(BlueprintScriptableObject blueprint, Match match) {
            string result;
            if (match.Groups["timer"].Success) {
                result = ApplyTimerBlueprintPatch((BlueprintBuff) blueprint);
            } else if (match.Groups["feat"].Success) {
                result = ApplyFeatBlueprintPatch((BlueprintFeature) blueprint, match);
            } else if (match.Groups["casterLevel"].Success) {
                result = ApplySpellItemBlueprintPatch((BlueprintItemEquipmentUsable) blueprint, match);
            } else if (match.Groups["enchantments"].Success) {
                result = ApplyRecipeItemBlueprintPatch((BlueprintItemEquipment) blueprint, match);
            } else if (match.Groups["components"].Success) {
                result = ApplyItemEnchantmentBlueprintPatch(blueprint, match);
            } else {
                throw new Exception($"Match of assetId {match.Value} didn't matching any sub-type");
            }

            return result;
        }

        // Attempt to work out the cost of enchantments which aren't in recipes by checking if blueprint, which contains the enchantment, contains only other
        // enchantments whose cost is know.
        private static bool ReverseEngineerEnchantmentCost(BlueprintItemEquipment blueprint, string enchantmentId) {
            if (blueprint == null || blueprint.IsNotable || blueprint.Ability != null || blueprint.ActivatableAbility != null) {
                return false;
            }

            if (blueprint is BlueprintItemShield || blueprint is BlueprintItemWeapon || blueprint is BlueprintItemArmor) {
                // Cost of enchantments on arms and armor is different, and can be treated as a straight delta.
                return true;
            }

            var mostExpensiveEnchantmentCost = 0;
            var costSum = 0;
            foreach (var enchantment in blueprint.Enchantments) {
                if (enchantment.AssetGuid == enchantmentId) {
                    continue;
                }

                if (!EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid) && !EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid)) {
                    return false;
                }

                var enchantmentCost = GetEnchantmentCost(enchantment.AssetGuid, blueprint);
                costSum += enchantmentCost;
                if (mostExpensiveEnchantmentCost < enchantmentCost) {
                    mostExpensiveEnchantmentCost = enchantmentCost;
                }
            }

            var remainder = blueprint.Cost - 3 * costSum / 2;
            if (remainder >= mostExpensiveEnchantmentCost) {
                // enchantmentId is the most expensive enchantment
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
                        recipeBased.Recipes = recipeBased.RecipeFileNames.Aggregate(Enumerable.Empty<RecipeData>(),
                            (all, fileName) => all.Concat(ReadJsonFile<RecipeData[]>($"{ModEntry.Path}/Data/{fileName}"))
                        ).ToArray();
                        foreach (var recipe in recipeBased.Recipes) {
                            foreach (var enchantment in recipe.Enchantments) {
                                AddRecipeForEnchantment(enchantment.AssetGuid, recipe);
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

                    if (itemData.ParentNameId != null) {
                        if (!SubCraftingData.ContainsKey(itemData.ParentNameId)) {
                            SubCraftingData[itemData.ParentNameId] = new List<ItemCraftingData>();
                        }

                        SubCraftingData[itemData.ParentNameId].Add(itemData);
                    }
                }

                var allUsableItems = Resources.FindObjectsOfTypeAll<BlueprintItemEquipment>();
                foreach (var item in allUsableItems) {
                    AddItemIdForEnchantment(item);
                }

                var allNonRecipeEnchantmentsInItems = Resources.FindObjectsOfTypeAll<BlueprintEquipmentEnchantment>()
                    .Where(enchantment => !EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid) && EnchantmentIdToItem.ContainsKey(enchantment.AssetGuid))
                    .ToArray();
                // BlueprintEnchantment.EnchantmentCost seems to be full of nonsense values - attempt to set cost of each enchantment by using the prices of
                // items with enchantments.
                foreach (var enchantment in allNonRecipeEnchantmentsInItems) {
                    var itemsWithEnchantment = EnchantmentIdToItem[enchantment.AssetGuid];
                    foreach (var item in itemsWithEnchantment) {
                        if (DoesItemMatchEnchantments(item, enchantment.AssetGuid)) {
                            EnchantmentIdToCost[enchantment.AssetGuid] = item.Cost;
                            break;
                        }
                    }
                }

                foreach (var enchantment in allNonRecipeEnchantmentsInItems) {
                    if (!EnchantmentIdToCost.ContainsKey(enchantment.AssetGuid)) {
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
                                    if (data.FeatGuid != null) {
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

            [HarmonyPriority(Priority.Last)]
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

        // Fix issue in Owlcat's UI - ActionBarManager.Update does not refresh the Groups (spells/Actions/Belt)
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

        // Owlcat's code doesn't correctly detect that a variant spell is in a spellList when its parent spell is. 
        [HarmonyPatch(typeof(BlueprintAbility), "IsInSpellList")]
        public static class BlueprintAbilityIsInSpellListPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(BlueprintAbility __instance, BlueprintSpellList spellList, ref bool __result) {
                if (!__result && __instance.Parent != null && __instance.Parent != __instance) {
                    __result = __instance.Parent.IsInSpellList(spellList);
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

        private static AbilityData FindCasterSpell(UnitDescriptor caster, BlueprintAbility spellBlueprint, bool mustHavePrepared,
            IReadOnlyCollection<AbilityData> spellsToCast) {
            foreach (var spellbook in caster.Spellbooks) {
                var spellLevel = spellbook.GetSpellLevel(spellBlueprint);
                if (spellLevel > spellbook.MaxSpellLevel || spellLevel < 0) {
                    continue;
                }

                if (mustHavePrepared && spellLevel > 0) {
                    if (spellbook.Blueprint.Spontaneous) {
                        // Count how many other spells of this class and level they're going to cast, to ensure they don't double-dip on spell slots.
                        var toCastCount = spellsToCast.Count(ability => ability.Spellbook == spellbook && spellbook.GetSpellLevel(ability) == spellLevel);
                        // Spontaneous spellcaster must have enough spell slots of the required level.
                        if (spellbook.GetSpontaneousSlots(spellLevel) <= toCastCount) {
                            continue;
                        }
                    } else {
                        // Prepared spellcaster must have memorized the spell...
                        var spellSlot = spellbook.GetMemorizedSpells(spellLevel).FirstOrDefault(slot =>
                            slot.Available && (slot.Spell?.Blueprint == spellBlueprint ||
                                               spellBlueprint.Parent && slot.Spell?.Blueprint == spellBlueprint.Parent));
                        if (spellSlot == null && (spellbook.GetSpontaneousConversionSpells(spellLevel).Contains(spellBlueprint) ||
                                                  (spellBlueprint.Parent &&
                                                   spellbook.GetSpontaneousConversionSpells(spellLevel).Contains(spellBlueprint.Parent)))) {
                            // ... or be able to convert, in which case any available spell of the given level will do.
                            spellSlot = spellbook.GetMemorizedSpells(spellLevel).FirstOrDefault(slot => slot.Available);
                        }

                        if (spellSlot == null) {
                            continue;
                        }

                        return spellSlot.Spell;
                    }
                }

                return spellbook.GetKnownSpells(spellLevel).Concat(spellbook.GetSpecialSpells(spellLevel))
                    .First(known => known.Blueprint == spellBlueprint ||
                                    (spellBlueprint.Parent && known.Blueprint == spellBlueprint.Parent));
            }

            // Try casting the spell from an item
            ItemEntity fromItem = null;
            var fromItemCharges = 0;
            foreach (var item in caster.Inventory) {
                // Check (non-potion) items wielded by the caster to see if they can cast the required spell
                if (item.Wielder == caster && (!(item.Blueprint is BlueprintItemEquipmentUsable usable) || usable.Type != UsableItemType.Potion)
                                           && (item.Ability?.Blueprint == spellBlueprint ||
                                               (spellBlueprint.Parent && item.Ability?.Blueprint == spellBlueprint.Parent))) {
                    // Choose the item with the most available charges, with a multiplier if the item restores charges on rest.
                    var charges = item.Charges * (((BlueprintItemEquipment) item.Blueprint).RestoreChargesOnRest ? 50 : 1);
                    if (charges > fromItemCharges) {
                        fromItem = item;
                        fromItemCharges = charges;
                    }
                }
            }

            return fromItem?.Ability?.Data;
        }

        private static int CheckSpellPrerequisites(CraftingProjectData project, UnitDescriptor caster, bool mustPrepare,
            out List<BlueprintAbility> missingSpells, out List<AbilityData> spellsToCast) {
            return CheckSpellPrerequisites(project.Prerequisites, project.AnyPrerequisite, caster, mustPrepare, out missingSpells, out spellsToCast);
        }

        private static int CheckSpellPrerequisites(BlueprintAbility[] prerequisites, bool anyPrerequisite, UnitDescriptor caster, bool mustPrepare,
            out List<BlueprintAbility> missingSpells, out List<AbilityData> spellsToCast) {
            spellsToCast = new List<AbilityData>();
            missingSpells = new List<BlueprintAbility>();
            if (prerequisites != null) {
                foreach (var spellBlueprint in prerequisites) {
                    var spell = FindCasterSpell(caster, spellBlueprint, mustPrepare, spellsToCast);
                    if (spell != null) {
                        spellsToCast.Add(spell);
                        if (anyPrerequisite) {
                            missingSpells.Clear();
                            return 0;
                        }
                    } else {
                        missingSpells.Add(spellBlueprint);
                    }
                }
            }

            return anyPrerequisite ? Math.Min(1, missingSpells.Count) : missingSpells.Count;
        }

        private static int CheckCrafterPrerequisites(CraftingProjectData project, UnitDescriptor caster) {
            var missing = GetMissingCrafterPrerequisites(project.CrafterPrerequisites, caster);
            foreach (var prerequisite in missing) {
                AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-missing-crafter-prerequisite",
                    new L10NString($"craftMagicItems-crafter-prerequisite-{prerequisite}"), MissingPrerequisiteDCModifier));
            }

            return missing.Count;
        }

        private static List<CrafterPrerequisiteType> GetMissingCrafterPrerequisites(CrafterPrerequisiteType[] prerequisites, UnitDescriptor caster) {
            var missingCrafterPrerequisites = new List<CrafterPrerequisiteType>();
            if (prerequisites != null) {
                missingCrafterPrerequisites.AddRange(prerequisites.Where(prerequisite =>
                    prerequisite == CrafterPrerequisiteType.AlignmentLawful && (caster.Alignment.Value.ToMask() & AlignmentMaskType.Lawful) == 0
                    || prerequisite == CrafterPrerequisiteType.AlignmentGood && (caster.Alignment.Value.ToMask() & AlignmentMaskType.Good) == 0
                    || prerequisite == CrafterPrerequisiteType.AlignmentChaotic && (caster.Alignment.Value.ToMask() & AlignmentMaskType.Chaotic) == 0
                    || prerequisite == CrafterPrerequisiteType.AlignmentEvil && (caster.Alignment.Value.ToMask() & AlignmentMaskType.Evil) == 0
                ));
            }

            return missingCrafterPrerequisites;
        }

        private static void WorkOnProjects(UnitDescriptor caster, bool returningToCapital) {
            if (!caster.IsPlayerFaction || caster.State.IsDead || caster.State.IsFinallyDead) {
                return;
            }

            currentCaster = caster.Unit;
            var withPlayer = Game.Instance.Player.PartyCharacters.Contains(caster.Unit);
            var playerInCapital = IsPlayerInCapital();
            // Only update characters in the capital when the player is also there.
            if (!withPlayer && !playerInCapital) {
                // Character is back in the capital - skipping them for now.
                return;
            }

            var isAdventuring = withPlayer && !playerInCapital;
            var timer = GetCraftingTimerComponentForCaster(caster);
            if (timer == null || timer.CraftingProjects.Count == 0) {
                // Character is not doing any crafting
                return;
            }

            // Round up the number of days, so caster makes some progress on a new project the first time they rest.
            var interval = Game.Instance.Player.GameTime.Subtract(timer.LastUpdated);
            var daysAvailableToCraft = (int) Math.Ceiling(interval.TotalDays);
            if (daysAvailableToCraft <= 0) {
                if (isAdventuring) {
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-not-full-day"));
                }

                return;
            }

            // Time passes for this character even if they end up making no progress on their projects.  LastUpdated can go up to
            // a day into the future, due to the rounding up of daysAvailableToCraft.
            timer.LastUpdated += TimeSpan.FromDays(daysAvailableToCraft);
            // Work on projects sequentially, skipping any that can't progress due to missing items, missing prerequisites or having too high a DC.
            foreach (var project in timer.CraftingProjects.ToList()) {
                var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
                if (project.UpgradeItem != null) {
                    // Check if the item has been dropped and picked up again, which apparently creates a new object with the same blueprint.
                    if (project.UpgradeItem.Collection != Game.Instance.Player.Inventory) {
                        var itemInInventory = Game.Instance.Player.Inventory.FirstOrDefault(item => item.Blueprint == project.UpgradeItem.Blueprint);
                        if (itemInInventory != null) {
                            ItemUpgradeProjects.Remove(project.UpgradeItem);
                            ItemUpgradeProjects[itemInInventory] = project;
                            project.UpgradeItem = itemInInventory;
                        }
                    }

                    // Check that the caster can get at the item they're upgrading... it must be in the party inventory, and either un-wielded, or the crafter
                    // must be with the wielder (together in the capital or out in the party together).
                    if (project.UpgradeItem.Collection != Game.Instance.Player.Inventory || !(project.UpgradeItem.Wielder == null
                                                                                              || (playerInCapital && !returningToCapital) ||
                                                                                              withPlayer == Game.Instance.Player.PartyCharacters.Contains(
                                                                                                  project.UpgradeItem.Wielder.Unit))) {
                        project.AddMessage(L10NFormat("craftMagicItems-logMessage-missing-upgrade-item", project.UpgradeItem.Blueprint.Name));
                        AddBattleLogMessage(project.LastMessage);
                        continue;
                    }
                }

                StatType craftingSkill;
                int dc;

                if (IsMundaneCraftingData(craftingData)) {
                    craftingSkill = StatType.SkillKnowledgeWorld;
                    dc = CalculateMundaneCraftingDC((RecipeBasedItemCraftingData) craftingData, project.ResultItem.Blueprint, caster);
                } else {
                    craftingSkill = StatType.SkillKnowledgeArcana;
                    dc = 5 + project.CasterLevel;
                }

                var missing = CheckSpellPrerequisites(project, caster, isAdventuring, out var missingSpells, out var spellsToCast);
                if (missing > 0) {
                    var missingSpellNames = missingSpells.Select(ability => ability.Name).BuildCommaList(project.AnyPrerequisite);
                    if (craftingData.PrerequisitesMandatory) {
                        project.AddMessage(L10NFormat("craftMagicItems-logMessage-missing-prerequisite",
                            project.ResultItem.Name, missingSpellNames));
                        AddBattleLogMessage(project.LastMessage);
                        // If the item type has mandatory prerequisites and some are missing, move on to the next project.
                        continue;
                    }

                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-missing-spell", missingSpellNames,
                        MissingPrerequisiteDCModifier * missing));
                }

                missing += CheckCrafterPrerequisites(project, caster);
                dc += MissingPrerequisiteDCModifier * missing;
                var casterLevel = caster.Spellbooks.Aggregate(0, (highest, book) => book.CasterLevel > highest ? book.CasterLevel : highest);
                if (casterLevel < project.CasterLevel) {
                    // Rob's ruling... if you're below the prerequisite caster level, you're considered to be missing a prerequisite for each
                    // level you fall short.
                    var casterLevelPenalty = MissingPrerequisiteDCModifier * (project.CasterLevel - casterLevel);
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-low-caster-level", project.CasterLevel, casterLevelPenalty));
                    dc += casterLevelPenalty;
                }

                var skillCheck = 10 + caster.Stats.GetStat(craftingSkill).ModifiedValue;
                if (skillCheck < dc) {
                    // Can't succeed by taking 10... move on to the next project.
                    project.AddMessage(L10NFormat("craftMagicItems-logMessage-dc-too-high", project.ResultItem.Name,
                        LocalizedTexts.Instance.Stats.GetText(craftingSkill), skillCheck, dc));
                    AddBattleLogMessage(project.LastMessage);
                    continue;
                }

                // Cleared the last hurdle, so caster is going to make progress on this project.
                // You only work at 1/4 speed if you're crafting while adventuring.
                var adventuringPenalty = !isAdventuring || ModSettings.CraftAtFullSpeedWhileAdventuring ? 1 : AdventuringProgressPenalty;
                var progressRate = IsMundaneCraftingData(craftingData) ? MundaneCraftingProgressPerDay : MagicCraftingProgressPerDay;
                // Each 1 by which the skill check exceeds the DC increases the crafting rate by 20% of the base progressRate
                var progressPerDay = (int) (progressRate * (1 + (float) (skillCheck - dc) / 5) / adventuringPenalty);
                var daysUntilProjectFinished = (int) Math.Ceiling(1.0 * (project.TargetCost - project.Progress) / progressPerDay);
                var daysCrafting = Math.Min(daysUntilProjectFinished, daysAvailableToCraft);
                var progressGold = daysCrafting * progressPerDay;
                foreach (var spell in spellsToCast) {
                    if (spell.SourceItem != null) {
                        // Use items whether we're adventuring or not, one charge per day of daysCrafting.  We might run out of charges...
                        if (spell.SourceItem.IsSpendCharges && !((BlueprintItemEquipment) spell.SourceItem.Blueprint).RestoreChargesOnRest) {
                            var itemSpell = spell;
                            for (var day = 0; day < daysCrafting; ++day) {
                                if (itemSpell.SourceItem.Charges <= 0) {
                                    // This item is exhausted and we haven't finished crafting - find another item.
                                    itemSpell = FindCasterSpell(caster, spell.Blueprint, isAdventuring, spellsToCast);
                                }

                                if (itemSpell == null) {
                                    // We've run out of items that can cast the spell...crafting progress is going to slow, if not stop.
                                    progressGold -= progressPerDay * (daysCrafting - day);
                                    skillCheck -= MissingPrerequisiteDCModifier;
                                    if (craftingData.PrerequisitesMandatory) {
                                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-missing-prerequisite", project.ResultItem.Name, spell.Name));
                                        daysCrafting = day;
                                        break;
                                    }

                                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-missing-spell", spell.Name, MissingPrerequisiteDCModifier));
                                    if (skillCheck < dc) {
                                        // Can no longer make progress
                                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-dc-too-high", project.ResultItem.Name,
                                            LocalizedTexts.Instance.Stats.GetText(craftingSkill), skillCheck, dc));
                                        daysCrafting = day;
                                    } else {
                                        // Progress will be slower, but they don't need to cast this spell any longer.
                                        progressPerDay = (int) (progressRate * (1 + (float) (skillCheck - dc) / 5) / adventuringPenalty);
                                        daysUntilProjectFinished =
                                            day + (int) Math.Ceiling(1.0 * (project.TargetCost - project.Progress - progressGold) / progressPerDay);
                                        daysCrafting = Math.Min(daysUntilProjectFinished, daysAvailableToCraft);
                                        progressGold += (daysCrafting - day) * progressPerDay;
                                    }

                                    break;
                                }

                                GameLogContext.SourceUnit = caster.Unit;
                                GameLogContext.Text = itemSpell.SourceItem.Name;
                                AddBattleLogMessage(CharacterUsedItemLocalized);
                                itemSpell.SourceItem.SpendCharges(caster);
                            }
                        }
                    } else if (isAdventuring) {
                        // Actually cast the spells if we're adventuring.
                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-expend-spell", spell.Name));
                        spell.SpendFromSpellbook();
                    }
                }

                var progress = L10NFormat("craftMagicItems-logMessage-made-progress", progressGold, project.TargetCost - project.Progress,
                    project.ResultItem.Name);
                var checkResult = L10NFormat("craftMagicItems-logMessage-made-progress-check", LocalizedTexts.Instance.Stats.GetText(craftingSkill),
                    skillCheck, dc);
                AddBattleLogMessage(progress, checkResult);
                daysAvailableToCraft -= daysCrafting;
                project.Progress += progressGold;
                if (project.Progress >= project.TargetCost) {
                    // Completed the project!
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-crafting-complete", project.ResultItem.Name));
                    CraftItem(project.ResultItem, project.UpgradeItem);
                    timer.CraftingProjects.Remove(project);
                    if (project.UpgradeItem == null) {
                        ItemCreationProjects.Remove(project);
                    } else {
                        ItemUpgradeProjects.Remove(project.UpgradeItem);
                    }
                } else {
                    var amountComplete = L10NFormat("craftMagicItems-logMessage-made-progress-amount-complete", project.ResultItem.Name,
                        100 * project.Progress / project.TargetCost);
                    AddBattleLogMessage(amountComplete, project.ResultItem);
                    project.AddMessage($"{progress} {checkResult}");
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

        [HarmonyPatch(typeof(CapitalCompanionLogic), "OnFactActivate")]
        private static class CapitalCompanionLogicOnFactActivatePatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix() {
                // Trigger project work on companions left behind in the capital, with a flag saying the party wasn't around while they were working.
                foreach (var companion in Game.Instance.Player.RemoteCompanions) {
                    if (companion.Value != null) {
                        WorkOnProjects(companion.Value.Descriptor, true);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(RestController), "ApplyRest")]
        private static class RestControllerApplyRestPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(UnitDescriptor unit) {
                WorkOnProjects(unit, false);
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
                if (__state && !__instance.IsLoadingInProcess) {
                    // Just finished loading a save
                    var casterList = UIUtility.GetGroup(true);
                    foreach (var caster in casterList) {
                        // If the mod is disabled, this will clean up crafting timer "buff" from all casters.
                        var timer = GetCraftingTimerComponentForCaster(caster.Descriptor);
                        if (timer != null) {
                            // Migrate all projects using ItemBlueprint to use ResultItem
                            foreach (var project in timer.CraftingProjects) {
                                if (project.ItemBlueprint != null) {
                                    var craftingData = itemCraftingData.First(data => data.Name == project.ItemType);
                                    project.ResultItem = BuildItemEntity(project.ItemBlueprint, craftingData);
                                    project.ItemBlueprint = null;
                                }

                                project.ResultItem.PostLoad();

                                project.Crafter = caster;
                                if (project.UpgradeItem == null) {
                                    ItemCreationProjects.Add(project);
                                } else {
                                    ItemUpgradeProjects[project.UpgradeItem] = project;
                                    project.UpgradeItem.PostLoad();
                                }
                            }
                        }
                    }
                }
            }
        }

        // Reverse the explicit code to hide weapon enchantments on shields - sorry, Owlcat.
        [HarmonyPatch(typeof(UIUtilityItem), "GetQualities")]
        private static class UIUtilityItemGetQualitiesPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(ItemEntity item) {
                if (item is ItemEntityShield shield && shield.IsIdentified) {
                    // It appears that shields are not properly identified when found.
                    shield.ArmorComponent.IsIdentified = true;
                    if (shield.WeaponComponent != null) {
                        shield.WeaponComponent.IsIdentified = true;
                    }
                }
            }

            // ReSharper disable once UnusedMember.Local
            private static void Postfix(ItemEntity item, ref string __result) {
                if (!item.IsIdentified) {
                    return;
                }

                if (item is ItemEntityWeapon weapon) {
                    if (weapon.Blueprint.DamageType.Physical.Material != 0) {
                        var description = new StringBuilder();
                        description.Append(LocalizedTexts.Instance.DamageMaterial.GetText(weapon.Blueprint.DamageType.Physical.Material));
                        if (!string.IsNullOrEmpty(__result)) {
                            description.Append(", ");
                            description.Append(__result);
                        }

                        __result = description.ToString();
                    }
                } else if (item is ItemEntityShield shield && shield.WeaponComponent != null) {
                    var weaponQualities = Traverse.Create(typeof(UIUtilityItem)).Method("GetQualities", new[] {typeof(ItemEntity)})
                        .GetValue<string>(shield.WeaponComponent);
                    if (!string.IsNullOrEmpty(weaponQualities)) {
                        __result = string.IsNullOrEmpty(__result)
                            ? $"{ShieldBashLocalized}: {weaponQualities}"
                            : $"{__result}{(__result.LastIndexOf(',') < __result.Length - 2 ? ", " : "")} {ShieldBashLocalized}: {weaponQualities}";
                    }
                }

                // Remove trailing commas while we're here.
                __result = __result.Trim();
                if (!string.IsNullOrEmpty(__result) && __result.LastIndexOf(',') == __result.Length - 1) {
                    __result = __result.Substring(0, __result.Length - 1);
                }
            }
        }

        [HarmonyPatch(typeof(UIUtilityItem), "FillShieldEnchantments")]
        private static class UIUtilityItemFillShieldEnchantmentsPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(ItemEntityShield shield, ref string __result) {
                if (shield.IsIdentified && shield.WeaponComponent != null) {
                    __result = Traverse.Create(typeof(UIUtilityItem))
                        .Method("FillWeaponQualities", new[] {typeof(TooltipData), typeof(ItemEntityWeapon), typeof(string)})
                        .GetValue<string>(new TooltipData(), shield.WeaponComponent, __result);
                }
            }
        }
    }
}