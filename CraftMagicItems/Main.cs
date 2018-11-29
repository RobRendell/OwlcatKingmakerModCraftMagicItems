using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.UI;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityModManagerNet;

namespace CraftMagicItems {
    struct ItemCraftingData {
        public string Name;
        public UsableItemType Type;
        public int MaxSpellLevel;
        public int BaseItemGoldCost;
        public int Charges;
        public string FeatGUID;
    }

    public class Settings: UnityModManager.ModSettings {

        public bool CraftingCostsNoGold = false;
        public bool IgnoreCraftingFeats = false;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }

    public class Main {
        static readonly int vanillaAssetIdLength = 32;
        static readonly string oldBlueprintPrefix = "#ScribeScroll";
        static readonly string blueprintPrefix = "#CraftMagicItems";
        static readonly Regex blueprintRegex = new Regex($"({oldBlueprintPrefix}|{blueprintPrefix})"
            + @"\(("
            + @"CL=(?<casterLevel>\d+)(?<spellLevelMatch>,SL=(?<spellLevel>\d+))?(?<spellIdMatch>,spellId=\((?<spellId>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\))?"
            + @"|feat=(?<feat>[a-z]+)"
            + @")\)");

        static readonly ItemCraftingData[] itemCraftingData = new ItemCraftingData[]
        {
            new ItemCraftingData { Name = "Scroll", Type = UsableItemType.Scroll, MaxSpellLevel = 9, BaseItemGoldCost = 25, Charges = 1, FeatGUID = "f180e72e4a9cbaa4da8be9bc958132ef#CraftMagicItems(feat=scroll)" },
            new ItemCraftingData { Name = "Potion", Type = UsableItemType.Potion, MaxSpellLevel = 3, BaseItemGoldCost = 50, Charges = 1, FeatGUID = "2f5d1e705c7967546b72ad8218ccf99c#CraftMagicItems(feat=potion)" },
            new ItemCraftingData { Name = "Wand", Type = UsableItemType.Wand, MaxSpellLevel = 4, BaseItemGoldCost = 750, Charges = 50, FeatGUID = "46fad72f54a33dc4692d3b62eca7bb78#CraftMagicItems(feat=wand)" }
        };
        static readonly FeatureGroup[] craftingFeatGroups = new FeatureGroup[] { FeatureGroup.Feat, FeatureGroup.WizardFeat };
        enum OpenSection {
            CraftsSection,
            FeatsSection,
            CheatsSection
        };

        static public UnityModManager.ModEntry modEntry;

        static bool modEnabled = true;
        static Settings settings;
        static OpenSection currentSection = OpenSection.CraftsSection;
        static int selectedItemTypeIndex = 0;
        static int selectedSpellcasterIndex = 0;
        static int selectedSpellbookIndex = 0;
        static int selectedSpellLevelIndex = 0;
        static int selectedCasterLevel = 0;
        static int selectedConvertedIndex = 0;
        static int selectedFeatToLearn = 0;
        static Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> spellIdToItem = new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();
        static List<string> customBlueprintGUIDs = new List<string>();
        static System.Random random = new System.Random();

        static void Load(UnityModManager.ModEntry modEntry) {
            Main.modEntry = modEntry;
            settings = Settings.Load<Settings>(modEntry);
            try {
                HarmonyInstance.Create("kingmaker.scribescroll").PatchAll(Assembly.GetExecutingAssembly());
            } catch (Exception e) {
                modEntry.Logger.Error($"Exception while patching Kingmaker: {e}");
                throw e;
            }
            modEnabled = modEntry.Active;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry) {
            settings.Save(modEntry);
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled) {
            modEnabled = enabled;
            if (!modEnabled) {
                // Remove any custom blueprints from ResourcesLibrary.
                foreach (string assetId in customBlueprintGUIDs) {
                    BlueprintScriptableObject customBlueprint = ResourcesLibrary.LibraryObject.BlueprintsByAssetId[assetId];
                    if (customBlueprint != null) {
                        ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Remove(assetId);
                        ResourcesLibrary.LibraryObject.GetAllBlueprints().Remove(customBlueprint);
                    }
                }
                customBlueprintGUIDs.Clear();
            }
            return true;
        }

