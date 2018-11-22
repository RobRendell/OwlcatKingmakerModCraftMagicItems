using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.Items;
using Kingmaker.UI;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityModManagerNet;

namespace CraftMagicItems {
    struct ItemTypeData {
        public UsableItemType Type;
        public int MaxSpellLevel;
        public int BaseItemGoldCost;
        public int Charges;
    }

    public class Main {
        static readonly int vanillaAssetIdLength = 32;
        static readonly string oldBlueprintPrefix = "#ScribeScroll";
        static readonly string blueprintPrefix = "#CraftMagicItems";
        static readonly Regex blueprintRegex = new Regex($"({oldBlueprintPrefix}|{blueprintPrefix})\\(CL=(\\d+)(,SL=(\\d+))(,spellId=\\((([^()]+|(?<Level>\\()|(?<-Level>\\)))+(?(Level)(?!)))\\))?\\)");
        static readonly string[] itemTypeNames = new string[] { "Scroll", "Potion", "Wand" };
        static readonly ItemTypeData[] itemTypeData = new ItemTypeData[]
        {
            new ItemTypeData { Type = UsableItemType.Scroll, MaxSpellLevel = 9, BaseItemGoldCost = 25, Charges = 1 },
            new ItemTypeData { Type = UsableItemType.Potion, MaxSpellLevel = 3, BaseItemGoldCost = 50, Charges = 1 },
            new ItemTypeData { Type = UsableItemType.Wand, MaxSpellLevel = 4, BaseItemGoldCost = 750, Charges = 50 }
        };

        static UnityModManager.ModEntry storedModEntry;

        static bool modEnabled = true;
        static int selectedItemTypeIndex = 0;
        static int selectedSpellcasterIndex = 0;
        static int selectedSpellbookIndex = 0;
        static int selectedSpellLevelIndex = 0;
        static int selectedCasterLevel = 0;
        static Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>> spellIdToItem = new Dictionary<UsableItemType, Dictionary<string, List<BlueprintItemEquipment>>>();
        static List<string> customBlueprintGUIDs = new List<string>();
        static System.Random random = new System.Random();

