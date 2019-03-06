using Kingmaker.Localization;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Kingmaker;
using Kingmaker.Designers.EventConditionActionSystem.Actions;

namespace CraftMagicItems {
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
                var allStringPairs = Main.ReadJsonFile<DataTable>(fileName);
                foreach (DataRow row in allStringPairs.Rows) {
                    var key = row["key"].ToString();
                    var value = row["value"].ToString();
                    if (LocalizationManager.CurrentPack.Strings.ContainsKey(key)) {
                        var original = LocalizationManager.CurrentPack.Strings[key];
                        ModifiedL10NStrings.Add(key, original);
                        if (value[0] == '+') {
                            value = original + value.Substring(1);
                        }
                    }
                    LocalizationManager.CurrentPack.Strings[key] = value;
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