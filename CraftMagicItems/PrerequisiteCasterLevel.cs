using System.Linq;
using Kingmaker.Blueprints.Classes.Prerequisites;
using Kingmaker.Localization;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;

namespace CraftMagicItems {
    public class PrerequisiteCasterLevel : Prerequisite {
        private static readonly LocalizedString CasterLevelLocalized = new L10NString("dfb34498-61df-49b1-af18-0a84ce47fc98");

        private int minimumCasterLevel;

        public void SetPrerequisiteCasterLevel(int level) {
            minimumCasterLevel = level;
        }

        public override bool Check(FeatureSelectionState selectionState, UnitDescriptor unit, LevelUpState state) {
            return Main.ModSettings.IgnoreFeatCasterLevelRestriction || Main.CharacterCasterLevel(unit) >= minimumCasterLevel;
        }

        [Harmony12.HarmonyPatch(typeof(UIUtility), "GetPrerequisiteObject")]
        private static class UIUtilityGetPrerequisiteObjectPatch {
            // ReSharper disable once UnusedMember.Local
            private static void Postfix(Prerequisite prerequisite, ref string __result) {
                if (prerequisite is PrerequisiteCasterLevel prerequisiteCasterLevel) {
                    __result = $"{CasterLevelLocalized} {prerequisiteCasterLevel.minimumCasterLevel}";
                }
            }
        }
    }
}