        static void Load(UnityModManager.ModEntry modEntry) {
            storedModEntry = modEntry;
            HarmonyInstance.Create("kingmaker.scribescroll").PatchAll(Assembly.GetExecutingAssembly());
            modEnabled = modEntry.Active;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
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
                GUILayout.BeginHorizontal();
                GUILayout.Label("The mod is disabled.  Loading saved games with custom items will cause them to revert to regular versions.");
                GUILayout.EndHorizontal();
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
                GUILayout.BeginHorizontal();
                GUILayout.Label("Item crafting is not available in this game state.");
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Number of custom Craft Magic Items blueprints loaded: {customBlueprintGUIDs.Count}");
            GUILayout.EndHorizontal();

            RenderSelection(ref selectedItemTypeIndex, "Item Type: ", itemTypeNames, 3);
            ItemTypeData selectedType = itemTypeData[selectedItemTypeIndex];

            // Only allow remote companions if in Oleg's (in Act I) or your capital (in Act II+)
            bool remote = Game.Instance.CurrentlyLoadedArea.IsCapital;
            List<UnitEntityData> partySpellCasters = (from entity in UIUtility.GetGroup(remote)
                                                      where entity.IsPlayerFaction
                                                      && !entity.Descriptor.IsPet
                                                      && entity.Descriptor.Spellbooks != null
                                                      && entity.Descriptor.Spellbooks.Any()
                                                      && !entity.Descriptor.State.IsFinallyDead
                                                      select entity).ToList<UnitEntityData>();
            if (partySpellCasters.Count == 0) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("No characters with spells available.");
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginVertical();
            string[] partyNames = (from entity in partySpellCasters select entity.CharacterName).ToArray<string>();
            RenderSelection(ref selectedSpellcasterIndex, "Caster: ", partyNames, 8);
            UnitEntityData caster = partySpellCasters[selectedSpellcasterIndex];
            List<Spellbook> spellbooks = (from book in caster.Descriptor.Spellbooks where book.CasterLevel > 0 select book).ToList<Spellbook>();
            if (spellbooks.Count == 0) {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{caster.CharacterName} is not yet able to cast spells.");
                GUILayout.EndHorizontal();
            } else if (spellbooks.Count == 1) {
                selectedSpellbookIndex = 0;
            } else {
                string[] spellbookNames = (from book in spellbooks select book.Blueprint.Name.ToString()).ToArray<string>();
                RenderSelection(ref selectedSpellbookIndex, "Class: ", spellbookNames, 10);
            }

            if (selectedSpellbookIndex < spellbooks.Count) {
                Spellbook spellbook = spellbooks[selectedSpellbookIndex];
                int maxLevel = Math.Min(spellbook.MaxSpellLevel, selectedType.MaxSpellLevel);
                string[] spellLevelNames = (from index in Enumerable.Range(0, maxLevel + 1) select $"Level {index}").ToArray<string>();
                RenderSelection(ref selectedSpellLevelIndex, "Select spell level: ", spellLevelNames, 10);
                int spellLevel = selectedSpellLevelIndex;
                IEnumerable<AbilityData> spellOptions = null;
                if (spellLevel == 0) {
                    // Cantrips/Orisons are special.
                    spellOptions = spellbook.GetKnownSpells(spellLevel);
                } else if (spellbook.Blueprint.Spontaneous) {
                    // Spontaneous spellcaster
                    if (spellbook.GetSpontaneousSlots(spellLevel) > 0) {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{caster.CharacterName} can cast {spellbook.GetSpontaneousSlots(spellLevel)} more level {spellLevel} spells today.");
                        GUILayout.EndHorizontal();
                        spellOptions = spellbook.GetKnownSpells(spellLevel);
                    }
                } else {
                    // Prepared spellcaster
                    spellOptions = (from slot in spellbook.GetMemorizedSpells(spellLevel) where slot.Spell != null && slot.Available select slot.Spell);

                }
                if (spellOptions == null || !spellOptions.Any()) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{caster.CharacterName} cannot currently cast any level {spellLevel} spells.");
                    GUILayout.EndHorizontal();
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
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"Caster level: {selectedCasterLevel}", GUILayout.ExpandWidth(false));
                        GUILayout.EndHorizontal();
                    }
                    foreach (AbilityData spell in spellOptions.OrderBy(spell => spell.Name).Distinct()) {
                        if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty) {
                            GUILayout.Label($"Cannot craft {itemTypeNames[selectedItemTypeIndex]} of {spell.Name} with metamagic applied.");
                        } else if (spell.Blueprint.HasVariants) {
                            // Spells with choices (e.g. Protection from Alignment, which can be Protection from Evil, Good, Chaos or Law)
                            foreach (BlueprintAbility variant in spell.Blueprint.Variants) {
                                RenderCraftItemControl(spellbook, spell, variant, spellLevel, selectedCasterLevel);
                            }
                        } else {
                            RenderCraftItemControl(spellbook, spell, spell.Blueprint, spellLevel, selectedCasterLevel);
                        }
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Current Money: {Game.Instance.Player.Money}");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
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

