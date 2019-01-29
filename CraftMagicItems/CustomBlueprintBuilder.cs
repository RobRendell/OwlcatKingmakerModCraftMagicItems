using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints;
using Object = UnityEngine.Object;

namespace CraftMagicItems {
    public class CustomBlueprintBuilder {
        public static readonly int VanillaAssetIdLength = 32;

        private static CustomBlueprintBuilder instance;

        private bool enabled = true;

        private readonly Regex blueprintRegex;
        private readonly Func<BlueprintScriptableObject, Match, string> patchBlueprint;

        public List<string> CustomBlueprintIDs { get; } = new List<string>();

        public bool Downgrade { get; private set; }

        public bool Enabled {
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


        public void Reset() {
            Downgrade = false;
        }

        public CustomBlueprintBuilder(Regex blueprintRegex, Func<BlueprintScriptableObject, Match, string> patchBlueprint) {
            this.blueprintRegex = blueprintRegex;
            this.patchBlueprint = patchBlueprint;
            instance = this;
        }

        private BlueprintScriptableObject PatchBlueprint(string assetId, BlueprintScriptableObject blueprint) {
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
                ResourcesLibrary.LibraryObject.BlueprintsByAssetId?.Add(newAssetId, blueprint);
                ResourcesLibrary.LibraryObject.GetAllBlueprints().Add(blueprint);
                // Also record the custom GUID so we can clean it up if the mod is later disabled.
                CustomBlueprintIDs.Add(newAssetId);
            }

            return blueprint;
        }

        // This patch is generic, and makes custom blueprints fall back to their initial version.
        [Harmony12.HarmonyPatch(typeof(ResourcesLibrary), "TryGetBlueprint")]
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
        private static class ResourcesLibraryTryGetBlueprintModPatch {
            // ReSharper disable once UnusedMember.Local
            private static MethodBase TargetMethod() {
                // ResourcesLibrary.TryGetBlueprint has two definitions which only differ by return type :(
                var allMethods = typeof(ResourcesLibrary).GetMethods();
                return allMethods.Single(info => info.Name == "TryGetBlueprint" && info.ReturnType == typeof(BlueprintScriptableObject));
            }

            // ReSharper disable once UnusedMember.Local
            // ReSharper disable once InconsistentNaming
            private static void Postfix(string assetId, ref BlueprintScriptableObject __result) {
                if (instance != null && __result != null && assetId != __result.AssetGuid) {
                    __result = instance.PatchBlueprint(assetId, __result);
                }
            }
        }
    }
}