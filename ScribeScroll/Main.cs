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
using UnityEngine;
using UnityModManagerNet;

namespace ScribeScroll
{
    public class Main
    {
        static readonly string BLUEPRINT_PREFIX = "#ScribeScroll";

        static bool modEnabled = true;
        static int selectedSpellcasterIndex = 0;
        static int selectedSpellbookIndex = 0;
        static int selectedSpellLevelIndex = 0;
        static int selectedCasterLevel = 0;
        static Dictionary<string, BlueprintItemEquipmentUsable> spellIdToScroll = null;
        static List<string> customBlueprintGUIDs = new List<string>();

        static void Load(UnityModManager.ModEntry modEntry)
        {
            HarmonyInstance.Create("kingmaker.scribescroll").PatchAll(Assembly.GetExecutingAssembly());
            modEnabled = modEntry.Active;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            modEnabled = enabled;
            if (!modEnabled)
            {
                // Remove any custom blueprints from ResourcesLibrary.
                foreach (string assetId in customBlueprintGUIDs)
                {
                    BlueprintScriptableObject customBlueprint = ResourcesLibrary.LibraryObject.BlueprintsByAssetId[assetId];
                    if (customBlueprint != null)
                    {
                        ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Remove(assetId);
                        ResourcesLibrary.LibraryObject.GetAllBlueprints().Remove(customBlueprint);
                    }
                }
                customBlueprintGUIDs.Clear();
            }
            return true;
        }

