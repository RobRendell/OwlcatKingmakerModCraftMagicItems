using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.Globalmap;
using Kingmaker.Items;
using Kingmaker.UI;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace ScribeScroll
{
    public class Main
    {
        static int selectedSpellcasterIndex = 0;
        static int selectedSpellbookIndex = 0;
        static int selectedSpellLevelIndex = 0;
        static int maxCasterLevelIndex = 0;
        static Dictionary<string, BlueprintItemEquipmentUsable> spellIdToScroll = null;

        static void Load(UnityModManager.ModEntry modEntry)
        {
            modEntry.OnGUI = OnGui;
        }

        static void OnGui(UnityModManager.ModEntry modEntry)
        {
            UnitEntityData mainCharacterValue = Game.Instance?.Player?.MainCharacter.Value;
            if (mainCharacterValue == null || !mainCharacterValue.IsViewActive || (
                Game.Instance.CurrentMode != GameModeType.Default
                && Game.Instance.CurrentMode != GameModeType.GlobalMap
                && Game.Instance.CurrentMode != GameModeType.FullScreenUi
                && Game.Instance.CurrentMode != GameModeType.Pause
                && Game.Instance.CurrentMode != GameModeType.Rest
                && Game.Instance.CurrentMode != GameModeType.Kingdom
                ))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scroll scribing is not available in this game state.");
                GUILayout.EndHorizontal();
            }
            else
            {
                // Only allow remote companions if in Oleg's (in Act I) or your capital (in Act II+)
                bool remote = Game.Instance.CurrentlyLoadedArea.IsCapital;
                List<UnitEntityData> partySpellCasters = (from entity in UIUtility.GetGroup(remote)
                                                          where entity.IsPlayerFaction
                                                          && !entity.Descriptor.IsPet
                                                          && entity.Descriptor.Spellbooks != null
                                                          && entity.Descriptor.Spellbooks.Any()
                                                          && !entity.Descriptor.State.IsFinallyDead
                                                          select entity).ToList<UnitEntityData>();
                GUILayout.BeginVertical();
                if (partySpellCasters.Count == 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("No characters with spells available.");
                    GUILayout.EndHorizontal();
                }
                else
                {
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
                        if (spellOptions != null && spellOptions.Any())
                        {
                            /*
                             * TODO not sure if it's possible to customise the CasterLevel without messing with the blueprint...
                            if (2 * spellLevel - 1 < spellbook.CasterLevel)
                            {
                                string[] casterLevelChoice = new string[2];
                                casterLevelChoice[0] = $"caster level {2 * spellLevel - 1}";
                                casterLevelChoice[1] = $"caster level {spellbook.CasterLevel}";
                                RenderSelection(ref maxCasterLevelIndex, "Scribe scrolls at: ", casterLevelChoice, 2);
                            }
                            */
                            int casterLevel = (maxCasterLevelIndex == 1) ? spellbook.CasterLevel : 2 * spellLevel - 1;

                            foreach (AbilityData spell in spellOptions.OrderBy(spell => spell.Name).Distinct())
                            {
                                if (spell.Blueprint.HasVariants)
                                {
                                    // Spells with choices (e.g. Protection from Alignment, which can be Protection from Evil, Good, Chaos or Law)
                                    foreach (BlueprintAbility variant in spell.Blueprint.Variants)
                                    {
                                        RenderScribeScrollControl(spellbook, spell, variant, spellLevel, casterLevel);
                                    }
                                }
                                else
                                {
                                    RenderScribeScrollControl(spellbook, spell, spell.Blueprint, spellLevel, casterLevel);
                                }
                            }
                        }
                        else
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"{caster.CharacterName} cannot currently cast any level {spellLevel} spells.");
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"Current Money: {Game.Instance.Player.Money}");
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
            }
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

        static void RenderSelection(ref int index, string label, string[] options, int xCount)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            index = GUILayout.SelectionGrid(index, options, xCount);
            GUILayout.EndHorizontal();
            if (index >= options.Length)
            {
                index = 0;
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
                if (spellBlueprint.MaterialComponent != null && spellBlueprint.MaterialComponent.Item != null)
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
                    if (spellBlueprint.MaterialComponent != null && spellBlueprint.MaterialComponent.Item != null)
                    {
                        Game.Instance.Player.Inventory.Remove(spellBlueprint.MaterialComponent.Item, spellBlueprint.MaterialComponent.Count);
                    }
                    Game.Instance.Player.Inventory.Add(scroll, 1);
                    spellbook.Spend(spell);
                    Game.Instance.UI.Common.UISound.Play(UISoundType.SubscribeItem);
                }
            }
        }
    }
}
