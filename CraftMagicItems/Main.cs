using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.Controllers.Rest;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.Kingdom;
using Kingmaker.ResourceLinks;
using Kingmaker.UI;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UI.Log;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using UnityEngine;
using UnityModManagerNet;
using Random = System.Random;

namespace CraftMagicItems {
    internal struct ItemCraftingData {
        public string Name;
        public string NameId;
        public UsableItemType Type;
        public int MaxSpellLevel;
        public int BaseItemGoldCost;
        public int Charges;
        public string FeatGuid;
        public bool PrerequisitesMandatory;
    }

    public class Settings: UnityModManager.ModSettings {

        public bool CraftingCostsNoGold;
        public bool IgnoreCraftingFeats;
        public bool CraftingTakesNoTime;
        public bool CraftAtFullSpeedWhileAdventuring;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    public class Main {
        private const string OldBlueprintPrefix = "#ScribeScroll";
        private const string BlueprintPrefix = "#CraftMagicItems";
        private static readonly Regex BlueprintRegex = new Regex($"({OldBlueprintPrefix}|{BlueprintPrefix})"
            + @"\(("
            + @"CL=(?<casterLevel>\d+)(?<spellLevelMatch>,SL=(?<spellLevel>\d+))?(?<spellIdMatch>,spellId=\((?<spellId>([^()]+|(?<Level>\()|(?<-Level>\)))+(?(Level)(?!)))\))?"
            + @"|feat=(?<feat>[a-z]+)"
            + @"|(?<timer>timer)"
            + @")\)");
        private const string TimerBlueprintGuid = "52e4be2ba79c8c94d907bdbaf23ec15f#CraftMagicItems(timer)";
        private const int CraftingProgressPerDay = 500;
        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        private static readonly ItemCraftingData[] ItemCraftingData = {
            new ItemCraftingData { Name = "Scroll", NameId = "92f72bcd-5598-4aef-b943-51910a0d519c", Type = UsableItemType.Scroll, MaxSpellLevel = 9, BaseItemGoldCost = 25, Charges = 1, FeatGuid = "f180e72e4a9cbaa4da8be9bc958132ef#CraftMagicItems(feat=scroll)", PrerequisitesMandatory = true },
            new ItemCraftingData { Name = "Potion", NameId = "c5b3aeb3-891c-4346-8bbf-28da41434011", Type = UsableItemType.Potion, MaxSpellLevel = 3, BaseItemGoldCost = 50, Charges = 1, FeatGuid = "2f5d1e705c7967546b72ad8218ccf99c#CraftMagicItems(feat=potion)", PrerequisitesMandatory = true },
            new ItemCraftingData { Name = "Wand", NameId = "017d4f8d-08bb-4859-bc56-2cc73aab0fb4", Type = UsableItemType.Wand, MaxSpellLevel = 4, BaseItemGoldCost = 750, Charges = 50, FeatGuid = "46fad72f54a33dc4692d3b62eca7bb78#CraftMagicItems(feat=wand)", PrerequisitesMandatory = true }
        };
        private static readonly FeatureGroup[] CraftingFeatGroups = { FeatureGroup.Feat, FeatureGroup.WizardFeat };
        private const string KnowledgeArcanaLocalizedId = "75941008-1ec4-4085-ab6d-17c18d15b662";
        
        private enum OpenSection {
            CraftsSection,
            ProjectsSection,
            FeatsSection,
            CheatsSection
        }

        public static UnityModManager.ModEntry ModEntry;

        private static bool modEnabled = true;
        private static Settings settings;
        private static OpenSection currentSection = OpenSection.CraftsSection;
        private static int selectedItemTypeIndex;
        private static int selectedSpellcasterIndex;
        private static int selectedSpellbookIndex;
        private static int selectedSpellLevelIndex;
        private static int selectedCasterLevel;
        private static bool selectedShowPreparedSpells;
        private static int selectedFeatToLearn;
        private static readonly CustomBlueprintBuilder CustomBlueprintBuilder = new CustomBlueprintBuilder(BlueprintRegex, ApplyBlueprintPatch);
        private static readonly Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> SpellIdToItem = new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();
        private static readonly Random RandomGenerator = new Random();

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
            return true;
        }

