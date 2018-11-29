using Harmony12;
using Kingmaker.Localization;
using Newtonsoft.Json;
using System;
using System.Data;
using System.IO;

namespace CraftMagicItems {
    class L10n {
        static void LoadL10nStrings() {
            string currentLocale = LocalizationManager.CurrentLocale.ToString();
            if (!File.Exists($"{Main.modEntry.Path}/L10n/Strings_{currentLocale}.json")) {
                Main.modEntry.Logger.Warning($"Localised text for current local \"{currentLocale}\" not found, falling back on enGB.");
                currentLocale = "enGB";
            }
            try {
                using (StreamReader reader = new StreamReader($"{Main.modEntry.Path}/L10n/Strings_{currentLocale}.json")) {
                    string json = reader.ReadToEnd();
                    DataTable allStringPairs = JsonConvert.DeserializeObject<DataTable>(json);
                    foreach (DataRow row in allStringPairs.Rows) {
                        LocalizationManager.CurrentPack.Strings.Add(row["key"].ToString(), row["value"].ToString());
                    }
                }
            } catch (Exception e) {
                Main.modEntry.Logger.Warning($"Exception loading L10n data for locale {currentLocale}: {e}");
            }
        }

        [HarmonyPatch(typeof(LocalizationManager))]
        [HarmonyPatch("CurrentLocale", MethodType.Setter)]
        static class LocalizationManager_CurrentLocale_Setter_Patch {
            static void Postfix() {
                LoadL10nStrings();
            }
        }

    }
}