        static void OnGui(UnityModManager.ModEntry modEntry) {
            if (!modEnabled) {
                RenderLabel("The mod is disabled.  Loading saved games with custom items and feats will cause them to revert to regular versions.");
                return;
            }

            UnitEntityData mainCharacterValue = Game.Instance?.Player?.MainCharacter.Value;
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

            RenderLabel($"Number of custom Craft Magic Items blueprints loaded: {customBlueprintGUIDs.Count}");

            if (RenderToggleSection(ref currentSection, OpenSection.CraftsSection, "Crafting")) {
                RenderCraftSection();
            }
            if (RenderToggleSection(ref currentSection, OpenSection.FeatsSection, "Feat Reassignment")) {
                RenderFeatReassignmentSection();
            }
            if (RenderToggleSection(ref currentSection, OpenSection.CheatsSection, "Cheats")) {
                RenderCheatsSection();
            }
            GUILayout.EndVertical();
        }

        static bool RenderToggleSection(ref OpenSection current, OpenSection mySection, string label) {
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

        static void RenderCraftSection() {
            UnitEntityData caster = RenderCasterSelection();
            if (caster == null) {
                return;
            }

            string[] itemTypeNames = itemCraftingData.Where(data => settings.IgnoreCraftingFeats || CasterHasFeat(caster, data.FeatGUID)).Select(data => data.Name).ToArray();

            if (itemTypeNames.Count() == 0) {
                RenderLabel($"{caster.CharacterName} does not know any crafting feats.");
                return;
            }

            RenderSelection(ref selectedItemTypeIndex, "Item Type: ", itemTypeNames, 8);
            ItemCraftingData craftingData = itemCraftingData.Single(data => data.Name == itemTypeNames[selectedItemTypeIndex]);

            List<Spellbook> spellbooks = caster.Descriptor.Spellbooks.Where(book => book.CasterLevel > 0).ToList();
            if (spellbooks.Count == 0) {
                RenderLabel($"{caster.CharacterName} is not yet able to cast spells.");
            } else if (spellbooks.Count == 1) {
                selectedSpellbookIndex = 0;
            } else {
                string[] spellbookNames = spellbooks.Select(book => book.Blueprint.Name.ToString()).ToArray();
                RenderSelection(ref selectedSpellbookIndex, "Class: ", spellbookNames, 10);
            }

            Spellbook spellbook = spellbooks[selectedSpellbookIndex];
            int maxLevel = Math.Min(spellbook.MaxSpellLevel, craftingData.MaxSpellLevel);
            string[] spellLevelNames = Enumerable.Range(0, maxLevel + 1).Select(index => $"Level {index}").ToArray();
            RenderSelection(ref selectedSpellLevelIndex, "Select spell level: ", spellLevelNames, 10);
            int spellLevel = selectedSpellLevelIndex;
            IEnumerable<AbilityData> spellOptions = null;
            if (spellLevel == 0) {
                // Cantrips/Orisons are special.
                spellOptions = spellbook.GetKnownSpells(spellLevel);
            } else if (spellbook.Blueprint.Spontaneous) {
                // Spontaneous spellcaster
                if (spellbook.GetSpontaneousSlots(spellLevel) > 0) {
                    RenderLabel($"{caster.CharacterName} can cast {spellbook.GetSpontaneousSlots(spellLevel)} more level {spellLevel} spells today.");
                    spellOptions = spellbook.GetKnownSpells(spellLevel);
                }
            } else {
                // Prepared spellcaster
                spellOptions = spellbook.GetMemorizedSpells(spellLevel).Where(slot => slot.Spell != null && slot.Available).Select(slot => slot.Spell);
            }
            if (spellOptions == null || !spellOptions.Any()) {
                RenderLabel($"{caster.CharacterName} cannot currently cast any level {spellLevel} spells.");
            } else {
                int minCasterLevel = Math.Max(1, 2 * spellLevel - 1);
                if (minCasterLevel < spellbook.CasterLevel) {
                    selectedCasterLevel = Mathf.RoundToInt(Mathf.Clamp(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Caster level: ", GUILayout.ExpandWidth(false));
                    selectedCasterLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel, GUILayout.Width(300)));
                    GUILayout.Label($"{selectedCasterLevel}", GUILayout.ExpandWidth(false));
                    GUILayout.EndHorizontal();
                } else {
                    selectedCasterLevel = minCasterLevel;
                    RenderLabel($"Caster level: {selectedCasterLevel}");
                }
                BlueprintAbility converted = null;
                IEnumerable<BlueprintAbility> convertedSpells = spellbook.GetSpontaneousConversionSpells(spellLevel);
                if (convertedSpells.Any()) {
                    List<string> convertOptions = new List<string>() { "Prepared spell" };
                    convertOptions.AddRange(convertedSpells.Select(blueprint => blueprint.Name));
                    RenderSelection(ref selectedConvertedIndex, "Use:", convertOptions.ToArray(), 5);
                    if (selectedConvertedIndex > 0) {
                        converted = convertedSpells.First(blueprint => blueprint.Name == convertOptions[selectedConvertedIndex]);
                    }
                }
                foreach (AbilityData spell in spellOptions.OrderBy(spell => spell.Name).Distinct()) {
                    if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty) {
                        GUILayout.Label($"Cannot craft {craftingData.Name} of {spell.Name} with metamagic applied.");
                    } else if (converted != null) {
                        RenderCraftItemControl(craftingData, spellbook, spell, converted, spellLevel, selectedCasterLevel);
                    } else if (spell.Blueprint.HasVariants) {
                        // Spells with choices (e.g. Protection from Alignment, which can be Protection from Evil, Good, Chaos or Law)
                        foreach (BlueprintAbility variant in spell.Blueprint.Variants) {
                            RenderCraftItemControl(craftingData, spellbook, spell, variant, spellLevel, selectedCasterLevel);
                        }
                    } else {
                        RenderCraftItemControl(craftingData, spellbook, spell, spell.Blueprint, spellLevel, selectedCasterLevel);
                    }
                }
            }

            RenderLabel($"Current Money: {Game.Instance.Player.Money}");
        }

