using Harmony12;
using Kingmaker.Localization;
using Newtonsoft.Json;
using System;
using System.Data;
using System.IO;

namespace CraftMagicItems {
    class L10n {
        private static void LoadL10NStrings() {
            var currentLocale = LocalizationManager.CurrentLocale.ToString();
            if (!File.Exists($"{Main.ModEntry.Path}/L10n/Strings_{currentLocale}.json")) {
                Main.ModEntry.Logger.Warning($"Localised text for current local \"{currentLocale}\" not found, falling back on enGB.");
                currentLocale = "enGB";
            }
            try {
                using (var reader = new StreamReader($"{Main.ModEntry.Path}/L10n/Strings_{currentLocale}.json")) {
                    var json = reader.ReadToEnd();
                    var allStringPairs = JsonConvert.DeserializeObject<DataTable>(json);
                    foreach (DataRow row in allStringPairs.Rows) {
                        LocalizationManager.CurrentPack?.Strings.Add(row["key"].ToString(), row["value"].ToString());
                    }
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

    }
}