        private static void OnGui(UnityModManager.ModEntry modEntry) {
            if (!modEnabled) {
                RenderLabel("The mod is disabled.  Loading saved games with custom items and feats will cause them to revert to regular versions.");
                return;
            }

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

        private static CraftingTimerComponent GetCraftingTimerComponentForCaster(UnitDescriptor caster, bool create = false) {
            var timerBlueprint = (BlueprintBuff)ResourcesLibrary.TryGetBlueprint(TimerBlueprintGuid);
            var timer = (Buff)caster.GetFact(timerBlueprint);
            if (timer == null) {
                if (!create) {
                    return null;
                }
                caster.AddFact<Buff>(timerBlueprint);
                timer = (Buff)caster.GetFact(timerBlueprint);
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
            var itemTypes = ItemCraftingData.Where(data => settings.IgnoreCraftingFeats || CasterHasFeat(caster, data.FeatGuid)).ToArray();
            if (!itemTypes.Any()) {
                RenderLabel($"{caster.CharacterName} does not know any crafting feats.");
                return;
            }
            var itemTypeNames = itemTypes.Select(data => new L10NString(data.NameId).ToString()).ToArray();
            RenderSelection(ref selectedItemTypeIndex, "Item Type: ", itemTypeNames, 8);
            var craftingData = itemTypes[selectedItemTypeIndex];
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
                spellOptions = spellOptions.Concat(spellbook.GetMemorizedSpells(spellLevel).Where(slot => slot.Spell != null && slot.Available).Select(slot => slot.Spell));
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
                    selectedCasterLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel, GUILayout.Width(300)));
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
                            RenderCraftItemControl(caster, craftingData, spell, variant, spellLevel, selectedCasterLevel);
                        }
                    } else {
                        RenderCraftItemControl(caster, craftingData, spell, spell.Blueprint, spellLevel, selectedCasterLevel);
                    }
                }
            }

            RenderLabel($"Current Money: {Game.Instance.Player.Money}");
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
            bool firstItem = true;
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
                        var craftingData = ItemCraftingData.First(data => data.Name == project.ItemType);
                        BuildCostString(out var cost, craftingData, project.TargetCost, project.Prerequisites);
                        foreach (var prerequisiteSpell in project.Prerequisites) {
                            if (prerequisiteSpell.MaterialComponent.Item) {
                                var number = prerequisiteSpell.MaterialComponent.Count * craftingData.Charges;
                                Game.Instance.Player.Inventory.Add(prerequisiteSpell.MaterialComponent.Item, number);
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
            var missingFeats = ItemCraftingData.Where(data => !CasterHasFeat(caster, data.FeatGuid)).ToArray();
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
                if (!feature.Blueprint.HideInUI && feature.Blueprint.HasGroup(CraftingFeatGroups) && feature.SourceProgression != null) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Feat: {feature.Name}", GUILayout.ExpandWidth(false));
                    if (!Array.Exists(ItemCraftingData, data => data.FeatGuid == feature.Blueprint.AssetGuid)) {
                        if (GUILayout.Button($"<- {learnFeat.Name}", GUILayout.ExpandWidth(false))) {
                            foreach (AddFacts addFact in feature.SelectComponents((AddFacts addFacts) => true)) {
                                addFact.OnFactDeactivate();
                            }
                            caster.Descriptor.Progression.ReplaceFeature(feature.Blueprint, learnFeat);
                            caster.Descriptor.Progression.Features.RemoveFact(feature);
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
            bool remote = IsPlayerInCapital();
            UnitEntityData[] partySpellCasters = UIUtility.GetGroup(remote).Where(character => character.IsPlayerFaction
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
            if (index >= options.Length) {
                index = 0;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            index = GUILayout.SelectionGrid(index, options, xCount);
            GUILayout.EndHorizontal();
        }

        private static void RenderCheckbox(ref bool value, string label) {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"{(value ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>")} {label}", GUILayout.ExpandWidth(false))) {
                value = !value;
            }
            GUILayout.EndHorizontal();
        }

        private static void AddItemBlueprintForSpell(UsableItemType itemType, BlueprintItemEquipment item) {
            if (!SpellIdToItem.ContainsKey(itemType)) {
                SpellIdToItem.Add(itemType, new Dictionary<string, List<BlueprintItemEquipment>>());
            }
            if (!SpellIdToItem[itemType].ContainsKey(item.Ability.AssetGuid)) {
                SpellIdToItem[itemType][item.Ability.AssetGuid] = new List<BlueprintItemEquipment>();
            }
            SpellIdToItem[itemType][item.Ability.AssetGuid].Add(item);
        }

        private static List<BlueprintItemEquipment> FindItemBlueprintForSpell(BlueprintScriptableObject spell, UsableItemType itemType) {
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

        private static bool CasterHasFeat(UnitEntityData caster, string featGuid) {
            var feat = ResourcesLibrary.TryGetBlueprint(featGuid) as BlueprintFeature;
            foreach (var feature in caster.Descriptor.Progression.Features) {
                if (feature.Blueprint == feat) {
                    return true;
                }
            }
            return false;
        }

        private static string RandomBaseBlueprintId(ItemCraftingData selectedItemData) {
            string[] blueprintIds;
            switch (selectedItemData.Type) {
                case UsableItemType.Scroll:
                    blueprintIds = new [] {
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
                    blueprintIds = new [] {
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
            return blueprintIds[RandomGenerator.Next(blueprintIds.Length)];
        }

        private static void CraftItem(BlueprintItem blueprint, string typeName) {
            var craftingData = ItemCraftingData.First(data => data.Name == typeName);
            var item = blueprint.CreateEntity();
            item.IsIdentified = true; // Mark the item as identified.
            item.Charges = craftingData.Charges; // Set the charges, since wand blueprints have random values.
            Game.Instance.Player.Inventory.Add(item);
            switch (craftingData.Type) {
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
        }

        private static int CalculateGoldCost(ItemCraftingData craftingData, int spellLevel, int casterLevel) {
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
                        var count = spellBlueprint.MaterialComponent.Count * craftingData.Charges;
                        if (itemTotals.ContainsKey(spellBlueprint.MaterialComponent.Item)) {
                            itemTotals[spellBlueprint.MaterialComponent.Item] += count;
                        } else {
                            itemTotals[spellBlueprint.MaterialComponent.Item] = count;
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

        private static void RenderCraftItemControl(UnitEntityData caster, ItemCraftingData craftingData, AbilityData spell, BlueprintAbility spellBlueprint, int spellLevel, int casterLevel) {
            var itemBlueprintList = FindItemBlueprintForSpell(spellBlueprint, craftingData.Type);
            if (itemBlueprintList == null && craftingData.Type == UsableItemType.Potion) {
                GUILayout.Label(string.Format(new L10NString("craftMagicItems-label-no-item-exists"), new L10NString(craftingData.NameId),
                    spellBlueprint.Name));
                return;
            }
            var existingItemBlueprint = itemBlueprintList?.Find(bp => bp.SpellLevel == spellLevel && bp.CasterLevel == casterLevel);
            var goldCost = CalculateGoldCost(craftingData, spellLevel, casterLevel);
            var canAfford = BuildCostString(out var cost, craftingData, goldCost, spellBlueprint);
            var custom = (existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength)
                ? new L10NString("craftMagicItems-label-custom").ToString() : "";
            var label = string.Format(new L10NString("craftMagicItems-label-craft-item"), custom, new L10NString(craftingData.NameId),
                spellBlueprint.Name, cost);
            if (!canAfford) {
                GUILayout.Label(label);
            } else if (GUILayout.Button(label, GUILayout.ExpandWidth(false))) {
                BlueprintItem itemBlueprint;
                if (itemBlueprintList == null) {
                    // No items for that spell exist at all - create a custom blueprint with casterLevel, spellLevel and spellId
                    var blueprintId = BuildCustomItemGuid(RandomBaseBlueprintId(craftingData), casterLevel, spellLevel, spell.Blueprint.AssetGuid);
                    itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintId);
                } else if (existingItemBlueprint == null) {
                    // No item for this spell & caster level - create a custom blueprint with casterLevel and optionally SpellLevel
                    var blueprintId = BuildCustomItemGuid(itemBlueprintList[0].AssetGuid, casterLevel, itemBlueprintList[0].SpellLevel == spellLevel ? -1 : spellLevel);
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

        private static string BuildCustomItemGuid(string originalGuid, int casterLevel, int spellLevel = -1, string spellId = null) {
            if (originalGuid.Length > CustomBlueprintBuilder.VanillaAssetIdLength) {
                // Check if GUID is already customised by this mod
                var match = BlueprintRegex.Match(originalGuid);
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
            return $"{originalGuid}{BlueprintPrefix}(CL={casterLevel}{spellLevelString}{spellIdString})";
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
            switch (feat) {
                case "scroll":
                case "potion":
                case "wand":
                    Traverse.Create(blueprint).Field("m_DisplayName").SetValue(new L10NString($"craftMagicItems-feat-{feat}-displayName"));
                    Traverse.Create(blueprint).Field("m_Description").SetValue(new L10NString($"craftMagicItems-feat-{feat}-description"));
                    var icon = Image2Sprite.Create($"{ModEntry.Path}/Icons/craft-{feat}.png");
                    Traverse.Create(blueprint).Field("m_Icon").SetValue(icon);
                    blueprint.ComponentsArray = null;
                    break;
                default:
                    return null;
            }
            return BuildCustomFeatGuid(blueprint.AssetGuid, feat);
        }

        private static string ApplyItemBlueprintPatch(BlueprintItemEquipment blueprint, Match match) {
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
            return BuildCustomItemGuid(blueprint.AssetGuid, casterLevel, spellLevel, spellId);
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
            } else {
                result = ApplyItemBlueprintPatch((BlueprintItemEquipment)blueprint, match);
                // also store the new item blueprint in our spell-to-item lookup dictionary.
                var usable = blueprint as BlueprintItemEquipmentUsable;
                if (usable != null && FindItemBlueprintForSpell(usable.Ability, usable.Type) == null) {
                    AddItemBlueprintForSpell(usable.Type, usable);
                }
            }
            return result;
        }

        [HarmonyPatch(typeof(MainMenu), "Start")]
        private static class MainMenuStartPatch {
            private static ObjectIDGenerator idGenerator = new ObjectIDGenerator();

            private static void AddCraftingFeats(BlueprintProgression progression) {
                foreach (var levelEntry in progression.LevelEntries) {
                    foreach (var featureBase in levelEntry.Features) {
                        var selection = featureBase as BlueprintFeatureSelection;
                        if (selection != null && (CraftingFeatGroups.Contains(selection.Group) || CraftingFeatGroups.Contains(selection.Group2))) {
                            // Use ObjectIDGenerator to detect which shared lists we've added the feats to.
                            idGenerator.GetId(selection.AllFeatures, out var firstTime);
                            if (firstTime) {
                                foreach (ItemCraftingData data in ItemCraftingData) {
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

            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
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
            if (Game.Instance.UI.BattleLogManager) {
                var data = new LogDataManager.LogItemData(message, color ?? GameLogStrings.Instance.DefaultColor, tooltip, PrefixIcon.None);
                Game.Instance.UI.BattleLogManager.LogView.AddLogEntry(data);
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
                var craftingData = ItemCraftingData.First(data => data.Name == project.ItemType);
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
                        project.ItemBlueprint.Name, new L10NString(KnowledgeArcanaLocalizedId), skillCheck, dc));
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
                    CraftItem(project.ItemBlueprint, project.ItemType);
                    timer.CraftingProjects.Remove(project);
                } else {
                    project.AddMessage(string.Format(new L10NString("craftMagicItems-logMessage-made-progress"),
                        100 * daysCrafting * progressPerDay / project.TargetCost, project.ItemBlueprint.Name,
                        new L10NString(KnowledgeArcanaLocalizedId), skillCheck, dc));
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
    }
}