        static void RenderFeatReassignmentSection() {
            UnitEntityData caster = RenderCasterSelection();
            if (caster == null) {
                return;
            }
            string[] featNames = itemCraftingData.Where(data => !CasterHasFeat(caster, data.FeatGUID)).Select(data => data.Name).ToArray();
            if (featNames.Length == 0) {
                RenderLabel($"{caster.CharacterName} already knows all crafting feats.");
                return;
            }
            RenderLabel("Use this section to reassign previous feat choices for this character to crafting feats.  <color=red>Warning:</color> This is a one-way assignment!");
            RenderSelection(ref selectedFeatToLearn, "Crafting Feat to learn", featNames, 8);
            ItemCraftingData learnFeatData = itemCraftingData.Single(data => data.Name == featNames[selectedFeatToLearn]);
            BlueprintFeature learnFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(learnFeatData.FeatGUID);
            int removedFeatIndex = 0;
            foreach (Feature feature in caster.Descriptor.Progression.Features) {
                if (!feature.Blueprint.HideInUI && feature.Blueprint.HasGroup(craftingFeatGroups) && feature.SourceProgression != null) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Feat: {feature.Name}", GUILayout.ExpandWidth(false));
                    if (!Array.Exists(itemCraftingData, data => data.FeatGUID == feature.Blueprint.AssetGuid)) {
                        if (GUILayout.Button($"<- {learnFeat.Name}", GUILayout.ExpandWidth(false))) {
                            foreach (AddFacts addFact in feature.SelectComponents((AddFacts addFacts) => true)) {
                                addFact.OnFactDeactivate();
                            }
                            caster.Descriptor.Progression.ReplaceFeature(feature.Blueprint, learnFeat);
                            caster.Descriptor.Progression.Features.RemoveFact(feature);
                            Feature addedFeature = caster.Descriptor.Progression.Features.AddFeature(learnFeat, null);
                            addedFeature.Source = feature.Source;
                            List<Fact> m_Facts = Traverse.Create(caster.Descriptor.Progression.Features).Field("m_Facts").GetValue<List<Fact>>();
                            if (removedFeatIndex < m_Facts.Count) {
                                // Move the new feat to the place in the list originally occupied by the removed one.
                                m_Facts.Remove(addedFeature);
                                m_Facts.Insert(removedFeatIndex, addedFeature);
                            }
                            ActionBarManager.Instance.HandleAbilityRemoved(null);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                removedFeatIndex++;
            }
        }

        static void RenderCheatsSection() {
            RenderCheckbox(ref settings.CraftingCostsNoGold, "Crafting costs no gold and no material components.");
            RenderCheckbox(ref settings.IgnoreCraftingFeats, "Crafting does not require characters to take crafting feats.");
        }

        static void RenderLabel(string label) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.EndHorizontal();
        }

        static UnitEntityData RenderCasterSelection() {
            // Only allow remote companions if in an area marked as the "capital" (Oleg's in Act I, your actual capital after that)
            bool remote = Game.Instance.CurrentlyLoadedArea.IsCapital;
            UnitEntityData[] partySpellCasters = UIUtility.GetGroup(remote).Where(character => character.IsPlayerFaction
                                                      && !character.Descriptor.IsPet
                                                      && character.Descriptor.Spellbooks != null
                                                      && character.Descriptor.Spellbooks.Any()
                                                      && !character.Descriptor.State.IsFinallyDead)
                                                      .ToArray();
            if (partySpellCasters.Length == 0) {
                RenderLabel("No characters with spells available.");
                return null;
            }

            string[] partyNames = partySpellCasters.Select(entity => entity.CharacterName).ToArray();
            RenderSelection(ref selectedSpellcasterIndex, "Caster: ", partyNames, 8);
            return partySpellCasters[selectedSpellcasterIndex];
        }

        static void RenderSelection(ref int index, string label, string[] options, int xCount) {
            if (index >= options.Length) {
                index = 0;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            index = GUILayout.SelectionGrid(index, options, xCount);
            GUILayout.EndHorizontal();
        }

        static void RenderCheckbox(ref bool value, string label) {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"{(value ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>")} {label}", GUILayout.ExpandWidth(false))) {
                value = !value;
            }
            GUILayout.EndHorizontal();
        }

