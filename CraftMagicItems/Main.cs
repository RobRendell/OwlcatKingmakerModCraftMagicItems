using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.Loot;
using Kingmaker.Blueprints.Root;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.Controllers.Rest;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Designers.TempMapCode.Capital;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.Kingdom;
using Kingmaker.Localization;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UI;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UI.Log;
using Kingmaker.UI.Tooltip;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Alignments;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using Newtonsoft.Json;
using UnityEngine;
using UnityModManagerNet;
using Random = System.Random;

namespace CraftMagicItems {
    public class Settings : UnityModManager.ModSettings {
        public const int MagicCraftingProgressPerDay = 500;
        public const int MundaneCraftingProgressPerDay = 5;

        public bool CraftingCostsNoGold;
        public bool IgnoreCraftingFeats;
        public bool CraftingTakesNoTime;
        public float CraftingPriceScale = 1;
        public bool CraftAtFullSpeedWhileAdventuring;
        public bool CasterLevelIsSinglePrerequisite;
        public bool IgnoreFeatCasterLevelRestriction;
        public bool IgnorePlusTenItemMaximum;
        public bool CustomCraftRate;
        public int MagicCraftingRate = MagicCraftingProgressPerDay;
        public int MundaneCraftingRate = MundaneCraftingProgressPerDay;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }

    public static class Main {
        private const int MissingPrerequisiteDCModifier = 5;
        private const int OppositionSchoolDCModifier = 4;
        private const int AdventuringProgressPenalty = 4;
        private const int MasterworkCost = 300;
        private const int WeaponPlusCost = 2000;
        private const int ArmourPlusCost = 1000;
        private const string BondedItemRitual = "bondedItemRitual";

        private static readonly string[] CraftingPriceStrings = {
            "100% (Owlcat prices)",
            "200% (Tabletop prices)",
            "Custom"
        };

        private static readonly FeatureGroup[] CraftingFeatGroups = {FeatureGroup.Feat, FeatureGroup.WizardFeat};
        private const string MasterworkGuid = "6b38844e2bffbac48b63036b66e735be";
        private const string AlchemistProgressionGuid = "efd55ff9be2fda34981f5b9c83afe4f1";
        private const string MartialWeaponProficiencies = "203992ef5b35c864390b4e4a1e200629";
        private const string ChannelEnergyFeatureGuid = "a79013ff4bcd4864cb669622a29ddafb";
        private const string CustomPriceLabel = "Crafting Cost: ";
        private static readonly LocalizedString CasterLevelLocalized = new L10NString("dfb34498-61df-49b1-af18-0a84ce47fc98");
        private static readonly LocalizedString CharacterUsedItemLocalized = new L10NString("be7942ed-3af1-4fc7-b20b-41966d2f80b7");
        private static readonly LocalizedString ShieldBashLocalized = new L10NString("314ff56d-e93b-4915-8ca4-24a7670ad436");
        private static readonly LocalizedString QualitiesLocalized = new L10NString("0f84fde9-14ca-4e2f-9c82-b2522039dbff");

        private static readonly WeaponCategory[] AmmunitionWeaponCategories = {
            WeaponCategory.Longbow,
            WeaponCategory.Shortbow,
            WeaponCategory.LightCrossbow,
            WeaponCategory.HeavyCrossbow,
            WeaponCategory.HandCrossbow,
            WeaponCategory.LightRepeatingCrossbow,
            WeaponCategory.HeavyRepeatingCrossbow
        };

        private static readonly ItemsFilter.ItemType[] BondedItemSlots = {
            ItemsFilter.ItemType.Weapon,
            ItemsFilter.ItemType.Ring,
            ItemsFilter.ItemType.Neck
        };

        private static readonly string[] BondedItemFeatures = {
            "2fb5e65bd57caa943b45ee32d825e9b9",
            "aa34ca4f3cd5e5d49b2475fcfdf56b24"
        };

        private enum OpenSection {
            CraftMagicItemsSection,
            CraftMundaneItemsSection,
            ProjectsSection,
            FeatsSection,
            CheatsSection
        }

        public static UnityModManager.ModEntry ModEntry;
        public static Settings ModSettings;
        public static CraftMagicItemsAccessors Accessors;
        public static ItemCraftingData[] ItemCraftingData;
        public static CustomLootItem[] CustomLootItems;

        private static bool modEnabled = true;
        private static Harmony12.HarmonyInstance harmonyInstance;
        private static CraftMagicItemsBlueprintPatcher blueprintPatcher;
        private static OpenSection currentSection = OpenSection.CraftMagicItemsSection;
        private static readonly Dictionary<string, int> SelectedIndex = new Dictionary<string, int>();
        private static int selectedCasterLevel;
        private static bool selectedShowPreparedSpells;
        private static bool selectedDoubleWeaponSecondEnd;
        private static int selectedCastsPerDay;
        private static string selectedBaseGuid;
        private static string selectedCustomName;
        private static BlueprintItem upgradingBlueprint;
        private static bool selectedBondWithNewObject;
        private static UnitEntityData currentCaster;

        private static readonly Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> SpellIdToItem =
            new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();

        private static readonly Dictionary<string, List<ItemCraftingData>> SubCraftingData = new Dictionary<string, List<ItemCraftingData>>();
        private static readonly Dictionary<string, List<BlueprintItemEquipment>> EnchantmentIdToItem = new Dictionary<string, List<BlueprintItemEquipment>>();
        private static readonly Dictionary<string, List<RecipeData>> EnchantmentIdToRecipe = new Dictionary<string, List<RecipeData>>();
        private static readonly Dictionary<string, int> EnchantmentIdToCost = new Dictionary<string, int>();
        private static readonly List<LogDataManager.LogItemData> PendingLogItems = new List<LogDataManager.LogItemData>();
        private static readonly Dictionary<ItemEntity, CraftingProjectData> ItemUpgradeProjects = new Dictionary<ItemEntity, CraftingProjectData>();
        private static readonly List<CraftingProjectData> ItemCreationProjects = new List<CraftingProjectData>();

        private static readonly Random RandomGenerator = new Random();


        /**
         * Patch all HarmonyPatch classes in the assembly, starting in the order of the methods named in methodNameOrder, and the rest after that.
         */
        private static void PatchAllOrdered(params string[] methodNameOrder) {
            var allAttributes = Assembly.GetExecutingAssembly().GetTypes()
                    .Select(type => new {type, methods = Harmony12.HarmonyMethodExtensions.GetHarmonyMethods(type)})
                    .Where(pair => pair.methods != null && pair.methods.Count > 0)
                    .Select(pair => new {pair.type, attributes = Harmony12.HarmonyMethod.Merge(pair.methods)})
                    .OrderBy(pair => methodNameOrder
                                         .Select((name, index) => new {name, index})
                                         .FirstOrDefault(nameIndex => nameIndex.name.Equals(pair.attributes.methodName))?.index
                                     ?? methodNameOrder.Length)
                ;
            foreach (var pair in allAttributes) {
                new Harmony12.PatchProcessor(harmonyInstance, pair.type, pair.attributes).Patch();
            }
        }

