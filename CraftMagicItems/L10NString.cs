using System.Text.RegularExpressions;
using Harmony12;
using Kingmaker.Localization;

namespace CraftMagicItems {
    public class L10NString: LocalizedString {
        private static readonly Regex StringModifier = new Regex(@"([-0-9a-f]+)/((?<find>[^/]+)/(?<replace>[^/]*)/)+");

        private readonly string[] findStrings;
        private readonly string[] replaceStrings;
        
        public L10NString(string key) {
            var match = StringModifier.Match(key);
            if (match.Success) {
                key = match.Groups[1].Value;
                var count = match.Groups["find"].Captures.Count;
                findStrings = new string[count];
                replaceStrings = new string[count];
                for (var index = 0; index < count; ++index) {
                    findStrings[index] = match.Groups["find"].Captures[index].Value;
                    replaceStrings[index] = match.Groups["replace"].Captures[index].Value;
                }
            }
            Traverse.Create(this).Field("m_Key").SetValue(key);
        }

        [HarmonyPatch(typeof(LocalizedString), "LoadString")]
        private static class LocalizedStringLoadStringPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(LocalizedString __instance, ref string __result) {
                if (__instance is L10NString self && self.findStrings != null) {
                    for (var index = 0; index < self.findStrings.Length; ++index) {
                        __result = __result.Replace(self.findStrings[index], self.replaceStrings[index]);
                    }
                }
            }
        }
    }

    public class FakeL10NString: LocalizedString {
        private readonly string fakeValue;
        
        public FakeL10NString(string fakeValue) {
            this.fakeValue = fakeValue;
            Traverse.Create(this).Field("m_Key").SetValue(fakeValue);
        }
        
        [HarmonyPatch(typeof(LocalizedString), "LoadString")]
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

        [HarmonyPatch(typeof(LocalizedString), "IsSet")]
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

        [HarmonyPatch(typeof(LocalizedString), "IsEmpty")]
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