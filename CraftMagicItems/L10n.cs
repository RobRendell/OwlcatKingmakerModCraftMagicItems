using Harmony12;
using Kingmaker.Localization;
using System;
using System.Data;
using System.IO;
using Kingmaker;

namespace CraftMagicItems {
    class L10n {

        private static bool initialLoad;
        
        private static void LoadL10NStrings() {
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
                    LocalizationManager.CurrentPack?.Strings.Add(row["key"].ToString(), row["value"].ToString());
                }
            } catch (Exception e) {
                Main.ModEntry.Logger.Warning($"Exception loading L10n data for locale {currentLocale}: {e}");
                throw;
            }
        }

        [HarmonyPatch(typeof(LocalizationManager))]
        [HarmonyPatch("CurrentLocale", MethodType.Setter)]
        private static class LocalizationManagerCurrentLocaleSetterPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix() {
                LoadL10NStrings();
            }
        }

        [HarmonyPatch(typeof(MainMenu), "Start")]
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
