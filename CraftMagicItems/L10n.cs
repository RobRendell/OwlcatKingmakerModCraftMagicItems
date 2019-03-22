using Kingmaker.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using Kingmaker;
using Newtonsoft.Json;

namespace CraftMagicItems {
    public class L10NData {
        [JsonProperty] public string Key;
        [JsonProperty] public string Value;
    }

    class L10n {
        private static readonly Dictionary<string, string> ModifiedL10NStrings = new Dictionary<string, string>();

        private static bool initialLoad;
        private static bool enabled = true;

        private static void LoadL10NStrings() {
            if (LocalizationManager.CurrentPack == null) {
                return;
            }
            initialLoad = true;
            var currentLocale = LocalizationManager.CurrentLocale.ToString();
            var fileName = $"{Main.ModEntry.Path}/L10n/Strings_{currentLocale}.json";
            if (!File.Exists(fileName)) {
                Main.ModEntry.Logger.Warning($"Localised text for current local \"{currentLocale}\" not found, falling back on enGB.");
                currentLocale = "enGB";
                fileName = $"{Main.ModEntry.Path}/L10n/Strings_{currentLocale}.json";
            }

            try {
                var allStrings = Main.ReadJsonFile<L10NData[]>(fileName);
                foreach (var data in allStrings) {
                    var value = data.Value;
                    if (LocalizationManager.CurrentPack.Strings.ContainsKey(data.Key)) {
                        var original = LocalizationManager.CurrentPack.Strings[data.Key];
                        ModifiedL10NStrings.Add(data.Key, original);
                        if (value[0] == '+') {
                            value = original + value.Substring(1);
                        }
                    }
                    LocalizationManager.CurrentPack.Strings[data.Key] = value;
                }
            } catch (Exception e) {
                Main.ModEntry.Logger.Warning($"Exception loading L10n data for locale {currentLocale}: {e}");
                throw;
            }
        }

        public static void SetEnabled(bool newEnabled) {
            if (LocalizationManager.CurrentPack != null && enabled != newEnabled) {
                enabled = newEnabled;
                foreach (var pair in ModifiedL10NStrings) {
                    var swap = ModifiedL10NStrings[pair.Key];
                    ModifiedL10NStrings[pair.Key] = LocalizationManager.CurrentPack.Strings[pair.Key];
                    LocalizationManager.CurrentPack.Strings[pair.Key] = swap;
                }
            }
        }

        [Harmony12.HarmonyPatch(typeof(LocalizationManager))]
        [Harmony12.HarmonyPatch("CurrentLocale", Harmony12.MethodType.Setter)]
        private static class LocalizationManagerCurrentLocaleSetterPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                LoadL10NStrings();
            }
        }

        [Harmony12.HarmonyPatch(typeof(MainMenu), "Start")]
        private static class MainMenuStartPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Prefix() {
                // Kingmaker Mod Loader doesn't appear to patch the game before LocalizationManager.CurrentLocale has been set.
                if (!initialLoad) {
                    LoadL10NStrings();
                }
            }
        }
    }
}