        static void AddItemBlueprintForSpell(BlueprintAbility spell, UsableItemType itemType, BlueprintItemEquipment item) {
            if (!spellIdToItem.ContainsKey(itemType)) {
                spellIdToItem.Add(itemType, new Dictionary<string, List<BlueprintItemEquipment>>());
            }
            if (!spellIdToItem[itemType].ContainsKey(item.Ability.AssetGuid)) {
                spellIdToItem[itemType][item.Ability.AssetGuid] = new List<BlueprintItemEquipment>();
            }
            spellIdToItem[itemType][item.Ability.AssetGuid].Add(item);
        }

        static List<BlueprintItemEquipment> FindItemBlueprintForSpell(BlueprintAbility spell, UsableItemType itemType) {
            if (!spellIdToItem.ContainsKey(itemType)) {
                BlueprintItemEquipmentUsable[] allUsableItems = Resources.FindObjectsOfTypeAll<BlueprintItemEquipmentUsable>();
                foreach (BlueprintItemEquipmentUsable item in allUsableItems) {
                    if (item.Type == itemType) {
                        AddItemBlueprintForSpell(spell, itemType, item);
                    }
                }
            }
            string spellId = spell.AssetGuid;
            return (spellIdToItem[itemType].ContainsKey(spellId)) ? spellIdToItem[itemType][spellId] : null;
        }

        static bool CasterHasFeat(UnitEntityData caster, string FeatGUID) {
            BlueprintFeature feat = ResourcesLibrary.TryGetBlueprint(FeatGUID) as BlueprintFeature;
            foreach (Feature feature in caster.Descriptor.Progression.Features) {
                if (feature.Blueprint == feat) {
                    return true;
                }
            }
            return false;
        }

        static string RandomBaseBlueprintId(ItemCraftingData selectedItemData) {
            string[] guids;
            switch (selectedItemData.Type) {
                case UsableItemType.Scroll:
                    guids = new string[] {
                        "17959707c7004bd4abad2983f8a4af66",
                        "be452dba5acdd9441841d2189e1ae55a",
                        "fbdd06f0414c3ef458eb4b2a8072e502",
                        "358ee9cb540a9af4e9bc76cc1af62e86",
                        "e5700c45eb88bdd40a324e10d3de4a07",
                        "33ea3e3e578d4db4c8e57632fca4c9ec",
                        "68d5aa212b7323e4e95e0fe731ea50cf"
                    };
                    break;
                case UsableItemType.Wand:
                    guids = new string[] {
                        "4bf15a56d9ade8f47bf93fda7aa84d8b",
                        "85a4ff725c5236b4f9e0adb17fb64e2b",
                        "ce90a6251242af745b3daa56f84a5fe5",
                        "a20d40dc97457f041a2c29bdb2e2efe8",
                        "229fcbd357a9d6b48bc63b79188cdbd6",
                        "021b4a12739c59541922e3857f3fb3a4",
                        "394d337603392eb4c817994c45877fc0",
                        "8cb627da1a91069428ce87d9a114cdd6",
                        "85a4ff725c5236b4f9e0adb17fb64e2b"
                    };
                    break;
                default:
                    return null;
            }
            return guids[random.Next(guids.Length)];
        }