        /**
         * Unpatch all HarmonyPatch classes for harmonyInstance, except the ones whose method names match exceptMethodName
         */
        private static void UnpatchAllExcept(params string[] exceptMethodName) {
            if (harmonyInstance != null) {
                try {
                    foreach (var method in harmonyInstance.GetPatchedMethods().ToArray()) {
                        if (!exceptMethodName.Contains(method.Name) && harmonyInstance.GetPatchInfo(method).Owners.Contains(harmonyInstance.Id)) {
                            harmonyInstance.Unpatch(method, Harmony12.HarmonyPatchType.All, harmonyInstance.Id);
                        }
                    }
                } catch (Exception e) {
                    ModEntry.Logger.Error($"Exception during Un-patching: {e}");
                }
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static void Load(UnityModManager.ModEntry modEntry) {
            try {
                ModEntry = modEntry;
                ModSettings = UnityModManager.ModSettings.Load<Settings>(modEntry);
                SelectedIndex[CustomPriceLabel] = Mathf.Abs(ModSettings.CraftingPriceScale - 1f) < 0.001 ? 0 :
                    Mathf.Abs(ModSettings.CraftingPriceScale - 2f) < 0.001 ? 1 : 2;
                modEnabled = modEntry.Active;
                modEntry.OnSaveGUI = OnSaveGui;
                modEntry.OnToggle = OnToggle;
                modEntry.OnGUI = OnGui;
                CustomBlueprintBuilder.InitialiseBlueprintRegex(CraftMagicItemsBlueprintPatcher.BlueprintRegex);
                harmonyInstance = Harmony12.HarmonyInstance.Create("kingmaker.craftMagicItems");
                // Patch the recovery methods first.
                PatchAllOrdered("TryGetBlueprint", "PostLoad", "OnAreaLoaded");
                Accessors = new CraftMagicItemsAccessors();
                blueprintPatcher = new CraftMagicItemsBlueprintPatcher(Accessors, modEnabled);
            } catch (Exception e) {
                modEntry.Logger.Error($"Exception during Load: {e}");
                modEnabled = false;
                CustomBlueprintBuilder.Enabled = false;
                // Unpatch everything except methods involved in recovering a save when mod is disabled.
                UnpatchAllExcept("TryGetBlueprint", "PostLoad", "OnAreaLoaded");
                throw;
            }
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
                if (Game.Instance == null || Game.Instance.CurrentMode != GameModeType.Default
                    && Game.Instance.CurrentMode != GameModeType.GlobalMap
                    && Game.Instance.CurrentMode != GameModeType.FullScreenUi
                    && Game.Instance.CurrentMode != GameModeType.Pause
                    && Game.Instance.CurrentMode != GameModeType.EscMode
                    && Game.Instance.CurrentMode != GameModeType.Rest
                    && Game.Instance.CurrentMode != GameModeType.Kingdom) {
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

        public static string L10NFormat(UnitEntityData sourceUnit, string key, params object[] args) {
            // Set GameLogContext so the caster will be used when generating localized strings.
            GameLogContext.SourceUnit = sourceUnit;
            var template = new L10NString(key);
            var result = string.Format(template.ToString(), args);
            GameLogContext.Clear();
            return result;
        }

        private static string L10NFormat(string key, params object[] args) {
            return L10NFormat(currentCaster ?? GetSelectedCrafter(false), key, args);
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
            // Manually search caster.Buffs rather than using GetFact, because we don't want to TryGetBlueprint if the mod is disabled.
            var timerBuff = caster.Buffs.Enumerable.FirstOrDefault(fact => fact.Blueprint.AssetGuid == CraftMagicItemsBlueprintPatcher.TimerBlueprintGuid);
            if (timerBuff == null) {
                if (CustomBlueprintBuilder.Downgrade) {
                    // The mod is disabled and we're downgrading custom blueprints - clean up the timer buff.
                    var baseBlueprintGuid = CraftMagicItemsBlueprintPatcher.TimerBlueprintGuid.Substring(0, CustomBlueprintBuilder.VanillaAssetIdLength);
                    timerBuff = caster.Buffs.Enumerable.FirstOrDefault(fact => fact.Blueprint.AssetGuid == baseBlueprintGuid);
                    if (timerBuff != null) {
                        caster.RemoveFact(timerBuff);
                    }

                    return null;
                }

                if (!create) {
                    return null;
                }

                var timerBlueprint = (BlueprintBuff) ResourcesLibrary.TryGetBlueprint(CraftMagicItemsBlueprintPatcher.TimerBlueprintGuid);
                caster.AddFact<Buff>(timerBlueprint);
                timerBuff = (Buff) caster.GetFact(timerBlueprint);
            }

            return timerBuff.SelectComponents<CraftingTimerComponent>().First();
        }

        public static BondedItemComponent GetBondedItemComponentForCaster(UnitDescriptor caster, bool create = false) {
            // Manually search caster.Buffs rather than using GetFact, because we don't want to TryGetBlueprint if the mod is disabled.
            var bondedItemBuff =
                caster.Buffs.Enumerable.FirstOrDefault(fact => fact.Blueprint.AssetGuid == CraftMagicItemsBlueprintPatcher.BondedItemBuffBlueprintGuid);
            if (bondedItemBuff == null) {
                if (CustomBlueprintBuilder.Downgrade) {
                    // The mod is disabled and we're downgrading custom blueprints - clean up the bonded item buff.
                    var baseBlueprintGuid = CraftMagicItemsBlueprintPatcher.TimerBlueprintGuid.Substring(0, CustomBlueprintBuilder.VanillaAssetIdLength);
                    bondedItemBuff = caster.Buffs.Enumerable.FirstOrDefault(fact => fact.Blueprint.AssetGuid == baseBlueprintGuid);
                    if (bondedItemBuff != null) {
                        caster.RemoveFact(bondedItemBuff);
                    }

                    return null;
                }

                if (!create) {
                    return null;
                }

                var timerBlueprint = (BlueprintBuff) ResourcesLibrary.TryGetBlueprint(CraftMagicItemsBlueprintPatcher.BondedItemBuffBlueprintGuid);
                caster.AddFact<Buff>(timerBlueprint);
                bondedItemBuff = (Buff) caster.GetFact(timerBlueprint);
            }

            return bondedItemBuff.SelectComponents<BondedItemComponent>().First();
        }

        private static void RenderCraftMagicItemsSection() {
            var caster = GetSelectedCrafter(false);
            if (caster == null) {
                return;
            }

            var itemTypes = ItemCraftingData
                .Where(data => data.FeatGuid != null && (ModSettings.IgnoreCraftingFeats || CharacterHasFeat(caster, data.FeatGuid)))
                .ToArray();
            if (!Enumerable.Any(itemTypes)) {
                RenderLabel($"{caster.CharacterName} does not know any crafting feats.");
                return;
            }

            var hasBondedItemFeature =
                caster.Descriptor.Progression.Features.Enumerable.Any(feature => BondedItemFeatures.Contains(feature.Blueprint.AssetGuid));
            var itemTypeNames = itemTypes.Select(data => new L10NString(data.NameId).ToString())
                .PrependConditional(hasBondedItemFeature, new L10NString("craftMagicItems-bonded-object-name")).ToArray();
            var selectedItemTypeIndex = RenderSelection("Crafting: ", itemTypeNames, 6, ref selectedCustomName);
            if (hasBondedItemFeature && selectedItemTypeIndex == 0) {
                RenderBondedItemCrafting(caster);
            } else {
                var craftingData = itemTypes[hasBondedItemFeature ? selectedItemTypeIndex - 1 : selectedItemTypeIndex];
                if (craftingData is SpellBasedItemCraftingData spellBased) {
                    RenderSpellBasedCrafting(caster, spellBased);
                } else {
                    RenderRecipeBasedCrafting(caster, craftingData as RecipeBasedItemCraftingData);
                }
            }

            RenderLabel($"Current Money: {Game.Instance.Player.Money}");
        }

        private static RecipeBasedItemCraftingData GetBondedItemCraftingData(BondedItemComponent bondedComponent) {
            // Find crafting data relevant to the bonded item
            return ItemCraftingData.OfType<RecipeBasedItemCraftingData>()
                .First(data => data.Slots.Contains(bondedComponent.ownerItem.Blueprint.ItemType) && !IsMundaneCraftingData(data));
        }

        private static void BondWithObject(UnitEntityData caster, ItemEntity item) {
            var bondedComponent = GetBondedItemComponentForCaster(caster.Descriptor, true);
            if (bondedComponent.ownerItem != null && bondedComponent.everyoneElseItem != null) {
                var ownerItem = bondedComponent.ownerItem;
                var everyoneElseItem = bondedComponent.everyoneElseItem;
                // Need to set these to null now so the unequipping/equipping below doesn't invoke the automagic item swapping.
                bondedComponent.ownerItem = null;
                bondedComponent.everyoneElseItem = null;
                var holdingSlot = ownerItem.HoldingSlot;
                if (holdingSlot != null && ownerItem != everyoneElseItem) {
                    // Revert the old bonded item to its original form.
                    using (new DisableBattleLog()) {
                        Game.Instance.Player.Inventory.Remove(ownerItem);
                        holdingSlot.InsertItem(everyoneElseItem);
                    }
                }
                // Cancel any upgrading of the old bonded item that was in progress.
                if (ItemUpgradeProjects.ContainsKey(ownerItem)) {
                    CancelCraftingProject(ItemUpgradeProjects[ownerItem]);
                }
            }
            bondedComponent.ownerItem = item;
            bondedComponent.everyoneElseItem = item;
            // Cancel any pending crafting projects by other characters for the new bonded item.
            if (ItemUpgradeProjects.ContainsKey(item) && ItemUpgradeProjects[item].Crafter != caster) {
                CancelCraftingProject(ItemUpgradeProjects[item]);
            }
        }

        private static void RenderBondedItemCrafting(UnitEntityData caster) {
            // Check if the caster is performing a bonded item ritual.
            var projects = GetCraftingTimerComponentForCaster(caster.Descriptor);
            var ritualProject = projects.CraftingProjects.FirstOrDefault(project => project.ItemType == BondedItemRitual);
            if (ritualProject != null) {
                RenderLabel($"{caster.CharacterName} is in the process of bonding with {ritualProject.ResultItem.Name}");
                return;
            }
            var bondedComponent = GetBondedItemComponentForCaster(caster.Descriptor);
            if (bondedComponent == null || bondedComponent.ownerItem == null || selectedBondWithNewObject) {
                if (selectedBondWithNewObject) {
                    RenderLabel("You may bond with a different object by performing a special ritual that costs 200 gp per caster level. This ritual takes 8 " +
                                "hours to complete. Items replaced in this way do not possess any of the additional enchantments of the previous bonded item, " +
                                "and the previous bonded item loses any enchantments you added via your bond.");
                    if (GUILayout.Button($"Cancel bonding to a new object")) {
                        selectedBondWithNewObject = false;
                    }
                }
                RenderLabel(
                    "You can enchant additional magic abilities to your bonded item as if you had the required Item Creation Feat, as long as you also " +
                    "meet the caster level prerequisite of the feat.  Abilities added in this fashion function only for you, and no-one else can add " +
                    "enchantments to your bonded item.");
                RenderLabel(new L10NString("craftMagicItems-bonded-item-glossary"));
                RenderLabel("Choose your bonded item.");
                var names = BondedItemSlots.Select(slot => new L10NString(GetSlotStringKey(slot)).ToString()).ToArray();
                var selectedItemSlotIndex = RenderSelection("Item type", names, 10);
                var selectedSlot = BondedItemSlots[selectedItemSlotIndex];
                var items = Game.Instance.Player.Inventory
                    .Where(item => item.Blueprint is BlueprintItemEquipment blueprint
                                   && DoesBlueprintMatchSlot(blueprint, selectedSlot)
                                   && CanEnchant(item)
                                   && CanRemove(blueprint)
                                   && (bondedComponent == null || (bondedComponent.ownerItem != item && bondedComponent.everyoneElseItem != item))
                                   && item.Wielder == caster.Descriptor)
                    .OrderBy(item => item.Name)
                    .ToArray();
                if (items.Length == 0) {
                    RenderLabel("You do not have any item of that type currently equipped.");
                    return;
                }
                var itemNames = items.Select(item => item.Name).ToArray();
                var selectedUpgradeItemIndex = RenderSelection("Item: ", itemNames, 5);
                var selectedItem = items[selectedUpgradeItemIndex];
                var goldCost = !selectedBondWithNewObject || ModSettings.CraftingCostsNoGold ? 0 : 200 * CharacterCasterLevel(caster.Descriptor);
                var canAfford = BuildCostString(out var cost, null, goldCost);
                var label = $"Make {selectedItem.Name} your bonded item{(goldCost == 0 ? "" : " for " + cost)}";
                if (!canAfford) {
                    RenderLabel(label);
                } else if (GUILayout.Button(label)) {
                    if (goldCost > 0) {
                        Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                        Game.Instance.Player.SpendMoney(goldCost);
                    }
                    if (selectedBondWithNewObject) {
                        selectedBondWithNewObject = false;
                        if (!ModSettings.CraftingTakesNoTime) {
                            // Create project
                            AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-begin-ritual-bonded-item", cost, selectedItem.Name));
                            var project = new CraftingProjectData(caster, ModSettings.MagicCraftingRate, goldCost, 0, selectedItem, BondedItemRitual);
                            AddNewProject(caster.Descriptor, project);
                            CalculateProjectEstimate(project);
                            currentSection = OpenSection.ProjectsSection;
                            return;
                        }
                    }
                    BondWithObject(caster, selectedItem);
                }
            } else {
                var craftingData = GetBondedItemCraftingData(bondedComponent);
                if (bondedComponent.ownerItem.Wielder != null && !IsPlayerInCapital()
                                                              && !Game.Instance.Player.PartyCharacters.Contains(bondedComponent.ownerItem.Wielder.Unit)) {
                    RenderLabel($"You cannot currently access {bondedComponent.ownerItem.Name}.");
                } else if (!ModSettings.IgnoreFeatCasterLevelRestriction && CharacterCasterLevel(caster.Descriptor) < craftingData.MinimumCasterLevel) {
                    RenderLabel($"You will not be able to enchant your bonded item until your caster level reaches {craftingData.MinimumCasterLevel} " +
                                $"(currently {CharacterCasterLevel(caster.Descriptor)}).");
                } else {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>Your bonded item</b>: {bondedComponent.ownerItem.Name}");
                    if (GUILayout.Button("Bond with a different item", GUILayout.ExpandWidth(false))) {
                        selectedBondWithNewObject = true;
                    }
                    GUILayout.EndHorizontal();
                    RenderRecipeBasedCrafting(caster, craftingData, bondedComponent.ownerItem);
                }
            }
        }

        private static void RenderSpellBasedCrafting(UnitEntityData caster, SpellBasedItemCraftingData craftingData) {
            var spellbooks = caster.Descriptor.Spellbooks.Where(book => book.CasterLevel > 0).ToList();
            if (spellbooks.Count == 0) {
                RenderLabel($"{caster.CharacterName} is not yet able to cast spells.");
                return;
            }

            var selectedSpellbookIndex = 0;
            if (spellbooks.Count != 1) {
                var spellBookNames = spellbooks.Select(book => book.Blueprint.Name.ToString()).ToArray();
                selectedSpellbookIndex = RenderSelection("Class: ", spellBookNames, 10);
            }

            var spellbook = spellbooks[selectedSpellbookIndex];
            var maxLevel = Math.Min(spellbook.MaxSpellLevel, craftingData.MaxSpellLevel);
            var spellLevelNames = Enumerable.Range(0, maxLevel + 1).Select(index => $"Level {index}").ToArray();
            var spellLevel = RenderSelection("Spell level: ", spellLevelNames, 10);
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
                var maxCasterLevel = CharacterCasterLevel(caster.Descriptor, spellbook);
                if (minCasterLevel < maxCasterLevel) {
                    RenderIntSlider(ref selectedCasterLevel, "Caster level: ", minCasterLevel, maxCasterLevel);
                } else {
                    selectedCasterLevel = minCasterLevel;
                    RenderLabel($"Caster level: {selectedCasterLevel}");
                }

                RenderCraftingSkillInformation(caster, StatType.SkillKnowledgeArcana, 5 + selectedCasterLevel, selectedCasterLevel);

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
                    } else if (spell.Blueprint.Variants != null) {
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

        public static ItemsFilter.ItemType GetItemType(BlueprintItem blueprint) {
            return (blueprint is BlueprintItemArmor armour && armour.IsShield
                    || blueprint is BlueprintItemWeapon weapon && (
                        weapon.Category == WeaponCategory.WeaponLightShield
                        || weapon.Category == WeaponCategory.WeaponHeavyShield
                        || weapon.Category == WeaponCategory.SpikedLightShield
                        || weapon.Category == WeaponCategory.SpikedHeavyShield))
                ? ItemsFilter.ItemType.Shield
                : blueprint.ItemType;
        }

        public static RecipeData FindSourceRecipe(string selectedEnchantmentId, BlueprintItem blueprint) {
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
            BlueprintItemEquipment upgradeItem = null, bool checkPrice = false) {
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

            // Verify the price of the vanilla item
            return !checkPrice || RulesRecipeItemCost(blueprint) == blueprint.Cost;
        }

        private static IEnumerable<T> PrependConditional<T>(this IEnumerable<T> target, bool prepend, params T[] items) {
            return prepend ? items.Concat(target ?? throw new ArgumentException(nameof(target))) : target;
        }

        private static string Join<T>(this IEnumerable<T> enumeration, string delimiter = ", ") {
            return enumeration.Aggregate("", (prev, curr) => prev + (prev != "" ? delimiter : "") + curr.ToString());
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
                    return ItemPlusEquivalent(armor) > 0;
                case BlueprintItemWeapon weapon:
                    return ItemPlusEquivalent(weapon) > 0;
                case BlueprintItemShield shield:
                    var isWeaponEnchantmentRecipe = recipe?.OnlyForSlots?.Contains(ItemsFilter.ItemType.Weapon) ?? false;
                    return !isWeaponEnchantmentRecipe && ItemPlusEquivalent(shield.ArmorComponent) > 0
                           || isWeaponEnchantmentRecipe && ItemPlusEquivalent(shield.WeaponComponent) > 0;
                case BlueprintItemEquipmentUsable usable:
                    return !usable.SpendCharges || usable.RestoreChargesOnRest;
                case BlueprintItemEquipment equipment:
                    return GetEnchantments(blueprint).Any() || equipment.Ability != null || equipment.ActivatableAbility != null;
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

        private static bool CanRemove(BlueprintItemEquipment blueprint) {
            return !blueprint.IsNonRemovable
                   // Also, can't remove Amiri's Ginormous Sword
                   && !blueprint.AssetGuid.Contains("2e3280bf21ec832418f51bee5136ec7a")
                   && !blueprint.AssetGuid.Contains("b60252a8ae028ba498340199f48ead67")
                   && !blueprint.AssetGuid.Contains("fb379e61500421143b52c739823b4082");
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
                        case ItemRestrictions.WeaponUseAmmunition when weapon == null || !AmmunitionWeaponCategories.Contains(weapon.Category):
                        case ItemRestrictions.WeaponNotUseAmmunition when weapon == null || AmmunitionWeaponCategories.Contains(weapon.Category):
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
                   && recipe.ResultItem == null
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
            item.Identify();
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                item.Charges = spellBased.Charges; // Set the charges, since wand blueprints have random values.
            }

            if (item is ItemEntityShield shield && item.IsIdentified) {
                shield.ArmorComponent.Identify();
                shield.WeaponComponent?.Identify();
            }

            item.PostLoad();
            return item;
        }

        private static bool DoesBlueprintMatchSlot(BlueprintItemEquipment blueprint, ItemsFilter.ItemType slot) {
            return blueprint.ItemType == slot || slot == ItemsFilter.ItemType.Usable && blueprint is BlueprintItemEquipmentUsable;
        }

        private static string GetBonusString(int bonus, RecipeData recipe) {
            bonus *= recipe.BonusMultiplier == 0 ? 1 : recipe.BonusMultiplier;
            return recipe.BonusDieSize != 0 ? new DiceFormula(bonus, recipe.BonusDieSize).ToString() : bonus.ToString();
        }

        private static bool IsAnotherCastersBondedItem(ItemEntity item, UnitEntityData caster) {
            var otherCharacters = UIUtility.GetGroup(true).Where(character => character != caster && character.IsPlayerFaction && !character.Descriptor.IsPet);
            return otherCharacters
                .Select(character => GetBondedItemComponentForCaster(character.Descriptor))
                .Any(bondedComponent => bondedComponent != null && (bondedComponent.ownerItem == item || bondedComponent.everyoneElseItem == item));
        }

        private static void RenderRecipeBasedCrafting(UnitEntityData caster, RecipeBasedItemCraftingData craftingData, ItemEntity upgradeItem = null) {
            ItemsFilter.ItemType selectedSlot;
            if (upgradeItem != null) {
                selectedSlot = upgradeItem.Blueprint.ItemType;
                while (ItemUpgradeProjects.ContainsKey(upgradeItem)) {
                    upgradeItem = ItemUpgradeProjects[upgradeItem].ResultItem;
                }
                RenderLabel($"Enchanting {upgradeItem.Name}");
            } else {
                // Choose slot/weapon type.
                var selectedItemSlotIndex = 0;
                if (craftingData.Slots.Length > 1) {
                    var names = craftingData.Slots.Select(slot => new L10NString(GetSlotStringKey(slot)).ToString()).ToArray();
                    selectedItemSlotIndex = RenderSelection("Item type", names, 10, ref selectedCustomName);
                }

                selectedSlot = craftingData.Slots[selectedItemSlotIndex];
                var playerInCapital = IsPlayerInCapital();
                // Choose an existing or in-progress item of that type, or create a new one (if allowed).
                var items = Game.Instance.Player.Inventory
                    .Concat(ItemCreationProjects.Select(project => project.ResultItem))
                    .Where(item => item.Blueprint is BlueprintItemEquipment blueprint
                                   && DoesBlueprintMatchSlot(blueprint, selectedSlot)
                                   && CanEnchant(item)
                                   && !IsAnotherCastersBondedItem(item, caster)
                                   && CanRemove(blueprint)
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
                var itemNames = items.Select(item => item.Name).PrependConditional(canCreateNew, new L10NString("craftMagicItems-label-craft-new-item"))
                    .ToArray();
                if (itemNames.Length == 0) {
                    RenderLabel($"{caster.CharacterName} can not access any items of that type.");
                    return;
                }

                var selectedUpgradeItemIndex = RenderSelection("Item: ", itemNames, 5, ref selectedCustomName);
                // See existing item details and enchantments.
                var index = selectedUpgradeItemIndex - (canCreateNew ? 1 : 0);
                upgradeItem = index < 0 ? null : items[index];
            }
            var upgradeItemDoubleWeapon = upgradeItem as ItemEntityWeapon;
            if (upgradeItem != null) {
                if (upgradeItemDoubleWeapon != null && upgradeItemDoubleWeapon.Blueprint.Double) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{upgradeItem.Name} is a double weapon; enchanting ", GUILayout.ExpandWidth(false));
                    var label = selectedDoubleWeaponSecondEnd ? "Secondary end" : "Primary end";
                    if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                        selectedDoubleWeaponSecondEnd = !selectedDoubleWeaponSecondEnd;
                    }
                    if (selectedDoubleWeaponSecondEnd) {
                        upgradeItem = upgradeItemDoubleWeapon.Second;
                    } else {
                        upgradeItemDoubleWeapon = null;
                    }
                    GUILayout.EndHorizontal();
                } else {
                    upgradeItemDoubleWeapon = null;
                }
                RenderLabel(BuildItemDescription(upgradeItem));
            }

            // Pick recipe to apply, but make any with the same ParentNameId appear in a second level menu under their parent name.
            var availableRecipes = craftingData.Recipes
                .Where(recipe => (recipe.ParentNameId == null || recipe == craftingData.SubRecipes[recipe.ParentNameId][0])
                                 && (recipe.OnlyForSlots == null || recipe.OnlyForSlots.Contains(selectedSlot))
                                 && RecipeAppliesToBlueprint(recipe, upgradeItem?.Blueprint))
                .OrderBy(recipe => new L10NString(recipe.ParentNameId ?? recipe.NameId).ToString())
                .ToArray();
            var recipeNames = availableRecipes.Select(recipe => new L10NString(recipe.ParentNameId ?? recipe.NameId).ToString())
                .Concat(upgradeItem == null && craftingData.NewItemBaseIDs == null || upgradeItemDoubleWeapon != null
                    ? new string[0]
                    : new[] {new L10NString("craftMagicItems-label-cast-spell-n-times").ToString()})
                .ToArray();
            var selectedRecipeIndex = RenderSelection("Enchantment: ", recipeNames, 5, ref selectedCustomName);
            if (selectedRecipeIndex == availableRecipes.Length) {
                // Cast spell N times
                RenderCastSpellNTimes(caster, craftingData, upgradeItem, selectedSlot);
                return;
            }

            var selectedRecipe = availableRecipes[selectedRecipeIndex];
            if (selectedRecipe.ParentNameId != null) {
                var category = recipeNames[selectedRecipeIndex];
                var availableSubRecipes = craftingData.SubRecipes[selectedRecipe.ParentNameId]
                    .OrderBy(recipe => new L10NString(recipe.NameId).ToString())
                    .ToArray();
                recipeNames = availableSubRecipes.Select(recipe => new L10NString(recipe.NameId).ToString()).ToArray();
                var selectedSubRecipeIndex = RenderSelection(category + ": ", recipeNames, 6, ref selectedCustomName);
                selectedRecipe = availableSubRecipes[selectedSubRecipeIndex];
            }

            BlueprintItemEnchantment selectedEnchantment = null;
            BlueprintItemEnchantment[] availableEnchantments = null;
            var selectedEnchantmentIndex = 0;
            if (selectedRecipe.ResultItem == null) {
                // Pick specific enchantment from the recipe
                availableEnchantments = selectedRecipe.Enchantments;
                var supersededEnchantment = upgradeItem != null ? FindSupersededEnchantmentId(upgradeItem.Blueprint, availableEnchantments[0].AssetGuid) : null;
                if (supersededEnchantment != null) {
                    // Don't offer downgrade options.
                    var existingIndex = availableEnchantments.FindIndex(enchantment => enchantment.AssetGuid == supersededEnchantment);
                    availableEnchantments = availableEnchantments.Skip(existingIndex + 1).ToArray();
                }

                if (availableEnchantments.Length > 0 && selectedRecipe.Enchantments.Length > 1) {
                    var counter = selectedRecipe.Enchantments.Length - availableEnchantments.Length;
                    var enchantmentNames = availableEnchantments.Select(enchantment => {
                        counter++;
                        return enchantment.Name.Empty() ? GetBonusString(counter, selectedRecipe) : enchantment.Name;
                    });
                    selectedEnchantmentIndex = RenderSelection("", enchantmentNames.ToArray(), 6);
                } else if (availableEnchantments.Length == 0) {
                    RenderLabel("This item cannot be further upgraded with this enchantment.");
                    return;
                }

                selectedEnchantment = availableEnchantments[selectedEnchantmentIndex];
            }

            var casterLevel = selectedRecipe.CasterLevelStart
                              + (selectedEnchantment == null
                                  ? 0
                                  : selectedRecipe.Enchantments.IndexOf(selectedEnchantment) * selectedRecipe.CasterLevelMultiplier);
            if (selectedEnchantment != null) {
                if (!string.IsNullOrEmpty(selectedEnchantment.Description)) {
                    RenderLabel(selectedEnchantment.Description);
                }
                if (selectedRecipe.CostType == RecipeCostType.EnhancementLevelSquared) {
                    RenderLabel($"Plus equivalent: +{(selectedRecipe.Enchantments.IndexOf(selectedEnchantment) + 1) * selectedRecipe.CostFactor}");
                }
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

            RenderCraftingSkillInformation(caster, StatType.SkillKnowledgeArcana, 5 + casterLevel, casterLevel, selectedRecipe.PrerequisiteSpells,
                selectedRecipe.AnyPrerequisite, selectedRecipe.CrafterPrerequisites);

            if (selectedRecipe.ResultItem != null) {
                // Just craft the item resulting from the recipe.
                RenderRecipeBasedCraftItemControl(caster, craftingData, selectedRecipe, casterLevel, selectedRecipe.ResultItem);
                return;
            }

            // See if the selected enchantment (plus optional mundane base item) corresponds to a vanilla blueprint.
            var allItemBlueprintsWithEnchantment = FindItemBlueprintForEnchantmentId(selectedEnchantment.AssetGuid);
            var matchingItem = allItemBlueprintsWithEnchantment?.FirstOrDefault(blueprint =>
                DoesBlueprintMatchSlot(blueprint, selectedSlot)
                && DoesItemMatchEnchantments(blueprint, selectedEnchantment.AssetGuid, upgradeItem?.Blueprint as BlueprintItemEquipment, true)
            );
            BlueprintItemEquipment itemToCraft;
            var itemGuid = "[not set]";
            if (matchingItem) {
                // Crafting an existing blueprint.
                itemToCraft = matchingItem;
            } else if (upgradeItem != null) {
                // Upgrading to a custom blueprint
                var name = upgradeItemDoubleWeapon?.Blueprint.Name ?? upgradeItem.Blueprint.Name;
                RenderCustomNameField(name);
                name = selectedCustomName == name ? null : selectedCustomName;
                IEnumerable<string> enchantments;
                string supersededEnchantmentId;
                if (selectedRecipe.EnchantmentsCumulative) {
                    enchantments = availableEnchantments.Take(selectedEnchantmentIndex + 1).Select(enchantment => enchantment.AssetGuid);
                    supersededEnchantmentId = null;
                } else {
                    enchantments = new List<string> {selectedEnchantment.AssetGuid};
                    supersededEnchantmentId = FindSupersededEnchantmentId(upgradeItem.Blueprint, selectedEnchantment.AssetGuid);
                }

                itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(upgradeItem.Blueprint.AssetGuid, enchantments,
                    supersededEnchantmentId == null ? null : new[] {supersededEnchantmentId}, name, descriptionId: "null");
                if (upgradeItemDoubleWeapon != null) {
                    // itemGuid is the blueprint GUID of the second end of upgradeItemWeapon - build the overall blueprint with the custom second end.
                    itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(upgradeItemDoubleWeapon.Blueprint.AssetGuid, Enumerable.Empty<string>(),
                        name: name, descriptionId: "null", secondEndGuid: itemGuid);
                    upgradeItem = upgradeItemDoubleWeapon;
                }
                itemToCraft = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(itemGuid);
            } else {
                // Crafting a new custom blueprint from scratch.
                SelectRandomApplicableBaseGuid(craftingData, selectedSlot);
                var baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                RenderCustomNameField($"{new L10NString(selectedRecipe.NameId)} {new L10NString(GetSlotStringKey(selectedSlot))}");
                var enchantmentsToRemove = GetEnchantments(baseBlueprint, selectedRecipe).Select(enchantment => enchantment.AssetGuid).ToArray();
                itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(selectedBaseGuid, new List<string> {selectedEnchantment.AssetGuid}, enchantmentsToRemove,
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

        private static string GetEnchantmentNames(BlueprintItem blueprint) {
            var weapon = blueprint as BlueprintItemWeapon;
            var material = weapon ? weapon.DamageType.Physical.Material : 0;
            return blueprint.Enchantments
                .Where(enchantment => !string.IsNullOrEmpty(enchantment.Name))
                .Select(enchantment => enchantment.Name)
                .OrderBy(name => name)
                .PrependConditional(material != 0, LocalizedTexts.Instance.DamageMaterial.GetText(material))
                .Join();
        }

        private static string BuildItemDescription(ItemEntity item) {
            var description = item.Description;
            if (CraftMagicItemsBlueprintPatcher.SlotsWhichShowEnchantments.Contains(item.Blueprint.ItemType)) {
                string qualities;
                if (item.Blueprint is BlueprintItemShield shield) {
                    qualities = GetEnchantmentNames(shield.ArmorComponent);
                    var weaponQualities = shield.WeaponComponent == null ? null : GetEnchantmentNames(shield.WeaponComponent);
                    if (!string.IsNullOrEmpty(weaponQualities)) {
                        qualities = $"{qualities}{(string.IsNullOrEmpty(qualities) ? "" : ", ")}{ShieldBashLocalized}: {weaponQualities}";
                    }
                } else {
                    qualities = GetEnchantmentNames(item.Blueprint);
                }
                if (!string.IsNullOrEmpty(qualities)) {
                    description += $"{(string.IsNullOrEmpty(description) ? "" : "\n")}{QualitiesLocalized}: {qualities}";
                }
            }
            return description;
        }

        private static void SelectRandomApplicableBaseGuid(ItemCraftingData craftingData, ItemsFilter.ItemType selectedSlot) {
            if (selectedBaseGuid != null) {
                var baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                if (!baseBlueprint || !DoesBlueprintMatchSlot(baseBlueprint, selectedSlot)) {
                    selectedBaseGuid = null;
                }
            }

            selectedBaseGuid = selectedBaseGuid ?? RandomBaseBlueprintId(craftingData,
                                   guid => DoesBlueprintMatchSlot(ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(guid), selectedSlot));
        }

        private static void RenderCastSpellNTimes(UnitEntityData caster, ItemCraftingData craftingData, ItemEntity upgradeItem,
            ItemsFilter.ItemType selectedSlot) {
            BlueprintItemEquipment equipment = null;
            if (upgradeItem != null) {
                equipment = upgradeItem.Blueprint as BlueprintItemEquipment;
                if (equipment == null || equipment.Ability != null && equipment.SpendCharges && !equipment.RestoreChargesOnRest) {
                    RenderLabel($"{upgradeItem.Name} cannot cast a spell N times a day (this is unexpected - please let the mod author know)");
                    return;
                }
            }

            BlueprintAbility ability;
            int spellLevel;
            if (equipment == null || equipment.Ability == null) {
                // Choose a spellbook known to the caster
                var spellbooks = caster.Descriptor.Spellbooks.ToList();
                var spellBookNames = spellbooks.Select(book => book.Blueprint.Name.ToString()).Concat(Enumerable.Repeat("From Items", 1)).ToArray();
                var selectedSpellbookIndex = RenderSelection("Source: ", spellBookNames, 10, ref selectedCustomName);
                if (selectedSpellbookIndex < spellbooks.Count) {
                    var spellbook = spellbooks[selectedSpellbookIndex];
                    // Choose a spell level
                    var spellLevelNames = Enumerable.Range(0, spellbook.Blueprint.MaxSpellLevel + 1).Select(index => $"Level {index}").ToArray();
                    spellLevel = RenderSelection("Spell level: ", spellLevelNames, 10, ref selectedCustomName);
                    var specialSpellLists = Accessors.GetSpellbookSpecialLists(spellbook);
                    var spellOptions = spellbook.Blueprint.SpellList.GetSpells(spellLevel)
                        .Concat(specialSpellLists.Aggregate(new List<BlueprintAbility>(), (allSpecial, spellList) => spellList.GetSpells(spellLevel)))
                        .Distinct()
                        .OrderBy(spell => spell.Name)
                        .ToArray();
                    if (!spellOptions.Any()) {
                        RenderLabel($"There are no level {spellLevel} {spellbook.Blueprint.Name} spells");
                        return;
                    }

                    var spellNames = spellOptions.Select(spell => spell.Name).ToArray();
                    var selectedSpellIndex = RenderSelection("Spell: ", spellNames, 4, ref selectedCustomName);
                    ability = spellOptions[selectedSpellIndex];
                } else {
                    var itemBlueprints = Game.Instance.Player.Inventory
                        .Where(item => item.Wielder == caster.Descriptor)
                        .Select(item => item.Blueprint)
                        .OfType<BlueprintItemEquipment>()
                        .Where(blueprint => blueprint.Ability != null && blueprint.Ability.IsSpell
                                                                      && (!(blueprint is BlueprintItemEquipmentUsable usable) ||
                                                                          usable.Type != UsableItemType.Potion))
                        .OrderBy(item => item.Name)
                        .ToArray();
                    if (itemBlueprints.Length == 0) {
                        RenderLabel("You are not wielding any items that can cast spells.");
                        return;
                    }
                    var itemNames = itemBlueprints.Select(item => item.Name).ToArray();
                    var itemIndex = RenderSelection("Cast from item: ", itemNames, 5, ref selectedCustomName);
                    var selectedItemBlueprint = itemBlueprints[itemIndex];
                    ability = selectedItemBlueprint.Ability;
                    spellLevel = selectedItemBlueprint.SpellLevel;
                    RenderLabel($"Spell: {ability.Name}");
                }
            } else {
                ability = equipment.Ability;
                spellLevel = equipment.SpellLevel;
                GameLogContext.Count = equipment.Charges;
                RenderLabel($"Current: {L10NFormat("craftMagicItems-label-cast-spell-n-times-details", ability.Name, equipment.CasterLevel)}");
                GameLogContext.Clear();
            }

            // Choose a caster level
            var minCasterLevel = Math.Max(equipment == null ? 0 : equipment.CasterLevel, Math.Max(1, 2 * spellLevel - 1));
            RenderIntSlider(ref selectedCasterLevel, "Caster level: ", minCasterLevel, 20);
            // Choose number of times per day
            var maxCastsPerDay = equipment == null ? 10 : ((equipment.Charges + 10) / 10) * 10;
            RenderIntSlider(ref selectedCastsPerDay, "Casts per day: ", equipment == null ? 1 : equipment.Charges, maxCastsPerDay);
            if (equipment != null && ability == equipment.Ability && selectedCasterLevel == equipment.CasterLevel && selectedCastsPerDay == equipment.Charges) {
                RenderLabel($"No changes made to {equipment.Name}");
                return;
            }

            // Show skill info
            RenderCraftingSkillInformation(caster, StatType.SkillKnowledgeArcana, 5 + selectedCasterLevel, selectedCasterLevel, new[] {ability});

            string itemGuid;
            if (upgradeItem == null) {
                // Option to rename item
                RenderCustomNameField($"{ability.Name} {new L10NString(GetSlotStringKey(selectedSlot))}");
                // Pick random base item
                SelectRandomApplicableBaseGuid(craftingData, selectedSlot);
                // Create customised item GUID
                var baseBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(selectedBaseGuid);
                var enchantmentsToRemove = GetEnchantments(baseBlueprint).Select(enchantment => enchantment.AssetGuid).ToArray();
                itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(selectedBaseGuid, new List<string>(), enchantmentsToRemove, selectedCustomName,
                    ability.AssetGuid, "null", casterLevel: selectedCasterLevel, spellLevel: spellLevel, perDay: selectedCastsPerDay);
            } else {
                // Option to rename item
                RenderCustomNameField(upgradeItem.Blueprint.Name);
                // Create customised item GUID
                itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(upgradeItem.Blueprint.AssetGuid, new List<string>(), null,
                    selectedCustomName == upgradeItem.Blueprint.Name ? null : selectedCustomName, ability.AssetGuid,
                    casterLevel: selectedCasterLevel == equipment.CasterLevel ? -1 : selectedCasterLevel,
                    spellLevel: spellLevel == equipment.SpellLevel ? -1 : spellLevel,
                    perDay: selectedCastsPerDay == equipment.Charges ? -1 : selectedCastsPerDay);
            }

            var itemToCraft = ResourcesLibrary.TryGetBlueprint<BlueprintItemEquipment>(itemGuid);
            // Render craft button
            GameLogContext.Count = selectedCastsPerDay;
            RenderLabel(L10NFormat("craftMagicItems-label-cast-spell-n-times-details", ability.Name, selectedCasterLevel));
            GameLogContext.Clear();
            var recipe = new RecipeData {
                PrerequisiteSpells = new[] {ability}
            };
            RenderRecipeBasedCraftItemControl(caster, craftingData, recipe, selectedCasterLevel, itemToCraft, upgradeItem);
        }

        public static int CharacterCasterLevel(UnitDescriptor character, Spellbook forSpellbook = null) {
            // There can be modifiers to Caster Level beyond what's set in a character's Spellbooks (e.g Magical Knack) - use event system.
            var casterLevel = 0;
            var booksToCheck = forSpellbook == null ? character.Spellbooks : Enumerable.Repeat(forSpellbook, 1);
            foreach (var spellbook in booksToCheck) {
                if (spellbook.CasterLevel > 0) {
                    var blueprintAbility = ScriptableObject.CreateInstance<BlueprintAbility>();
                    var rule = new RuleCalculateAbilityParams(character.Unit, blueprintAbility, spellbook);
                    RulebookEventBus.OnEventAboutToTrigger(rule);
                    rule.OnTrigger(null);
                    casterLevel = rule.Result.CasterLevel > casterLevel ? rule.Result.CasterLevel : casterLevel;
                }
            }

            return casterLevel;
        }

        private static SpellSchool CheckForOppositionSchool(UnitDescriptor crafter, BlueprintAbility[] prerequisiteSpells) {
            if (prerequisiteSpells != null) {
                foreach (var spell in prerequisiteSpells) {
                    if (crafter.Spellbooks.Any(spellbook => spellbook.Blueprint.SpellList.Contains(spell)
                                                            && spellbook.OppositionSchools.Contains(spell.School))) {
                        return spell.School;
                    }
                }
            }
            return SpellSchool.None;
        }

        private static int RenderCraftingSkillInformation(UnitEntityData crafter, StatType skill, int dc, int casterLevel = 0,
            BlueprintAbility[] prerequisiteSpells = null, bool anyPrerequisite = false, CrafterPrerequisiteType[] crafterPrerequisites = null,
            bool render = true) {
            if (render) {
                RenderLabel($"Base Crafting DC: {dc}");
            }
            // ReSharper disable once UnusedVariable
            var missing = CheckSpellPrerequisites(prerequisiteSpells, anyPrerequisite, crafter.Descriptor, false, out var missingSpells,
                // ReSharper disable once UnusedVariable
                out var spellsToCast);
            missing += GetMissingCrafterPrerequisites(crafterPrerequisites, crafter.Descriptor).Count;
            var crafterCasterLevel = CharacterCasterLevel(crafter.Descriptor);
            var casterLevelShortfall = Math.Max(0, casterLevel - crafterCasterLevel);
            if (casterLevelShortfall > 0 && ModSettings.CasterLevelIsSinglePrerequisite) {
                missing++;
                casterLevelShortfall = 0;
            }
            if (missing > 0 && render) {
                RenderLabel(
                    $"{crafter.CharacterName} is unable to meet {missing} of the prerequisites, raising the DC by {MissingPrerequisiteDCModifier * missing}");
            }
            if (casterLevelShortfall > 0 && render) {
                RenderLabel(L10NFormat("craftMagicItems-logMessage-low-caster-level", casterLevel, MissingPrerequisiteDCModifier * casterLevelShortfall));
            }
            // Rob's ruling... if you're below the prerequisite caster level, you're considered to be missing a prerequisite for each
            // level you fall short.
            dc += MissingPrerequisiteDCModifier * (missing + casterLevelShortfall);
            var oppositionSchool = CheckForOppositionSchool(crafter.Descriptor, prerequisiteSpells);
            if (oppositionSchool != SpellSchool.None) {
                dc += OppositionSchoolDCModifier;
                if (render) {
                    RenderLabel(L10NFormat("craftMagicItems-logMessage-opposition-school", LocalizedTexts.Instance.SpellSchoolNames.GetText(oppositionSchool),
                        OppositionSchoolDCModifier));
                }
            }
            var skillCheck = 10 + crafter.Stats.GetStat(skill).ModifiedValue;
            if (render) {
                RenderLabel(L10NFormat("craftMagicItems-logMessage-made-progress-check", LocalizedTexts.Instance.Stats.GetText(skill), skillCheck, dc));
            }

            var skillMargin = skillCheck - dc;
            if (skillMargin < 0 && render) {
                RenderLabel(ModSettings.CraftingTakesNoTime
                    ? $"This project would be too hard for {crafter.CharacterName} if \"Crafting Takes No Time\" cheat was disabled."
                    : $"<color=red>Warning:</color> This project will be too hard for {crafter.CharacterName}");
            }

            return skillMargin;
        }

        private static int GetMaterialComponentMultiplier(ItemCraftingData craftingData, BlueprintItem resultBlueprint = null,
            BlueprintItem upgradeBlueprint = null) {
            if (craftingData is SpellBasedItemCraftingData spellBased) {
                return spellBased.Charges;
            }

            var upgradeEquipment = upgradeBlueprint as BlueprintItemEquipment;
            if (resultBlueprint is BlueprintItemEquipment resultEquipment && resultEquipment.RestoreChargesOnRest
                                                                          && resultEquipment.Ability !=
                                                                          (upgradeEquipment == null ? null : upgradeEquipment.Ability)) {
                // Cast a Spell N times a day costs material components as if it has 50 charges.
                return 50;
            }

            return 0;
        }

        private static void CancelCraftingProject(CraftingProjectData project) {
            // Refund gold and material components.
            if (!ModSettings.CraftingCostsNoGold) {
                Game.Instance.UI.Common.UISound.Play(UISoundType.LootCollectGold);
                var goldRefund = project.GoldSpent >= 0 ? project.GoldSpent : project.TargetCost;
                Game.Instance.Player.GainMoney(goldRefund);
                var craftingData = ItemCraftingData.FirstOrDefault(data => data.Name == project.ItemType);
                BuildCostString(out var cost, craftingData, goldRefund, project.Prerequisites, project.ResultItem.Blueprint, project.UpgradeItem?.Blueprint);
                var factor = GetMaterialComponentMultiplier(craftingData, project.ResultItem.Blueprint, project.UpgradeItem?.Blueprint);
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
                case BlueprintItemEquipmentUsable usable when usable.AssetGuid == "fd56596e273d1ff49a8c29cc9802ae6e":
                    // Alchemist's Fire has a DC of 20
                    dc += 5;
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
            var itemTypes = ItemCraftingData
                .Where(data => data.FeatGuid == null
                               && (data.ParentNameId == null || SubCraftingData[data.ParentNameId][0] == data))
                .ToArray();
            var itemTypeNames = itemTypes.Select(data => new L10NString(data.ParentNameId ?? data.NameId).ToString()).ToArray();
            var selectedItemTypeIndex = upgradingBlueprint == null
                ? RenderSelection("Crafting: ", itemTypeNames, 6, ref selectedCustomName)
                : GetSelectionIndex("Crafting: ");

            var selectedCraftingData = itemTypes[selectedItemTypeIndex];
            if (selectedCraftingData.ParentNameId != null) {
                itemTypeNames = SubCraftingData[selectedCraftingData.ParentNameId].Select(data => new L10NString(data.NameId).ToString()).ToArray();
                var selectedItemSubTypeIndex = 0;
                if (upgradingBlueprint == null) {
                    selectedItemSubTypeIndex = RenderSelection($"{new L10NString(selectedCraftingData.ParentNameId)}: ", itemTypeNames, 6);
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

                var selectedUpgradeItemIndex = RenderSelection("Item: ", blueprintNames, 5, ref selectedCustomName);
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
            var selectedRecipeIndex = RenderSelection("Craft: ", recipeNames, 6, ref selectedCustomName);
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

            RenderCraftingSkillInformation(crafter, StatType.SkillKnowledgeWorld, dc);

            // Upgrading to a custom blueprint, rather than use the standard mithral/adamantine blueprints.
            var enchantments = selectedEnchantment == null ? new List<string>() : new List<string> {selectedEnchantment.AssetGuid};
            var upgradeName = selectedRecipe != null && selectedRecipe.Material != 0
                ? new L10NString(selectedRecipe.NameId).ToString()
                : selectedEnchantment == null
                    ? null
                    : selectedEnchantment.Name;
            var name = upgradeName == null ? baseBlueprint.Name : $"{upgradeName} {baseBlueprint.Name}";
            var visual = ApplyVisualMapping(selectedRecipe, baseBlueprint);
            var itemToCraft = baseBlueprint;
            var itemGuid = "[not set]";
            if (selectedRecipe != null) {
                var doubleWeapon = baseBlueprint as BlueprintItemWeapon;
                if (doubleWeapon) {
                    if (doubleWeapon.Double) {
                        baseBlueprint = doubleWeapon.SecondWeapon;
                    } else {
                        doubleWeapon = null;
                    }
                }
                itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(baseBlueprint.AssetGuid, enchantments, null, name, null, null,
                    selectedRecipe.Material, visual);
                if (doubleWeapon) {
                    baseBlueprint = doubleWeapon;
                    itemGuid = blueprintPatcher.BuildCustomRecipeItemGuid(baseBlueprint.AssetGuid, enchantments, null, name, null, null,
                        selectedRecipe.Material, visual, secondEndGuid: itemGuid);
                }
                itemToCraft = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(itemGuid);
            }

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
                GUILayout.Label($"   <b>{project.ResultItem.Name}</b> is {100 * project.Progress / project.TargetCost}% complete.",
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
                RenderLabel($"       {BuildItemDescription(project.ResultItem).Replace("\n", "\n       ")}");
                RenderLabel($"       {project.LastMessage}");
            }
        }

        private static void RenderFeatReassignmentSection() {
            var caster = GetSelectedCrafter(false);
            if (caster == null) {
                return;
            }

            var casterLevel = CharacterCasterLevel(caster.Descriptor);
            var missingFeats = ItemCraftingData
                .Where(data => data.FeatGuid != null && !CharacterHasFeat(caster, data.FeatGuid) && data.MinimumCasterLevel <= casterLevel)
                .ToArray();
            if (missingFeats.Length == 0) {
                RenderLabel($"{caster.CharacterName} does not currently qualify for any crafting feats.");
                return;
            }

            RenderLabel(
                "Use this section to reassign previous feat choices for this character to crafting feats.  <color=red>Warning:</color> This is a one-way assignment!");
            var selectedFeatToLearn = RenderSelection("Feat to learn", missingFeats.Select(data => new L10NString(data.NameId).ToString()).ToArray(), 6);
            var learnFeatData = missingFeats[selectedFeatToLearn];
            var learnFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(learnFeatData.FeatGuid);
            if (learnFeat == null) {
                throw new Exception($"Unable to find feat with guid {learnFeatData.FeatGuid}");
            }

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
                        var mFacts = Accessors.GetFeatureCollectionFacts(caster.Descriptor.Progression.Features);
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
                var selectedCustomPriceScaleIndex = RenderSelection(CustomPriceLabel, CraftingPriceStrings, 4);
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
            if (!ModSettings.CraftingTakesNoTime) {
                RenderCheckbox(ref ModSettings.CustomCraftRate, "Craft at a non-standard rate.");
                if (ModSettings.CustomCraftRate) {
                    var maxMagicRate = ((ModSettings.MagicCraftingRate + 1000) / 1000) * 1000;
                    RenderIntSlider(ref ModSettings.MagicCraftingRate, "Magic Item Crafting Rate", 1, maxMagicRate);
                    var maxMundaneRate = ((ModSettings.MundaneCraftingRate + 10) / 10) * 10;
                    RenderIntSlider(ref ModSettings.MundaneCraftingRate, "Mundane Item Crafting Rate", 1, maxMundaneRate);
                } else {
                    ModSettings.MagicCraftingRate = Settings.MagicCraftingProgressPerDay;
                    ModSettings.MundaneCraftingRate = Settings.MundaneCraftingProgressPerDay;
                }
            }
            RenderCheckbox(ref ModSettings.CasterLevelIsSinglePrerequisite,
                "When crafting, a Caster Level less than the prerequisite counts as a single missing prerequisite.");
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

            const string label = "Crafter: ";
            var selectedSpellcasterIndex = GetSelectionIndex(label);
            if (render) {
                var partyNames = characters.Select(entity => $"{entity.CharacterName}" +
                                                             $"{((GetCraftingTimerComponentForCaster(entity.Descriptor)?.CraftingProjects.Any() ?? false) ? "*" : "")}")
                    .ToArray();
                selectedSpellcasterIndex = RenderSelection(label, partyNames, 8, ref upgradingBlueprint);
            }

            return characters[selectedSpellcasterIndex];
        }

        private static int RenderSelection(string label, string[] options, int xCount) {
            var dummy = "";
            return RenderSelection(label, options, xCount, ref dummy);
        }

        private static int GetSelectionIndex(string label) {
            return SelectedIndex.ContainsKey(label) ? SelectedIndex[label] : 0;
        }

        private static int RenderSelection<T>(string label, string[] options, int xCount, ref T emptyOnChange) {
            var index = GetSelectionIndex(label);
            if (index >= options.Length) {
                index = 0;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            var newIndex = GUILayout.SelectionGrid(index, options, xCount);
            if (index != newIndex) {
                emptyOnChange = default(T);
            }

            GUILayout.EndHorizontal();
            SelectedIndex[label] = newIndex;
            return newIndex;
        }

        private static void RenderIntSlider(ref int value, string label, int min, int max) {
            value = Mathf.Clamp(value, min, max);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(300)));
            GUILayout.Label($"{value}", GUILayout.ExpandWidth(false));
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

        public static void AddItemBlueprintForSpell(UsableItemType itemType, BlueprintItemEquipment itemBlueprint) {
            if (!SpellIdToItem.ContainsKey(itemType)) {
                SpellIdToItem.Add(itemType, new Dictionary<string, List<BlueprintItemEquipment>>());
            }

            if (!SpellIdToItem[itemType].ContainsKey(itemBlueprint.Ability.AssetGuid)) {
                SpellIdToItem[itemType][itemBlueprint.Ability.AssetGuid] = new List<BlueprintItemEquipment>();
            }

            SpellIdToItem[itemType][itemBlueprint.Ability.AssetGuid].Add(itemBlueprint);
        }

        public static List<BlueprintItemEquipment> FindItemBlueprintsForSpell(BlueprintScriptableObject spell, UsableItemType itemType) {
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

        private static bool CharacterHasFeat(UnitEntityData caster, string featGuid) {
            return caster.Descriptor.Progression.Features.Enumerable.Any(feat => feat.Blueprint.AssetGuid == featGuid);
        }

        private static string RandomBaseBlueprintId(ItemCraftingData itemData, Func<string, bool> selector = null) {
            var blueprintIds = selector == null ? itemData.NewItemBaseIDs : itemData.NewItemBaseIDs.Where(selector).ToArray();
            return blueprintIds[RandomGenerator.Next(blueprintIds.Length)];
        }

        private static void CraftItem(ItemEntity resultItem, ItemEntity upgradeItem = null) {
            var characters = UIUtility.GetGroup(true).Where(character => character.IsPlayerFaction && !character.Descriptor.IsPet);
            foreach (var character in characters) {
                var bondedComponent = GetBondedItemComponentForCaster(character.Descriptor);
                if (bondedComponent && bondedComponent.ownerItem == upgradeItem) {
                    bondedComponent.ownerItem = resultItem;
                }
            }

            using (new DisableBattleLog(!ModSettings.CraftingTakesNoTime)) {
                var holdingSlot = upgradeItem?.HoldingSlot;
                if (upgradeItem != null) {
                    Game.Instance.Player.Inventory.Remove(upgradeItem);
                }
                if (holdingSlot == null) {
                    Game.Instance.Player.Inventory.Add(resultItem);
                } else {
                    holdingSlot.InsertItem(resultItem);
                }
            }

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

        private static bool BuildCostString(out string cost, ItemCraftingData craftingData, int goldCost,
            IEnumerable<BlueprintAbility> spellBlueprintArray = null,
            BlueprintItem resultBlueprint = null, BlueprintItem upgradeBlueprint = null) {
            var canAfford = true;
            if (ModSettings.CraftingCostsNoGold) {
                cost = new L10NString("craftMagicItems-label-cost-free");
            } else {
                canAfford = (Game.Instance.Player.Money >= goldCost);
                var notAffordGold = canAfford ? "" : new L10NString("craftMagicItems-label-cost-gold-too-much");
                cost = L10NFormat("craftMagicItems-label-cost-gold", goldCost, notAffordGold);
                var itemTotals = new Dictionary<BlueprintItem, int>();
                if (spellBlueprintArray != null) {
                    foreach (var spellBlueprint in spellBlueprintArray) {
                        if (spellBlueprint.MaterialComponent.Item) {
                            var count = spellBlueprint.MaterialComponent.Count *
                                        GetMaterialComponentMultiplier(craftingData, resultBlueprint, upgradeBlueprint);
                            if (count > 0) {
                                if (itemTotals.ContainsKey(spellBlueprint.MaterialComponent.Item)) {
                                    itemTotals[spellBlueprint.MaterialComponent.Item] += count;
                                } else {
                                    itemTotals[spellBlueprint.MaterialComponent.Item] = count;
                                }
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
            var craftingData = ItemCraftingData.FirstOrDefault(data => data.Name == project.ItemType);
            StatType craftingSkill;
            int dc;
            int progressRate;
            if (project.ItemType == BondedItemRitual) {
                craftingSkill = StatType.SkillKnowledgeArcana;
                dc = 10 + project.Crafter.Stats.GetStat(craftingSkill).ModifiedValue;
                progressRate = ModSettings.MagicCraftingRate;
            } else if (IsMundaneCraftingData(craftingData)) {
                craftingSkill = StatType.SkillKnowledgeWorld;
                var recipeBasedItemCraftingData = (RecipeBasedItemCraftingData) craftingData;
                dc = CalculateMundaneCraftingDC(recipeBasedItemCraftingData, project.ResultItem.Blueprint, project.Crafter.Descriptor);
                progressRate = ModSettings.MundaneCraftingRate;
            } else {
                craftingSkill = StatType.SkillKnowledgeArcana;
                dc = 5 + project.CasterLevel;
                progressRate = ModSettings.MagicCraftingRate;
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
            GameLogContext.Clear();

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
            var canAfford = BuildCostString(out var cost, craftingData, goldCost, new[] {spellBlueprint});
            var custom = existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Contains(CraftMagicItemsBlueprintPatcher.BlueprintPrefix)
                ? new L10NString("craftMagicItems-label-custom").ToString()
                : "";
            var label = L10NFormat("craftMagicItems-label-craft-spell-item", custom, new L10NString(craftingData.NamePrefixId), spellBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                BlueprintItem itemBlueprint;
                if (itemBlueprintList == null) {
                    // No items for that spell exist at all - create a custom blueprint with casterLevel, spellLevel and spellId
                    var blueprintId =
                        blueprintPatcher.BuildCustomSpellItemGuid(RandomBaseBlueprintId(craftingData), casterLevel, spellLevel, spellBlueprint.AssetGuid);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else if (existingItemBlueprint == null) {
                    // No item for this spell & caster level - create a custom blueprint with casterLevel and optionally SpellLevel
                    var blueprintId = blueprintPatcher.BuildCustomSpellItemGuid(itemBlueprintList[0].AssetGuid, casterLevel,
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

            var canAfford = BuildCostString(out var cost, craftingData, goldCost, recipe?.PrerequisiteSpells ?? new BlueprintAbility[0],
                itemBlueprint, upgradeItem?.Blueprint);
            var custom = itemBlueprint.AssetGuid.Contains(CraftMagicItemsBlueprintPatcher.BlueprintPrefix)
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
                    var factor = GetMaterialComponentMultiplier(craftingData, itemBlueprint, upgradeItem?.Blueprint);
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

        public static LocalizedString BuildCustomRecipeItemDescription(BlueprintItem blueprint, IEnumerable<BlueprintItemEnchantment> enchantments,
            IList<BlueprintItemEnchantment> removed, string ability, int casterLevel, int perDay) {
            var allKnown = blueprint.Enchantments.All(enchantment => EnchantmentIdToRecipe.ContainsKey(enchantment.AssetGuid));
            var description = allKnown
                ? new L10NString("craftMagicItems-custom-description-start")
                : blueprint.Description + new L10NString("craftMagicItems-custom-description-additional");
            description += (allKnown ? blueprint.Enchantments : enchantments)
                .Select(enchantment => {
                    if (!string.IsNullOrEmpty(enchantment.Name)) {
                        return enchantment.Name;
                    }
                    var recipe = FindSourceRecipe(enchantment.AssetGuid, blueprint);
                    if (recipe.Enchantments.Length <= 1) {
                        return new L10NString(recipe.NameId);
                    }
                    var bonusString = GetBonusString(recipe.Enchantments.IndexOf(enchantment) + 1, recipe);
                    var bonusDescription = recipe.BonusTypeId != null
                        ? L10NFormat("craftMagicItems-custom-description-bonus-to", new L10NString(recipe.BonusTypeId), new L10NString(recipe.NameId))
                        : recipe.BonusToId != null
                            ? L10NFormat("craftMagicItems-custom-description-bonus-to", new L10NString(recipe.NameId), new L10NString(recipe.BonusToId))
                            : L10NFormat("craftMagicItems-custom-description-bonus", new L10NString(recipe.NameId));
                    var upgradeFrom = allKnown ? null : removed.FirstOrDefault(remove => FindSourceRecipe(remove.AssetGuid, blueprint) == recipe);
                    if (upgradeFrom == null) {
                        return L10NFormat("craftMagicItems-custom-description-enchantment-template", bonusString, bonusDescription);
                    }
                    var oldBonus = recipe.Enchantments.IndexOf(upgradeFrom) + 1;
                    return L10NFormat("craftMagicItems-custom-description-enchantment-upgrade-template", bonusDescription, oldBonus,
                        bonusString);
                })
                .OrderBy(enchantmentDescription => enchantmentDescription)
                .Select(enchantmentDescription => "\n * " + enchantmentDescription)
                .Join("");

            if (blueprint is BlueprintItemEquipment equipment && (ability != null && ability != "null" || casterLevel > -1 || perDay > -1)) {
                GameLogContext.Count = equipment.Charges;
                description += "\n * " + L10NFormat("craftMagicItems-label-cast-spell-n-times-details", equipment.Ability.Name, equipment.CasterLevel);
                GameLogContext.Clear();
            }

            return new FakeL10NString(description);
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
                    case RecipeCostType.Flat:
                        return recipe.CostFactor;
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
                    var assetGuid = CustomBlueprintBuilder.AssetGuidWithoutMatch(weapon.AssetGuid);
                    var baseWeapon = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(assetGuid);
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

        public static int RulesRecipeItemCost(BlueprintItem blueprint) {
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

            if (blueprint is BlueprintItemEquipment equipment && equipment.Ability != null && equipment.RestoreChargesOnRest) {
                var castSpellCost = (int) (equipment.Charges * equipment.CasterLevel * 360 * (equipment.SpellLevel == 0 ? 0.5 : equipment.SpellLevel));
                cost += castSpellCost;
                if (mostExpensiveEnchantmentCost < castSpellCost) {
                    mostExpensiveEnchantmentCost = castSpellCost;
                }
            }

            if (blueprint is BlueprintItemArmor || blueprint is BlueprintItemWeapon) {
                if (blueprint is BlueprintItemWeapon weapon && weapon.DamageType.Physical.Material != 0) {
                    cost += GetSpecialMaterialCost(weapon.DamageType.Physical.Material, weapon);
                }

                var enhancementLevel = ItemPlusEquivalent(blueprint);
                var factor = blueprint is BlueprintItemWeapon ? WeaponPlusCost : ArmourPlusCost;
                cost += enhancementLevel * enhancementLevel * factor;
                if (blueprint is BlueprintItemWeapon doubleWeapon && doubleWeapon.Double) {
                    return cost + RulesRecipeItemCost(doubleWeapon.SecondWeapon);
                }
                return cost;
            }

            // Usable (belt slot) items cost double.
            return (3 * cost - mostExpensiveEnchantmentCost) / (blueprint is BlueprintItemEquipmentUsable ? 1 : 2);
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

        [Harmony12.HarmonyPatch(typeof(MainMenu), "Start")]
        private static class MainMenuStartPatch {
            private static bool mainMenuStarted;

            private static void InitialiseCraftingData() {
                // Read the crafting data now that ResourcesLibrary is loaded.
                ItemCraftingData = ReadJsonFile<ItemCraftingData[]>($"{ModEntry.Path}/Data/ItemTypes.json", new CraftingTypeConverter());
                // Initialise lookup tables.
                foreach (var itemData in ItemCraftingData) {
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
                CustomLootItems = ReadJsonFile<CustomLootItem[]>($"{ModEntry.Path}/Data/LootItems.json");
            }

            private static void AddCraftingFeats(ObjectIDGenerator idGenerator, BlueprintProgression progression) {
                foreach (var levelEntry in progression.LevelEntries) {
                    foreach (var featureBase in levelEntry.Features) {
                        var selection = featureBase as BlueprintFeatureSelection;
                        if (selection != null && (CraftingFeatGroups.Contains(selection.Group) || CraftingFeatGroups.Contains(selection.Group2))) {
                            // Use ObjectIDGenerator to detect which shared lists we've added the feats to.
                            idGenerator.GetId(selection.AllFeatures, out var firstTime);
                            if (firstTime) {
                                foreach (var data in ItemCraftingData) {
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
                var idGenerator = new ObjectIDGenerator();
                // Add crafting feats to general feat selection
                AddCraftingFeats(idGenerator, Game.Instance.BlueprintRoot.Progression.FeatsProgression);
                // ... and to relevant class feat selections.
                foreach (var characterClass in Game.Instance.BlueprintRoot.Progression.CharacterClasses) {
                    AddCraftingFeats(idGenerator, characterClass.Progression);
                }

                // Alchemists get Brew Potion as a bonus 1st level feat
                var brewPotionData = ItemCraftingData.First(data => data.Name == "Potion");
                var brewPotion = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(brewPotionData.FeatGuid);
                var alchemistProgression = ResourcesLibrary.TryGetBlueprint<BlueprintProgression>(AlchemistProgressionGuid);
                if (brewPotion != null && alchemistProgression != null) {
                    foreach (var levelEntry in alchemistProgression.LevelEntries) {
                        if (levelEntry.Level == 1) {
                            levelEntry.Features.Add(brewPotion);
                        }
                    }

                    alchemistProgression.UIDeterminatorsGroup = alchemistProgression.UIDeterminatorsGroup.Concat(new[] {brewPotion}).ToArray();
                } else {
                    ModEntry.Logger.Warning("Failed to locate Alchemist progression or Brew Potion feat!");
                }
            }

            private static void InitialiseMod() {
                if (modEnabled) {
                    InitialiseCraftingData();
                    AddAllCraftingFeats();
                }
            }

            [Harmony12.HarmonyPriority(Harmony12.Priority.Last)]
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                if (!mainMenuStarted) {
                    mainMenuStarted = true;
                    InitialiseMod();
                }
            }

            public static void ModEnabledChanged() {
                if (!modEnabled) {
                    // Reset everything InitialiseMod initialises
                    ItemCraftingData = null;
                    SubCraftingData.Clear();
                    SpellIdToItem.Clear();
                    EnchantmentIdToItem.Clear();
                    EnchantmentIdToCost.Clear();
                    EnchantmentIdToRecipe.Clear();
                } else if (mainMenuStarted) {
                    // If the mod is enabled and we're past the Start of main menu, (re-)initialise.
                    InitialiseMod();
                }
            }
        }

        // Fix issue in Owlcat's UI - ActionBarManager.Update does not refresh the Groups (spells/Actions/Belt)
        [Harmony12.HarmonyPatch(typeof(ActionBarManager), "Update")]
        // ReSharper disable once UnusedMember.Local
        private static class ActionBarManagerUpdatePatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(ActionBarManager __instance) {
                var mNeedReset = Accessors.GetActionBarManagerNeedReset(__instance);
                if (mNeedReset) {
                    var mSelected = Accessors.GetActionBarManagerSelected(__instance);
                    __instance.Group.Set(mSelected);
                }
            }
        }

        [Harmony12.HarmonyPatch(typeof(BlueprintItemEquipmentUsable), "Cost", Harmony12.MethodType.Getter)]
        // ReSharper disable once UnusedMember.Local
        private static class BlueprintItemEquipmentUsableCostPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(BlueprintItemEquipmentUsable __instance, ref int __result) {
                if (__result == 0 && __instance.SpellLevel == 0) {
                    // Owlcat's cost calculation doesn't handle level 0 spells properly.
                    int chargeCost;
                    switch (__instance.Type) {
                        case UsableItemType.Wand:
                            chargeCost = 15;
                            break;
                        case UsableItemType.Scroll:
                            chargeCost = 25;
                            break;
                        case UsableItemType.Potion:
                            chargeCost = 50;
                            break;
                        default:
                            return;
                    }
                    __result = __instance.CasterLevel * chargeCost * __instance.Charges / 2;
                }
            }
        }

        // Load Variant spells into m_KnownSpellLevels
        [Harmony12.HarmonyPatch(typeof(Spellbook), "PostLoad")]
        // ReSharper disable once UnusedMember.Local
        private static class SpellbookPostLoadPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(Spellbook __instance) {
                if (!modEnabled) {
                    return;
                }

                var mKnownSpells = Accessors.GetSpellbookKnownSpells(__instance);
                var mKnownSpellLevels = Accessors.GetSpellbookKnownSpellLevels(__instance);
                for (var level = 0; level < mKnownSpells.Length; ++level) {
                    foreach (var spell in mKnownSpells[level]) {
                        if (spell.Blueprint.Variants != null) {
                            foreach (var variant in spell.Blueprint.Variants) {
                                mKnownSpellLevels[variant] = level;
                            }
                        }
                    }
                }
            }
        }

        // Owlcat's code doesn't correctly detect that a variant spell is in a spellList when its parent spell is. 
        [Harmony12.HarmonyPatch(typeof(BlueprintAbility), "IsInSpellList")]
        // ReSharper disable once UnusedMember.Global
        public static class BlueprintAbilityIsInSpellListPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(BlueprintAbility __instance, BlueprintSpellList spellList, ref bool __result) {
                if (!__result && __instance.Parent != null && __instance.Parent != __instance) {
                    __result = __instance.Parent.IsInSpellList(spellList);
                }
            }
        }

        public static void AddBattleLogMessage(string message, object tooltip = null, Color? color = null) {
            var data = new LogDataManager.LogItemData(message, color ?? GameLogStrings.Instance.DefaultColor, tooltip, PrefixIcon.None);
            if (Game.Instance.UI.BattleLogManager) {
                Game.Instance.UI.BattleLogManager.LogView.AddLogEntry(data);
            } else {
                PendingLogItems.Add(data);
            }
        }

        [Harmony12.HarmonyPatch(typeof(LogDataManager.LogItemData), "UpdateSize")]
        // ReSharper disable once UnusedMember.Local
        private static class LogItemDataUpdateSizePatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix() {
                // Avoid null pointer exception when BattleLogManager not set.
                return Game.Instance.UI.BattleLogManager != null;
            }
        }

        [Harmony12.HarmonyPatch(typeof(BattleLogManager), "Initialize")]
        // ReSharper disable once UnusedMember.Local
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
                    || prerequisite == CrafterPrerequisiteType.FeatureChannelEnergy &&
                    caster.GetFeature(ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(ChannelEnergyFeatureGuid)) == null
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

                var craftingData = ItemCraftingData.FirstOrDefault(data => data.Name == project.ItemType);
                StatType craftingSkill;
                int dc;
                int progressRate;

                if (project.ItemType == BondedItemRitual) {
                    craftingSkill = StatType.SkillKnowledgeArcana;
                    dc = 10 + project.Crafter.Stats.GetStat(craftingSkill).ModifiedValue;
                    progressRate = ModSettings.MagicCraftingRate;
                } else if (IsMundaneCraftingData(craftingData)) {
                    craftingSkill = StatType.SkillKnowledgeWorld;
                    dc = CalculateMundaneCraftingDC((RecipeBasedItemCraftingData) craftingData, project.ResultItem.Blueprint, caster);
                    progressRate = ModSettings.MundaneCraftingRate;
                } else {
                    craftingSkill = StatType.SkillKnowledgeArcana;
                    dc = 5 + project.CasterLevel;
                    progressRate = ModSettings.MagicCraftingRate;
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
                var casterLevel = CharacterCasterLevel(caster);
                if (casterLevel < project.CasterLevel) {
                    // Rob's ruling... if you're below the prerequisite caster level, you're considered to be missing a prerequisite for each
                    // level you fall short, unless ModSettings.CasterLevelIsSinglePrerequisite is true.
                    var casterLevelPenalty = ModSettings.CasterLevelIsSinglePrerequisite
                        ? MissingPrerequisiteDCModifier
                        : MissingPrerequisiteDCModifier * (project.CasterLevel - casterLevel);
                    dc += casterLevelPenalty;
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-low-caster-level", project.CasterLevel, casterLevelPenalty));
                }
                var oppositionSchool = CheckForOppositionSchool(caster, project.Prerequisites);
                if (oppositionSchool != SpellSchool.None) {
                    dc += OppositionSchoolDCModifier;
                    AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-opposition-school",
                        LocalizedTexts.Instance.SpellSchoolNames.GetText(oppositionSchool), OppositionSchoolDCModifier));
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
                                GameLogContext.Clear();
                                itemSpell.SourceItem.SpendCharges(caster);
                            }
                        }
                    } else if (isAdventuring) {
                        // Actually cast the spells if we're adventuring.
                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-expend-spell", spell.Name));
                        spell.SpendFromSpellbook();
                    }
                }

                var progressKey = project.ItemType == BondedItemRitual
                    ? "craftMagicItems-logMessage-made-progress-bondedItem"
                    : "craftMagicItems-logMessage-made-progress";
                var progress = L10NFormat(progressKey, progressGold, project.TargetCost - project.Progress, project.ResultItem.Name);
                var checkResult = L10NFormat("craftMagicItems-logMessage-made-progress-check", LocalizedTexts.Instance.Stats.GetText(craftingSkill),
                    skillCheck, dc);
                AddBattleLogMessage(progress, checkResult);
                daysAvailableToCraft -= daysCrafting;
                project.Progress += progressGold;
                if (project.Progress >= project.TargetCost) {
                    // Completed the project!
                    if (project.ItemType == BondedItemRitual) {
                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-bonding-ritual-complete", project.ResultItem.Name), project.ResultItem);
                        BondWithObject(project.Crafter, project.ResultItem);
                    } else {
                        AddBattleLogMessage(L10NFormat("craftMagicItems-logMessage-crafting-complete", project.ResultItem.Name), project.ResultItem);
                        CraftItem(project.ResultItem, project.UpgradeItem);
                    }
                    timer.CraftingProjects.Remove(project);
                    if (project.UpgradeItem == null) {
                        ItemCreationProjects.Remove(project);
                    } else {
                        ItemUpgradeProjects.Remove(project.UpgradeItem);
                    }
                } else {
                    var completeKey = project.ItemType == BondedItemRitual
                        ? "craftMagicItems-logMessage-made-progress-bonding-ritual-amount-complete"
                        : "craftMagicItems-logMessage-made-progress-amount-complete";
                    var amountComplete = L10NFormat(completeKey, project.ResultItem.Name, 100 * project.Progress / project.TargetCost);
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

        [Harmony12.HarmonyPatch(typeof(CapitalCompanionLogic), "OnFactActivate")]
        // ReSharper disable once UnusedMember.Local
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

        [Harmony12.HarmonyPatch(typeof(RestController), "ApplyRest")]
        // ReSharper disable once UnusedMember.Local
        private static class RestControllerApplyRestPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(UnitDescriptor unit) {
                WorkOnProjects(unit, false);
            }
        }

        private static void AddToLootTables(BlueprintItem blueprint, string[] tableNames, bool firstTime) {
            var tableCount = tableNames.Length;
            foreach (var loot in ResourcesLibrary.GetBlueprints<BlueprintLoot>()) {
                if (tableNames.Contains(loot.name)) {
                    tableCount--;
                    if (!loot.Items.Any(entry => entry.Item == blueprint)) {
                        var lootItems = loot.Items.ToList();
                        lootItems.Add(new LootEntry {Count = 1, Item = blueprint});
                        loot.Items = lootItems.ToArray();
                    }
                }
            }
            foreach (var unitLoot in ResourcesLibrary.GetBlueprints<BlueprintUnitLoot>()) {
                if (tableNames.Contains(unitLoot.name)) {
                    tableCount--;
                    if (unitLoot is BlueprintSharedVendorTable vendor) {
                        if (firstTime) {
                            var vendorTable = Game.Instance.Player.SharedVendorTables.GetTable(vendor);
                            vendorTable.Add(blueprint.CreateEntity());
                        }
                    } else if (!unitLoot.ComponentsArray.Any(component => component is LootItemsPackFixed pack && pack.Item.Item == blueprint)) {
                        var lootItem = new LootItem();
                        Accessors.SetLootItemItem(lootItem, blueprint);
                        var lootComponent = ScriptableObject.CreateInstance<LootItemsPackFixed>();
                        Accessors.SetLootItemsPackFixedItem(lootComponent, lootItem);
                        blueprintPatcher.EnsureComponentNameUnique(lootComponent, unitLoot.ComponentsArray);
                        var components = unitLoot.ComponentsArray.ToList();
                        components.Add(lootComponent);
                        unitLoot.ComponentsArray = components.ToArray();
                    }
                }
            }
            if (tableCount > 0) {
                Harmony12.FileLog.Log($"!!! Failed to match all loot table names for {blueprint.Name}.  {tableCount} table names not found.");
            }
        }

        private static void UpgradeSave(Version version) {
            foreach (var lootItem in CustomLootItems) {
                var firstTime = (version == null || version.CompareTo(lootItem.AddInVersion) < 0);
                var item = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(lootItem.AssetGuid);
                if (item == null) {
                    Harmony12.FileLog.Log($"!!! Loot item not created: {lootItem.AssetGuid}");
                } else {
                    AddToLootTables(item, lootItem.LootTables, firstTime);
                }
            }
        }

        [Harmony12.HarmonyPatch(typeof(Player), "PostLoad")]
        // ReSharper disable once UnusedMember.Local
        private static class PlayerPostLoadPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                // Just finished loading a save
                ItemUpgradeProjects.Clear();
                ItemCreationProjects.Clear();

                var characterList = UIUtility.GetGroup(true);
                foreach (var character in characterList) {
                    // If the mod is disabled, this will clean up crafting timer "buff" from all casters.
                    var timer = GetCraftingTimerComponentForCaster(character.Descriptor, character.IsMainCharacter);
                    var bondedItemComponent = GetBondedItemComponentForCaster(character.Descriptor);

                    if (!modEnabled) {
                        continue;
                    }

                    if (timer != null) {
                        foreach (var project in timer.CraftingProjects) {
                            if (project.ItemBlueprint != null) {
                                // Migrate all projects using ItemBlueprint to use ResultItem
                                var craftingData = ItemCraftingData.First(data => data.Name == project.ItemType);
                                project.ResultItem = BuildItemEntity(project.ItemBlueprint, craftingData);
                                project.ItemBlueprint = null;
                            }

                            project.Crafter = character;
                            project.ResultItem.PostLoad();
                            if (project.UpgradeItem == null) {
                                ItemCreationProjects.Add(project);
                            } else {
                                ItemUpgradeProjects[project.UpgradeItem] = project;
                                project.UpgradeItem.PostLoad();
                            }
                        }

                        if (character.IsMainCharacter) {
                            UpgradeSave(string.IsNullOrEmpty(timer.Version) ? null : Version.Parse(timer.Version));
                            timer.Version = ModEntry.Version.ToString();
                        }
                    }

                    if (bondedItemComponent != null) {
                        bondedItemComponent.ownerItem?.PostLoad();
                        bondedItemComponent.everyoneElseItem?.PostLoad();
                    }

                    // Retroactively give character any crafting feats in their past progression data which they don't actually have
                    // (e.g. Alchemists getting Brew Potion)
                    foreach (var characterClass in character.Descriptor.Progression.Classes) {
                        foreach (var levelData in characterClass.CharacterClass.Progression.LevelEntries) {
                            if (levelData.Level <= characterClass.Level) {
                                foreach (var feature in levelData.Features.OfType<BlueprintFeature>()) {
                                    if (feature.AssetGuid.Contains("#CraftMagicItems(feat=") && !CharacterHasFeat(character, feature.AssetGuid)) {
                                        character.Descriptor.Progression.Features.AddFeature(feature);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [Harmony12.HarmonyPatch(typeof(Game), "OnAreaLoaded")]
        // ReSharper disable once UnusedMember.Local
        private static class GameOnAreaLoadedPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                if (CustomBlueprintBuilder.Downgrade) {
                    UIUtility.ShowMessageBox("Craft Magic Items is disabled.  All your custom enchanted items and crafting feats have been replaced with " +
                                             "vanilla versions.", DialogMessageBox.BoxType.Message, null);
                    CustomBlueprintBuilder.Reset();
                }
            }
        }


        // Reverse the explicit code to hide weapon enchantments on shields - sorry, Owlcat.
        [Harmony12.HarmonyPatch(typeof(UIUtilityItem), "GetQualities")]
        // ReSharper disable once UnusedMember.Local
        private static class UIUtilityItemGetQualitiesPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix(ItemEntity item) {
                if (item is ItemEntityShield shield && shield.IsIdentified) {
                    // It appears that shields are not properly identified when found.
                    shield.ArmorComponent.Identify();
                    shield.WeaponComponent?.Identify();
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
                    var weaponQualities = Accessors.CallUIUtilityItemGetQualities(shield.WeaponComponent);
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

        [Harmony12.HarmonyPatch(typeof(UIUtilityItem), "FillShieldEnchantments")]
        // ReSharper disable once UnusedMember.Local
        private static class UIUtilityItemFillShieldEnchantmentsPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(ItemEntityShield shield, ref string __result) {
                if (shield.IsIdentified && shield.WeaponComponent != null) {
                    __result = Accessors.CallUIUtilityItemFillWeaponQualities(new TooltipData(), shield.WeaponComponent, __result);
                }
            }
        }
    }
}