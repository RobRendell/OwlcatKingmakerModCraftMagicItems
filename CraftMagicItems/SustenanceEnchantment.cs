using System;
using System.Collections.Generic;
using System.Linq;
using Harmony12;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Controllers;
using Kingmaker.Controllers.Rest;
using Kingmaker.Controllers.Rest.State;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.ResourceLinks;
using Kingmaker.UI.RestCamp;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;

namespace CraftMagicItems {
    // A living character with a SustenanceFact does not need rations when they camp, and can perform two camp roles (not both guard shifts though).
    public class SustenanceFact : BlueprintBuff {
    }

    public class SustenanceEnchantment : BlueprintItemEnchantment {
        private static readonly SustenanceEnchantment BlueprintSustenanceEnchantment = CreateInstance<SustenanceEnchantment>();
        private const string SustenanceEnchantmentGuid = "8eb9d1c94b1e4894a88c228aa71b79e5#CraftMagicItems(sustenanceEnchantment)";
        private static readonly SustenanceFact BlueprintSustenanceFact = CreateInstance<SustenanceFact>();
        private const string SustenanceFactGuid = "235533b62159790499ced35860636bb2#CraftMagicItems(sustenanceFact)";

        private static bool initialised;

        [HarmonyPatch(typeof(MainMenu), "Start")]
        private static class MainMenuStartPatch {
            private static void AddBlueprint(string guid, BlueprintScriptableObject blueprint) {
                Traverse.Create(blueprint).Field("m_AssetGuid").SetValue(guid);
                ResourcesLibrary.LibraryObject.BlueprintsByAssetId?.Add(guid, blueprint);
                ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(blueprint);
            }

            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                if (!initialised) {
                    initialised = true;
                    AddBlueprint(SustenanceEnchantmentGuid, BlueprintSustenanceEnchantment);
                    AddBlueprint(SustenanceFactGuid, BlueprintSustenanceFact);
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_EnchantName")
                        .SetValue(new L10NString("craftMagicItems-enchantment-sustenance-name"));
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_Description")
                        .SetValue(new L10NString("craftMagicItems-enchantment-sustenance-description"));
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_Prefix").SetValue(new L10NString(""));
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_Suffix").SetValue(new L10NString(""));
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_EnchantmentCost").SetValue(1);
                    Traverse.Create(BlueprintSustenanceEnchantment).Field("m_IdentifyDC").SetValue(5);
                    var addSustenanceFact = CreateInstance<AddUnitFactEquipment>();
                    addSustenanceFact.Blueprint = BlueprintSustenanceFact;
                    addSustenanceFact.name = "AddUnitFactEquipment-SustenanceFact";
                    BlueprintSustenanceEnchantment.ComponentsArray = new BlueprintComponent[] {addSustenanceFact};
                    Traverse.Create(BlueprintSustenanceFact).Field("m_Flags").SetValue(2 + 8); // Enum is private... 2 = HiddenInUi, 8 = StayOnDeath
                    BlueprintSustenanceFact.Stacking = StackingType.Replace;
                    BlueprintSustenanceFact.Frequency = DurationRate.Rounds;
                    BlueprintSustenanceFact.FxOnStart = new PrefabLink();
                    BlueprintSustenanceFact.FxOnRemove = new PrefabLink();
                    Traverse.Create(BlueprintSustenanceFact).Field("m_DisplayName").SetValue(new L10NString("craftMagicItems-enchantment-sustenance-name"));
                    Traverse.Create(BlueprintSustenanceFact).Field("m_Description")
                        .SetValue(new L10NString("craftMagicItems-enchantment-sustenance-description"));
                }
            }
        }

        private static bool UnitHasSustenance(UnitEntityData unit) {
            return unit?.Descriptor.GetFact(BlueprintSustenanceFact) != null;
        }

        [HarmonyPatch(typeof(RestController), "CalculateNeededRations")]
        private static class RestControllerCalculateNeededRationsPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(ref int __result) {
                var sustenanceCount = Game.Instance.Player.Party.NotDead().Count(UnitHasSustenance);
                __result = Math.Max(0, __result - sustenanceCount);
            }
        }

        private static int CountRoles(UnitEntityData unit) {
            var roles = (Game.Instance.Player.Camping.Builders.Contains(unit) ? 1 : 0)
                        + (Game.Instance.Player.Camping.Hunters.Contains(unit) ? 1 : 0)
                        + (Game.Instance.Player.Camping.Cookers.Contains(unit) ? 1 : 0)
                        + (Game.Instance.Player.Camping.Special.Contains(unit) ? 1 : 0);
            foreach (var guardShift in Game.Instance.Player.Camping.Guards) {
                roles += guardShift.Contains(unit) ? 1 : 0;
            }

            return roles;
        }

        [HarmonyPatch(typeof(CampManager), "RemoveAllCompanionRoles")]
        private static class CampManagerRemoveAllCompanionRolesPatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(UnitEntityData unit) {
                if (UnitHasSustenance(unit)) {
                    // Only return true (and thus remove their roles) if they're already doing 2 roles.
                    return CountRoles(unit) >= 2;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(MemberUIBody), "CheckHasRole")]
        private static class MemberUiBodyCheckHasRolePatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(MemberUIBody __instance, ref bool __result) {
                var unit = __instance.CharacterSlot.Unit();
                if (UnitHasSustenance(unit) && CountRoles(unit) < 2) {
                    // The unit can still be assigned to another role.
                    __instance.HasRole = false;
                    Traverse.Create(__instance).Method("SetupRoleView").GetValue();
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        private static List<UnitReference> FindBestRoleToDrop(UnitEntityData unit, List<UnitReference> current, List<UnitReference> best) {
            return current.Contains(unit) && (best == null || current.Count > best.Count) ? current : best;
        }

        [HarmonyPatch(typeof(CampingState), "CleanupRoles")]
        private static class CampingStateCleanupRolesPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                // Ensure that anyone assigned to multiple roles actually has Sustenance
                foreach (var unit in Game.Instance.Player.Party) {
                    if (CountRoles(unit) > 1 && !UnitHasSustenance(unit)) {
                        // Need to drop one role - prefer roles that others are doing.
                        var roleToDrop = FindBestRoleToDrop(unit, Game.Instance.Player.Camping.Builders, null);
                        roleToDrop = FindBestRoleToDrop(unit, Game.Instance.Player.Camping.Hunters, roleToDrop);
                        roleToDrop = FindBestRoleToDrop(unit, Game.Instance.Player.Camping.Cookers, roleToDrop);
                        roleToDrop = FindBestRoleToDrop(unit, Game.Instance.Player.Camping.Special, roleToDrop);
                        foreach (var guardShift in Game.Instance.Player.Camping.Guards) {
                            roleToDrop = FindBestRoleToDrop(unit, guardShift, roleToDrop);
                        }

                        roleToDrop.Remove(unit);
                    }
                }
            }
        }

        // This seems to be a method from back when the game supported multiple roles.  We don't want characters using Sustenance to increase camp time.
        [HarmonyPatch(typeof(CampingState), "GetRolesExtraTime")]
        private static class CampingStateGetRolesExtraTimePatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(ref TimeSpan __result) {
                __result = 0.Hours();
                return false;
            }
        }
    }
}