        static void RenderCraftItemControl(ItemCraftingData craftingData, Spellbook spellbook, AbilityData spell, BlueprintAbility spellBlueprint, int spellLevel, int casterLevel) {
            List<BlueprintItemEquipment> itemBlueprintList = FindItemBlueprintForSpell(spellBlueprint, craftingData.Type);
            if (itemBlueprintList == null && craftingData.Type == UsableItemType.Potion) {
                GUILayout.Label($"There is no {craftingData.Name} of {spellBlueprint.Name}");
                return;
            }
            BlueprintItemEquipment existingItemBlueprint = (itemBlueprintList == null) ? null : itemBlueprintList.Find(bp => bp.SpellLevel == spellLevel && bp.CasterLevel == casterLevel);
            int goldCost = 0;
            string cost;
            bool canAfford = true;
            if (settings.CraftingCostsNoGold) {
                cost = "free (cheating)";
            } else {
                goldCost = craftingData.BaseItemGoldCost * Math.Max(1, spellLevel) * casterLevel / (spellLevel == 0 ? 8 : 4);
                canAfford = (Game.Instance.Player.Money >= goldCost);
                cost = $"{goldCost} gold{(canAfford ? "" : " (which you can't afford)")}";
                if (spell.RequireMaterialComponent) {
                    int count = spellBlueprint.MaterialComponent.Count * craftingData.Charges;
                    cost += $" and {count} {spellBlueprint.MaterialComponent.Item.Name}";
                    if (!Game.Instance.Player.Inventory.Contains(spellBlueprint.MaterialComponent.Item, count)) {
                        canAfford = false;
                        cost += " (which you don't have)";
                    }
                }
            }
            string custom = (itemBlueprintList == null || existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Length > vanillaAssetIdLength) ? "(custom) " : "";
            string casting = (spell.Blueprint != spellBlueprint) ? $" (by casting {spell.Name})" : "";
            string label = $"Craft {custom}{craftingData.Name} of {spellBlueprint.Name}{casting} for {cost}";
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                Game.Instance.Player.SpendMoney(goldCost);
                spell.SpendFromSpellbook();
                if (spell.RequireMaterialComponent && !settings.CraftingCostsNoGold) {
                    int count = spellBlueprint.MaterialComponent.Count * craftingData.Charges;
                    Game.Instance.Player.Inventory.Remove(spellBlueprint.MaterialComponent.Item, count);
                }
                string blueprintId = null;
                if (itemBlueprintList == null) {
                    // Create a custom blueprint with casterLevel, spellLevel and spellId
                    blueprintId = BuildCustomItemGuid(RandomBaseBlueprintId(craftingData), casterLevel, spellLevel, spell.Blueprint.AssetGuid);
                } else if (existingItemBlueprint == null) {
                    // Create a custom blueprint with casterLevel and optionally SpellLevel
                    blueprintId = BuildCustomItemGuid(itemBlueprintList[0].AssetGuid, casterLevel, itemBlueprintList[0].SpellLevel == spellLevel ? -1 : spellLevel);
                } else {
                    // Use an existing blueprint
                    blueprintId = existingItemBlueprint.AssetGuid;
                }
                BlueprintItemEquipment actualBlueprint = (BlueprintItemEquipment)ResourcesLibrary.TryGetBlueprint(blueprintId);
                ItemEntity item = ItemsEntityFactory.CreateEntity(actualBlueprint);
                item.IsIdentified = true; // Mark the item as identified.
                item.Charges = craftingData.Charges; // Set the charges, since wand blueprints have random values.
                Game.Instance.Player.Inventory.Add(item);
                if (existingItemBlueprint == null) {
                    AddItemBlueprintForSpell(spell.Blueprint, craftingData.Type, actualBlueprint);
                }
                switch (craftingData.Type) {
                    case UsableItemType.Scroll:
                        Game.Instance.UI.Common.UISound.Play(UISoundType.NewInformation);
                        break;
                    case UsableItemType.Potion:
                        Game.Instance.UI.Common.UISound.PlayItemSound(SlotAction.Take, item, false);
                        break;
                    case UsableItemType.Wand:
                        Game.Instance.UI.Common.UISound.Play(UISoundType.SettlementBuildStart);
                        break;
                }
            }
        }