        static void OnGui(UnityModManager.ModEntry modEntry)
        {
            if (!modEnabled)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("The mod is disabled.  Loading saved games with custom scrolls will cause them to revert to regular scrolls.");
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
                ))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scroll scribing is not available in this game state.");
                GUILayout.EndHorizontal();
                return;
            }

            // Only allow remote companions if in Oleg's (in Act I) or your capital (in Act II+)
            bool remote = Game.Instance.CurrentlyLoadedArea.IsCapital;
            List<UnitEntityData> partySpellCasters = (from entity in UIUtility.GetGroup(remote)
                                                      where entity.IsPlayerFaction
                                                      && !entity.Descriptor.IsPet
                                                      && entity.Descriptor.Spellbooks != null
                                                      && entity.Descriptor.Spellbooks.Any()
                                                      && !entity.Descriptor.State.IsFinallyDead
                                                      select entity).ToList<UnitEntityData>();
            if (partySpellCasters.Count == 0)
            {
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
            if (spellbooks.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{caster.CharacterName} is not yet able to cast spells.");
                GUILayout.EndHorizontal();
            }
            else if (spellbooks.Count == 1)
            {
                selectedSpellbookIndex = 0;
            }
            else
            {
                string[] spellbookNames = (from book in spellbooks select book.Blueprint.Name.ToString()).ToArray<string>();
                RenderSelection(ref selectedSpellbookIndex, "Class: ", spellbookNames, 10);
            }

            if (selectedSpellbookIndex < spellbooks.Count)
            {
                Spellbook spellbook = spellbooks[selectedSpellbookIndex];
                string[] spellLevelNames = (from index in Enumerable.Range(1, spellbook.MaxSpellLevel) select $"Level {index}").ToArray<string>();
                RenderSelection(ref selectedSpellLevelIndex, "Select spell level: ", spellLevelNames, 10);
                int spellLevel = selectedSpellLevelIndex + 1;
                IEnumerable<AbilityData> spellOptions = null;
                if (spellbook.Blueprint.Spontaneous)
                {
                    // Spontaneous spellcaster
                    if (spellbook.GetSpontaneousSlots(spellLevel) > 0)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{caster.CharacterName} can cast {spellbook.GetSpontaneousSlots(spellLevel)} more level {spellLevel} spells today.");
                        GUILayout.EndHorizontal();
                        spellOptions = spellbook.GetKnownSpells(spellLevel);
                    }
                }
                else
                {
                    // Prepared spellcaster
                    spellOptions = (from slot in spellbook.GetMemorizedSpells(spellLevel) where slot.Spell != null && slot.Available select slot.Spell);

                }
                if (spellOptions == null || !spellOptions.Any())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{caster.CharacterName} cannot currently cast any level {spellLevel} spells.");
                    GUILayout.EndHorizontal();
                }
                else
                {
                    int minCasterLevel = 2 * spellLevel - 1;
                    if (minCasterLevel < spellbook.CasterLevel)
                    {
                        selectedCasterLevel = Mathf.RoundToInt(Mathf.Clamp(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel));
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Caster level: ", GUILayout.ExpandWidth(false));
                        selectedCasterLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(selectedCasterLevel, minCasterLevel, spellbook.CasterLevel, GUILayout.Width(300)));
                        GUILayout.Label($"{selectedCasterLevel}", GUILayout.ExpandWidth(false));
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        selectedCasterLevel = minCasterLevel;
                    }
                    foreach (AbilityData spell in spellOptions.OrderBy(spell => spell.Name).Distinct())
                    {
                        if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                        {
                            GUILayout.Label($"Cannot scribe scroll of {spell.Name} with metamagic applied.");
                        }
                        else if (spell.Blueprint.HasVariants)
                        {
                            // Spells with choices (e.g. Protection from Alignment, which can be Protection from Evil, Good, Chaos or Law)
                            foreach (BlueprintAbility variant in spell.Blueprint.Variants)
                            {
                                RenderScribeScrollControl(spellbook, spell, variant, spellLevel, selectedCasterLevel);
                            }
                        }
                        else
                        {
                            RenderScribeScrollControl(spellbook, spell, spell.Blueprint, spellLevel, selectedCasterLevel);
                        }
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Current Money: {Game.Instance.Player.Money}");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        static void RenderSelection(ref int index, string label, string[] options, int xCount)
        {
            if (index >= options.Length)
            {
                index = 0;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            index = GUILayout.SelectionGrid(index, options, xCount);
            GUILayout.EndHorizontal();
        }

        static BlueprintItemEquipmentUsable FindScrollForSpell(BlueprintAbility blueprint)
        {
            if (spellIdToScroll == null)
            {
                // Populate spellIdToScroll dict in a single initial pass
                spellIdToScroll = new Dictionary<string, BlueprintItemEquipmentUsable>();
                BlueprintItemEquipmentUsable[] allUsableItems = Resources.FindObjectsOfTypeAll<BlueprintItemEquipmentUsable>();
                foreach (BlueprintItemEquipmentUsable item in allUsableItems)
                {
                    if (item.Type == UsableItemType.Scroll)
                    {
                        spellIdToScroll[item.Ability.AssetGuid] = item;
                    }
                }
            }
            string spellId = blueprint.AssetGuid;
            if (spellIdToScroll.ContainsKey(spellId))
            {
                return spellIdToScroll[spellId];
            }
            else
            {
                return null;
            }
        }

        static void RenderScribeScrollControl(Spellbook spellbook, AbilityData spell, BlueprintAbility spellBlueprint, int spellLevel, int casterLevel)
        {
            BlueprintItemEquipmentUsable scroll = FindScrollForSpell(spellBlueprint);
            if (scroll == null)
            {
                GUILayout.Label($"There is no scroll of {spellBlueprint.Name}");
            }
            else
            {
                int goldCost = 25 * spellLevel * casterLevel / 4;
                bool canAfford = (Game.Instance.Player.Money >= goldCost);
                string cost = $"{goldCost} gold{(canAfford ? "" : " (which you can't afford)")}";
                if (spell.RequireMaterialComponent)
                {
                    cost += $" and {spellBlueprint.MaterialComponent.Count} {spellBlueprint.MaterialComponent.Item.Name}";
                    if (Game.Instance.Player.Inventory.Count(spellBlueprint.MaterialComponent.Item) < spellBlueprint.MaterialComponent.Count)
                    {
                        canAfford = false;
                        cost += " (which you don't have)";
                    }
                }
                if (!canAfford)
                {
                    GUILayout.Label($"Scribe scroll of {spellBlueprint.Name} for {cost}");
                }
                else if (GUILayout.Button($"Scribe scroll of {spellBlueprint.Name} for {cost}", GUILayout.ExpandWidth(false)))
                {
                    Game.Instance.Player.SpendMoney(goldCost);
                    spell.Spend(); // Also removes expensive material components
                    ItemEntity item;
                    if (casterLevel == scroll.CasterLevel)
                    {
                        // Create a scroll item using the default scroll blueprint
                        item = ItemsEntityFactory.CreateEntity(scroll);
                    }
                    else
                    {
                        // Create a scroll item using a custom scroll blueprint
                        string blueprintId = BuildCustomScrollGuid(scroll.AssetGuid, casterLevel);
                        BlueprintItem customBlueprint = (BlueprintItem)ResourcesLibrary.TryGetBlueprint(blueprintId);
                        item = ItemsEntityFactory.CreateEntity(customBlueprint);
                    }
                    item.IsIdentified = true; // Mark the scroll as identified
                    Game.Instance.Player.Inventory.Add(item);
                    Game.Instance.UI.Common.UISound.Play(UISoundType.NewInformation);
                }
            }
        }

        static string BuildCustomScrollGuid(string originalGuid, int casterLevel)
        {
            return $"{originalGuid}{BLUEPRINT_PREFIX}(CL={casterLevel})";
        }

        // This patch is generic, and makes custom blueprints fall back to their initial version.
        [HarmonyPatch]
        static class ResourcesLibrary_TryGetBlueprint_Fallback_Patch
        {
            static MethodBase TargetMethod()
            {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                MethodInfo[] allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            [HarmonyPriority(Priority.First)]
            static void Postfix(string assetId, ref BlueprintScriptableObject __result)
            {
                if (__result == null && assetId.Length > 32)
                {
                    // Failed to load custom blueprint - return the original.
                    string originalGuid = assetId.Substring(0, 32);
                    __result = ResourcesLibrary.TryGetBlueprint(originalGuid);
                }
            }
        }

        // Make our mod-specific updates to the blueprint based on the data stored in assetId.  Return a string which
        // is the AssetGuid of the supplied blueprint plus our customization again.
        static string ApplyBlueprintPatch(BlueprintScriptableObject blueprint, string assetId)
        {
            int pos = assetId.IndexOf(BLUEPRINT_PREFIX);
            int startPos = assetId.IndexOf("CL=", pos) + 3;
            for (pos = startPos; pos < assetId.Length && Char.IsDigit(assetId[pos]); pos++)
            {
            }
            int casterLevel = int.Parse(assetId.Substring(startPos, pos - startPos));
            BlueprintItemEquipment scroll = (BlueprintItemEquipment)blueprint;
            scroll.CasterLevel = casterLevel;
            Traverse.Create(blueprint).Field("m_Cost").SetValue(0); // Allow the game to auto-calculate the cost
            return BuildCustomScrollGuid(blueprint.AssetGuid, casterLevel);
        }

        [HarmonyPatch]
        static class ResourcesLibrary_TryGetBlueprint_Mod_Patch
        {
            static MethodBase TargetMethod()
            {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                MethodInfo[] allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            static void Postfix(string assetId, ref BlueprintScriptableObject __result)
            {
                if (modEnabled && assetId != __result.AssetGuid && assetId.Contains(BLUEPRINT_PREFIX))
                {
                    if (__result.AssetGuid.Length == 32)
                    {
                        // Loaded the original copy of the blueprint - clone it.
                        Type type = __result.GetType();
                        BlueprintScriptableObject clone = (BlueprintScriptableObject)Activator.CreateInstance(type);
                        for (; type != null && type != typeof(UnityEngine.Object); type = type.BaseType)
                        {
                            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            foreach (FieldInfo field in fields)
                            {
                                field.SetValue(clone, field.GetValue(__result));
                            }
                        }
                        __result = clone;
                    }
                    // Patch the blueprint, and insert into ResourcesLibrary under the new GUID.
                    string newAssetId = ApplyBlueprintPatch(__result, assetId);
                    Traverse.Create(__result).Field("m_AssetGuid").SetValue(newAssetId);
                    ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Add(newAssetId, __result);
                    ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(__result);
                    customBlueprintGUIDs.Add(newAssetId);
                }
            }
        }
    }
}
