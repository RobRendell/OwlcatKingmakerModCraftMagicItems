using System.Text.RegularExpressions;
using Kingmaker.Localization;

namespace CraftMagicItems {
    public class L10NString : LocalizedString {
        private static readonly Regex StringModifier = new Regex(@"(?<key>[-0-9a-f]+)/((?<find>[^/]+)/(?<replace>[^/]*)/)+");

        public L10NString(string key) {
            if (LocalizationManager.CurrentPack != null && !LocalizationManager.CurrentPack.Strings.ContainsKey(key)) {
                var match = StringModifier.Match(key);
                if (match.Success) {
                    // If we're modifying an existing string, we need to insert it into the language bundle up front.
                    var result = new L10NString(match.Groups["key"].Value).ToString();
                    var count = match.Groups["find"].Captures.Count;
                    for (var index = 0; index < count; ++index) {
                        result = result.Replace(match.Groups["find"].Captures[index].Value, match.Groups["replace"].Captures[index].Value);
                    }
                    LocalizationManager.CurrentPack.Strings[key] = result;
                }
            }
            Harmony12.Traverse.Create(this).Field("m_Key").SetValue(key);
        }
    }

    public class FakeL10NString : LocalizedString {
        private readonly string fakeValue;

        public FakeL10NString(string fakeValue) {
            this.fakeValue = fakeValue;
            Harmony12.Traverse.Create(this).Field("m_Key").SetValue(fakeValue);
        }

        [Harmony12.HarmonyPatch(typeof(LocalizedString), "LoadString")]
        // ReSharper disable once UnusedMember.Local
        private static class LocalizedStringLoadStringPatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(LocalizedString __instance, ref string __result) {
                if (__instance is FakeL10NString fake) {
                    __result = fake.fakeValue;
                    return false;
                }

                return true;
            }
        }

        [Harmony12.HarmonyPatch(typeof(LocalizedString), "IsSet")]
        // ReSharper disable once UnusedMember.Local
        private static class LocalizedStringIsSetPatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(LocalizedString __instance, ref bool __result) {
                if (__instance is FakeL10NString fake) {
                    __result = !string.IsNullOrEmpty(fake.fakeValue);
                    return false;
                }

                return true;
            }
        }

        [Harmony12.HarmonyPatch(typeof(LocalizedString), "IsEmpty")]
        // ReSharper disable once UnusedMember.Local
        private static class LocalizedStringIsEmptyPatch {
            // ReSharper disable once UnusedMember.Local
            private static bool Prefix(LocalizedString __instance, ref bool __result) {
                if (__instance is FakeL10NString fake) {
                    __result = string.IsNullOrEmpty(fake.fakeValue);
                    return false;
                }

                return true;
            }
        }
    }
}