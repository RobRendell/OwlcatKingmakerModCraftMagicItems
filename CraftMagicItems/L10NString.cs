using Harmony12;
using Kingmaker.Localization;

namespace CraftMagicItems {
    public class L10NString: LocalizedString {
        public L10NString(string key) {
            Traverse.Create(this).Field("m_Key").SetValue(key);
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