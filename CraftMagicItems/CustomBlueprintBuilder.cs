using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints;
using Object = UnityEngine.Object;

namespace CraftMagicItems {
    public static class CustomBlueprintBuilder {
        public const int VanillaAssetIdLength = 32;

        private static bool enabled;
        private static Regex blueprintRegex;
        private static Func<BlueprintScriptableObject, Match, string> patchBlueprint;
        private static string[] substitutions;

        public static List<string> CustomBlueprintIDs { get; } = new List<string>();

        public static bool Downgrade { get; private set; }

        public static void InitialiseBlueprintRegex(Regex initBlueprintRegex) {
            // This needs to happen as early as possible to allow graceful downgrading when the mod startup fails.
            blueprintRegex = initBlueprintRegex;
        }

        public static void Initialise(Func<BlueprintScriptableObject, Match, string> initPatchBlueprint, bool initEnabled, params string[] initSubstitutions) {
            patchBlueprint = initPatchBlueprint;
            enabled = initEnabled;
            substitutions = initSubstitutions;
        }

        public static bool Enabled {
            set {
                enabled = value;
                if (enabled) {
                    Downgrade = false;
                } else {
                    // If we disable custom blueprints, remove any we've created from the ResourcesLibrary.
                    foreach (var assetId in CustomBlueprintIDs) {
                        var customBlueprint = ResourcesLibrary.LibraryObject.BlueprintsByAssetId?[assetId];
                        if (customBlueprint != null) {
                            ResourcesLibrary.LibraryObject.BlueprintsByAssetId.Remove(assetId);
                            ResourcesLibrary.LibraryObject.GetAllBlueprints().Remove(customBlueprint);
                        }
                    }

                    CustomBlueprintIDs.Clear();
                }
            }
        }

        public static void Reset() {
            Downgrade = false;
        }

        private static BlueprintScriptableObject PatchBlueprint(string assetId, BlueprintScriptableObject blueprint) {
            if (blueprintRegex == null) {
                // Catastrophic failure - assume we're downgrading.
                Downgrade = true;
                return blueprint;
            }

            var match = blueprintRegex.Match(assetId);
            if (!match.Success) {
                return blueprint;
            }

            if (!enabled) {
                Downgrade = true;
                return blueprint;
            }

            if (blueprint.AssetGuid.Length == VanillaAssetIdLength) {
                // We have the original blueprint - clone it so we can make modifications which won't affect the original.
                blueprint = Object.Instantiate(blueprint);
            }

            // Patch the blueprint
            var newAssetId = patchBlueprint(blueprint, match);
            if (newAssetId != null) {
                // Insert patched blueprint into ResourcesLibrary under the new GUID.
                Main.Accessors.SetBlueprintScriptableObjectAssetGuid(blueprint, newAssetId);
                if (ResourcesLibrary.LibraryObject.BlueprintsByAssetId != null) {
                    ResourcesLibrary.LibraryObject.BlueprintsByAssetId[newAssetId] = blueprint;
                }
                ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(blueprint);
                // Also record the custom GUID so we can clean it up if the mod is later disabled.
                CustomBlueprintIDs.Add(newAssetId);
            }

            return blueprint;
        }

        public static string AssetGuidWithoutMatch(string assetGuid, Match match = null) {
            if (match == null) {
                if (blueprintRegex == null) {
                    return assetGuid;
                }

                match = blueprintRegex.Match(assetGuid);
            }

            return !match.Success ? assetGuid : assetGuid.Substring(0, match.Index) + assetGuid.Substring(match.Index + match.Length);
        }

        // This patch is generic, and makes custom blueprints fall back to their initial version.
        [Harmony12.HarmonyPatch(typeof(ResourcesLibrary), "TryGetBlueprint")]
        // ReSharper disable once UnusedMember.Local
        private static class ResourcesLibraryTryGetBlueprintFallbackPatch {
            // ReSharper disable once UnusedMember.Local
            private static MethodBase TargetMethod() {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                var allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            // ReSharper disable once UnusedMember.Local
            [Harmony12.HarmonyPriority(Harmony12.Priority.First)]
            // ReSharper disable once InconsistentNaming
            private static void Postfix(string assetId, ref BlueprintScriptableObject __result) {
                if (__result == null && assetId.Length > VanillaAssetIdLength) {
                    // Failed to load custom blueprint - return the original.
                    var originalGuid = assetId.Substring(0, VanillaAssetIdLength);
                    __result = ResourcesLibrary.TryGetBlueprint(originalGuid);
                }
            }
        }

        [Harmony12.HarmonyPatch(typeof(ResourcesLibrary), "TryGetBlueprint")]
        // ReSharper disable once UnusedMember.Local
        private static class ResourcesLibraryTryGetBlueprintModPatch {
            // ReSharper disable once UnusedMember.Local
            private static MethodBase TargetMethod() {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                var allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            // ReSharper disable once UnusedMember.Local
            private static void Prefix(ref string assetId) {
                // Perform any backward compatibility substitutions
                for (var index = 0; index < substitutions.Length; index += 2) {
                    assetId = assetId.Replace(substitutions[index], substitutions[index + 1]);
                }
            }

            // ReSharper disable once UnusedMember.Local
            // ReSharper disable once InconsistentNaming
            private static void Postfix(string assetId, ref BlueprintScriptableObject __result) {
                if (__result != null && assetId != __result.AssetGuid) {
                    __result = PatchBlueprint(assetId, __result);
                }
            }
        }
    }
}