        static string RandomBaseBlueprintId(ItemTypeData selectedItemData) {
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

        static void RenderCraftItemControl(Spellbook spellbook, AbilityData spell, BlueprintAbility spellBlueprint, int spellLevel, int casterLevel) {
            ItemTypeData selectedItemData = itemTypeData[selectedItemTypeIndex];
            List<BlueprintItemEquipment> itemBlueprintList = FindItemBlueprintForSpell(spellBlueprint, selectedItemData.Type);
            if (itemBlueprintList == null && selectedItemData.Type == UsableItemType.Potion) {
                GUILayout.Label($"There is no {itemTypeNames[selectedItemTypeIndex]} of {spellBlueprint.Name}");
                return;
            }
            BlueprintItemEquipment existingItemBlueprint = (itemBlueprintList == null) ? null : itemBlueprintList.Find(bp => bp.SpellLevel == spellLevel && bp.CasterLevel == casterLevel);
            int goldCost = selectedItemData.BaseItemGoldCost * Math.Max(1, spellLevel) * casterLevel / (spellLevel == 0 ? 8 : 4);
            bool canAfford = (Game.Instance.Player.Money >= goldCost);
            string cost = $"{goldCost} gold{(canAfford ? "" : " (which you can't afford)")}";
            if (spell.RequireMaterialComponent) {
                int count = spellBlueprint.MaterialComponent.Count * selectedItemData.Charges;
                cost += $" and {count} {spellBlueprint.MaterialComponent.Item.Name}";
                if (!Game.Instance.Player.Inventory.Contains(spellBlueprint.MaterialComponent.Item, count)) {
                    canAfford = false;
                    cost += " (which you don't have)";
                }
            }
            string custom = (itemBlueprintList == null || existingItemBlueprint == null || existingItemBlueprint.AssetGuid.Length > vanillaAssetIdLength) ? "(custom) " : "";
            if (!canAfford) {
                GUILayout.Label($"Craft {custom}{itemTypeNames[selectedItemTypeIndex]} of {spellBlueprint.Name} for {cost}");
            } else if (GUILayout.Button($"Craft {custom}{itemTypeNames[selectedItemTypeIndex]} of {spellBlueprint.Name} for {cost}", GUILayout.ExpandWidth(false))) {
                Game.Instance.Player.SpendMoney(goldCost);
                spell.SpendFromSpellbook();
                if (spell.RequireMaterialComponent) {
                    int count = spellBlueprint.MaterialComponent.Count * selectedItemData.Charges;
                    Game.Instance.Player.Inventory.Remove(spellBlueprint.MaterialComponent.Item, count);
                }
                string blueprintId = null;
                if (itemBlueprintList == null) {
                    // Create a custom blueprint with casterLevel, spellLevel and spellId
                    blueprintId = BuildCustomItemGuid(RandomBaseBlueprintId(selectedItemData), casterLevel, spellLevel, spell.Blueprint.AssetGuid);
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
                item.Charges = selectedItemData.Charges; // Set the charges, since wand blueprints have random values.
                Game.Instance.Player.Inventory.Add(item);
                if (existingItemBlueprint == null) {
                    AddItemBlueprintForSpell(spell.Blueprint, selectedItemData.Type, actualBlueprint);
                }
                switch (selectedItemData.Type) {
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
                    if (spellLevel == -1 && match.Groups[3].Success) {
                        spellLevel = int.Parse(match.Groups[4].Value);
                    }
                    if (spellId == null && match.Groups[5].Success) {
                        spellId = match.Groups[6].Value;
                    }
                }
            }
            string spellLevelString = (spellLevel == -1 ? "" : $",SL={spellLevel}");
            string spellIdString = (spellId == null ? "" : $",spellId=({spellId})");
            return $"{originalGuid}{blueprintPrefix}(CL={casterLevel}{spellLevelString}{spellIdString})";
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


        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again, or null if we couldn't change the
        // blueprint.
        static string ApplyBlueprintPatch(BlueprintItemEquipment blueprint, string assetId) {
            Match match = blueprintRegex.Match(assetId);
            if (match.Success) {
                int casterLevel = int.Parse(match.Groups[2].Value);
                blueprint.CasterLevel = casterLevel;
                int spellLevel = -1;
                if (match.Groups[3].Success) {
                    spellLevel = int.Parse(match.Groups[4].Value);
                    blueprint.SpellLevel = spellLevel;
                }
                string spellId = null;
                if (match.Groups[5].Success) {
                    spellId = match.Groups[6].Value;
                    blueprint.Ability = (BlueprintAbility)ResourcesLibrary.TryGetBlueprint(spellId);
                    blueprint.DC = 0;
                }
                if (blueprint.Ability?.LocalizedSavingThrow != null && blueprint.Ability.LocalizedSavingThrow.IsSet()) {
                    blueprint.DC = 10 + spellLevel * 3 / 2;
                }
                Traverse.Create(blueprint).Field("m_Cost").SetValue(0); // Allow the game to auto-calculate the cost
                return BuildCustomItemGuid(blueprint.AssetGuid, casterLevel, spellLevel, spellId);
            } else {
                storedModEntry.Logger.Warning($"Failed to find expected substring in custom blueprint assetId ${assetId}");
                return null;
            }
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
                    if (__result.AssetGuid.Length == vanillaAssetIdLength) {
                        // We have the original blueprint - clone it so we can make modifications which won't affect the original.
                        __result = CloneObject(__result);
                    }
                    // Patch the blueprint
                    string newAssetId = ApplyBlueprintPatch((BlueprintItemEquipment)__result, assetId);
                    if (newAssetId != null) {
                        // Insert patched blueprint into ResourcesLibrary under the new GUID.
                        Traverse.Create(__result).Field("m_AssetGuid").SetValue(newAssetId);
                        ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Add(newAssetId, __result);
                        ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(__result);
                        // Also record the custom GUID so we can clean it up if the mod is later disabled.
                        customBlueprintGUIDs.Add(newAssetId);
                    }
                }
            }
        }
    }
}
