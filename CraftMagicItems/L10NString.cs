using Harmony12;
using Kingmaker.Localization;

namespace CraftMagicItems {
    public class L10NString: LocalizedString {
        public L10NString(string key) {
            Traverse.Create(this).Field("m_Key").SetValue(key);
        }
    }
}