        static string BuildCustomItemGuid(string originalGuid, int casterLevel, int spellLevel = -1, string spellId = null) {
            if (originalGuid.Length > vanillaAssetIdLength) {
                // Check if GUID is already customised by this mod
                Match match = blueprintRegex.Match(originalGuid);
                if (match.Success) {
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
            string spellLevelString = (spellLevel == -1 ? "" : $",SL={spellLevel}");
            string spellIdString = (spellId == null ? "" : $",spellId=({spellId})");
            return $"{originalGuid}{blueprintPrefix}(CL={casterLevel}{spellLevelString}{spellIdString})";
        }

        static string BuildCustomFeatGuid(string originalGuid, string feat) {
            return $"{originalGuid}{blueprintPrefix}(feat={feat})";
        }

        // This patch is generic, and makes custom blueprints fall back to their initial version.
        [HarmonyPatch]
        static class ResourcesLibrary_TryGetBlueprint_Fallback_Patch {
            static MethodBase TargetMethod() {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                MethodInfo[] allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            [HarmonyPriority(Priority.First)]
            static void Postfix(string assetId, ref BlueprintScriptableObject __result) {
                if (__result == null && assetId.Length > vanillaAssetIdLength) {
                    // Failed to load custom blueprint - return the original.
                    string originalGuid = assetId.Substring(0, vanillaAssetIdLength);
                    __result = ResourcesLibrary.TryGetBlueprint(originalGuid);
                }
            }
        }

        static T CloneObject<T>(T originalObject) {
            Type type = originalObject.GetType();
            T clone = (T)Activator.CreateInstance(type);
            for (; type != null && type != typeof(UnityEngine.Object); type = type.BaseType) {
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (FieldInfo field in fields) {
                    field.SetValue(clone, field.GetValue(originalObject));
                }
            }
            return clone;
        }

        static LocalizedString buildLocalizedString(string key) {
            LocalizedString result = new LocalizedString();
            Traverse.Create(result).Field("m_Key").SetValue(key);
            return result;

        }

        static string ApplyFeatBlueprintPatch(BlueprintFeature blueprint, Match match) {
            string feat = match.Groups["feat"].Value;
            switch (feat) {
                case "scroll":
                case "potion":
                case "wand":
                    Traverse.Create(blueprint).Field("m_DisplayName").SetValue(buildLocalizedString($"craftMagicItems-feat-{feat}-displayName"));
                    Traverse.Create(blueprint).Field("m_Description").SetValue(buildLocalizedString($"craftMagicItems-feat-{feat}-description"));
                    Sprite icon = Image2Sprite.Create($"{modEntry.Path}/Icons/craft-{feat}.png");
                    Traverse.Create(blueprint).Field("m_Icon").SetValue(icon);
                    blueprint.ComponentsArray = null;
                    break;
                default:
                    return null;
            }
            return BuildCustomFeatGuid(blueprint.AssetGuid, feat);
        }

        static string ApplyItemBlueprintPatch(BlueprintItemEquipment blueprint, Match match) {
            int casterLevel = int.Parse(match.Groups["casterLevel"].Value);
            blueprint.CasterLevel = casterLevel;
            int spellLevel = -1;
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
            if (blueprint.Ability?.LocalizedSavingThrow != null && blueprint.Ability.LocalizedSavingThrow.IsSet()) {
                blueprint.DC = 10 + spellLevel * 3 / 2;
            }
            Traverse.Create(blueprint).Field("m_Cost").SetValue(0); // Allow the game to auto-calculate the cost
            return BuildCustomItemGuid(blueprint.AssetGuid, casterLevel, spellLevel, spellId);
        }

        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again, or null if we couldn't change the
        // blueprint.
        static string ApplyBlueprintPatch(BlueprintScriptableObject blueprint, string assetId) {
            Match match = blueprintRegex.Match(assetId);
            if (match.Success) {
                if (match.Groups["feat"].Success) {
                    return ApplyFeatBlueprintPatch((BlueprintFeature)blueprint, match);
                } else {
                    return ApplyItemBlueprintPatch((BlueprintItemEquipment)blueprint, match);
                }
            } else {
                modEntry.Logger.Warning($"Failed to find expected substring in custom blueprint assetId ${assetId}");
                return null;
            }
        }

        static BlueprintScriptableObject PatchBlueprint(string assetId, BlueprintScriptableObject blueprint) {
            if (blueprint.AssetGuid.Length == vanillaAssetIdLength) {
                // We have the original blueprint - clone it so we can make modifications which won't affect the original.
                blueprint = CloneObject(blueprint);
            }
            // Patch the blueprint
            string newAssetId = ApplyBlueprintPatch(blueprint, assetId);
            if (newAssetId != null) {
                // Insert patched blueprint into ResourcesLibrary under the new GUID.
                Traverse.Create(blueprint).Field("m_AssetGuid").SetValue(newAssetId);
                ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Add(newAssetId, blueprint);
                ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(blueprint);
                // Also record the custom GUID so we can clean it up if the mod is later disabled.
                customBlueprintGUIDs.Add(newAssetId);
            }
            return blueprint;
        }

        [HarmonyPatch]
        static class ResourcesLibrary_TryGetBlueprint_Mod_Patch {
            static MethodBase TargetMethod() {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                MethodInfo[] allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            static void Postfix(string assetId, ref BlueprintScriptableObject __result) {
                if (modEnabled && __result != null && assetId != __result.AssetGuid && (assetId.Contains(blueprintPrefix) || assetId.Contains(oldBlueprintPrefix))) {
                    __result = PatchBlueprint(assetId, __result);
                }
            }
        }

        [HarmonyPatch(typeof(MainMenu), "Start")]
        static class MainMenu_Start_Patch {

            static ObjectIDGenerator idGenerator = new ObjectIDGenerator();

            static void AddCraftingFeats(BlueprintProgression progression) {
                foreach (LevelEntry levelEntry in progression.LevelEntries) {
                    foreach (BlueprintFeatureBase featureBase in levelEntry.Features) {
                        BlueprintFeatureSelection selection = featureBase as BlueprintFeatureSelection;
                        if (selection != null && (craftingFeatGroups.Contains(selection.Group) || craftingFeatGroups.Contains(selection.Group2))) {
                            bool firstTime;
                            // Use ObjectIDGenerator to detect which shared lists we've added the feats to.
                            idGenerator.GetId(selection.AllFeatures, out firstTime);
                            if (firstTime) {
                                foreach (ItemCraftingData data in itemCraftingData) {
                                    BlueprintFeature featBlueprint = ResourcesLibrary.TryGetBlueprint(data.FeatGUID) as BlueprintFeature;
                                    List<BlueprintFeature> list = selection.AllFeatures.ToList();
                                    list.Add(featBlueprint);
                                    selection.AllFeatures = list.ToArray();
                                    idGenerator.GetId(selection.AllFeatures, out firstTime);
                                }
                            }
                        }
                    }
                }
            }

            static void Postfix() {
                if (idGenerator != null) {
                    // Add crafting feats to general feat selection
                    AddCraftingFeats(Game.Instance.BlueprintRoot.Progression.FeatsProgression);
                    // ... and to relevant class feat selections.
                    foreach (BlueprintCharacterClass characterClass in Game.Instance.BlueprintRoot.Progression.CharacterClasses) {
                        AddCraftingFeats(characterClass.Progression);
                    }
                    idGenerator = null;
                }
            }
        }

        // Fix a bug in UI - ActionBarManager.Update does not refresh the Groups (spells/Actions/Belt)
        [HarmonyPatch(typeof(ActionBarManager), "Update")]
        static class ActionBarManager_Update_Patch {
            static void Prefix(ActionBarManager __instance) {
                bool m_NeedReset = Traverse.Create(__instance).Field("m_NeedReset").GetValue<bool>();
                if (m_NeedReset) {
                    UnitEntityData m_Selected = Traverse.Create(__instance).Field("m_Selected").GetValue<UnitEntityData>();
                    __instance.Group.Set(m_Selected);
                }
            }
        }

    }
}
