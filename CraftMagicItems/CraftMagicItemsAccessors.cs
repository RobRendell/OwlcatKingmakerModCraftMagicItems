using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.Common;
using Kingmaker.UI.Tooltip;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace CraftMagicItems {
    /**
     * Spacehamster's idea: create reflection-based accessors up front, so the mod fails on startup if the Kingmaker code changes in an incompatible way.
     */
    public class CraftMagicItemsAccessors {
        public readonly FastGetter<Spellbook, List<BlueprintSpellList>> GetSpellbookSpecialLists =
            Accessors.CreateGetter<Spellbook, List<BlueprintSpellList>>("m_SpecialLists");

        public readonly FastGetter<FeatureCollection, List<Fact>> GetFeatureCollectionFacts =
            Accessors.CreateGetter<FeatureCollection, List<Fact>>("m_Facts");

        public readonly FastGetter<ActionBarManager, bool> GetActionBarManagerNeedReset = Accessors.CreateGetter<ActionBarManager, bool>("m_NeedReset");

        public readonly FastGetter<ActionBarManager, UnitEntityData> GetActionBarManagerSelected =
            Accessors.CreateGetter<ActionBarManager, UnitEntityData>("m_Selected");

        public readonly FastGetter<Spellbook, List<AbilityData>[]> GetSpellbookKnownSpells =
            Accessors.CreateGetter<Spellbook, List<AbilityData>[]>("m_KnownSpells");

        public readonly FastGetter<Spellbook, Dictionary<BlueprintAbility, int>> GetSpellbookKnownSpellLevels =
            Accessors.CreateGetter<Spellbook, Dictionary<BlueprintAbility, int>>("m_KnownSpellLevels");


        public readonly FastSetter<BlueprintUnitFact, LocalizedString> SetBlueprintUnitFactDisplayName =
            Accessors.CreateSetter<BlueprintUnitFact, LocalizedString>("m_DisplayName");

        public readonly FastSetter<BlueprintUnitFact, LocalizedString> SetBlueprintUnitFactDescription =
            Accessors.CreateSetter<BlueprintUnitFact, LocalizedString>("m_Description");

        public readonly FastSetter<BlueprintUnitFact, Sprite> SetBlueprintUnitFactIcon = Accessors.CreateSetter<BlueprintUnitFact, Sprite>("m_Icon");

        public readonly FastSetter<BlueprintItemEquipmentUsable, int> SetBlueprintItemEquipmentUsableCost =
            Accessors.CreateSetter<BlueprintItemEquipmentUsable, int>("m_Cost");

        public readonly FastSetter<BlueprintItem, List<BlueprintItemEnchantment>> SetBlueprintItemCachedEnchantments =
            Accessors.CreateSetter<BlueprintItem, List<BlueprintItemEnchantment>>("m_CachedEnchantments");

        public readonly FastSetter<BlueprintItemShield, BlueprintItemArmor> SetBlueprintItemShieldArmorComponent =
            Accessors.CreateSetter<BlueprintItemShield, BlueprintItemArmor>("m_ArmorComponent");

        public readonly FastSetter<BlueprintItemShield, BlueprintItemWeapon> SetBlueprintItemShieldWeaponComponent =
            Accessors.CreateSetter<BlueprintItemShield, BlueprintItemWeapon>("m_WeaponComponent");

        public readonly FastSetter<BlueprintItemWeapon, DamageTypeDescription> SetBlueprintItemWeaponDamageType =
            Accessors.CreateSetter<BlueprintItemWeapon, DamageTypeDescription>("m_DamageType");

        public readonly FastSetter<BlueprintItemWeapon, bool> SetBlueprintItemWeaponOverrideDamageType =
            Accessors.CreateSetter<BlueprintItemWeapon, bool>("m_OverrideDamageType");

        public readonly FastSetter<BlueprintItem, Sprite> SetBlueprintItemIcon = Accessors.CreateSetter<BlueprintItem, Sprite>("m_Icon");

        public readonly FastSetter<BlueprintItemEquipmentHand, WeaponVisualParameters> SetBlueprintItemEquipmentHandVisualParameters =
            Accessors.CreateSetter<BlueprintItemEquipmentHand, WeaponVisualParameters>("m_VisualParameters");

        public readonly FastSetter<BlueprintItemArmor, ArmorVisualParameters> SetBlueprintItemArmorVisualParameters =
            Accessors.CreateSetter<BlueprintItemArmor, ArmorVisualParameters>("m_VisualParameters");

        public readonly FastSetter<BlueprintBuff, int> SetBlueprintBuffFlags = Accessors.CreateSetter<BlueprintBuff, int>("m_Flags");

        public readonly FastSetter<BlueprintItem, LocalizedString> SetBlueprintItemDisplayNameText =
            Accessors.CreateSetter<BlueprintItem, LocalizedString>("m_DisplayNameText");

        public readonly FastSetter<BlueprintItem, LocalizedString> SetBlueprintItemDescriptionText =
            Accessors.CreateSetter<BlueprintItem, LocalizedString>("m_DescriptionText");

        public readonly FastSetter<BlueprintItem, LocalizedString> SetBlueprintItemFlavorText =
            Accessors.CreateSetter<BlueprintItem, LocalizedString>("m_FlavorText");

        public readonly FastSetter<BlueprintItem, int> SetBlueprintItemCost = Accessors.CreateSetter<BlueprintItem, int>("m_Cost");

        public readonly FastSetter<BlueprintItemEnchantment, LocalizedString> SetBlueprintItemEnchantmentEnchantName =
            Accessors.CreateSetter<BlueprintItemEnchantment, LocalizedString>("m_EnchantName");

        public readonly FastSetter<BlueprintItemEnchantment, LocalizedString> SetBlueprintItemEnchantmentDescription =
            Accessors.CreateSetter<BlueprintItemEnchantment, LocalizedString>("m_Description");

        public readonly FastSetter<BlueprintItemEnchantment, LocalizedString> SetBlueprintItemEnchantmentPrefix =
            Accessors.CreateSetter<BlueprintItemEnchantment, LocalizedString>("m_Prefix");

        public readonly FastSetter<BlueprintItemEnchantment, LocalizedString> SetBlueprintItemEnchantmentSuffix =
            Accessors.CreateSetter<BlueprintItemEnchantment, LocalizedString>("m_Suffix");

        public readonly FastSetter<BlueprintItemEnchantment, int> SetBlueprintItemEnchantmentEnchantmentCost =
            Accessors.CreateSetter<BlueprintItemEnchantment, int>("m_EnchantmentCost");

        public readonly FastSetter<BlueprintItemEnchantment, int> SetBlueprintItemEnchantmentEnchantmentIdentifyDC =
            Accessors.CreateSetter<BlueprintItemEnchantment, int>("m_IdentifyDC");

        public readonly FastSetter<BlueprintScriptableObject, string> SetBlueprintScriptableObjectAssetGuid =
            Accessors.CreateSetter<BlueprintScriptableObject, string>("m_AssetGuid");

        public readonly FastStaticInvoker<ItemEntityWeapon, string> CallUIUtilityItemGetQualities =
            Accessors.CreateStaticInvoker<ItemEntityWeapon, string>(typeof(UIUtilityItem), "GetQualities");

        public readonly FastStaticInvoker<TooltipData, ItemEntityWeapon, string, string> CallUIUtilityItemFillWeaponQualities =
            Accessors.CreateStaticInvoker<TooltipData, ItemEntityWeapon, string, string>(typeof(UIUtilityItem), "FillWeaponQualities");
    }
}