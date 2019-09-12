using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace CraftMagicItems {
    public enum DataTypeEnum {
        SpellBased,
        RecipeBased
    }

    public enum SlotRestrictionEnum {
        ArmourExceptRobes,
        ArmourOnlyRobes
    }

    public interface ICraftingData {
        // ReSharper disable once UnusedMember.Global
        DataTypeEnum DataType { get; set; }
    }

    public class ItemCraftingData : ICraftingData {
        public DataTypeEnum DataType { get; set; }
        [JsonProperty] public string Name;
        [JsonProperty] public string NameId;
        [JsonProperty] public string ParentNameId;
        [JsonProperty] public string FeatGuid;
        [JsonProperty] public int MinimumCasterLevel;
        [JsonProperty] public bool PrerequisitesMandatory;
        [JsonProperty] public string[] NewItemBaseIDs;
    }

    public class SpellBasedItemCraftingData : ItemCraftingData {
        [JsonProperty] [JsonConverter(typeof(StringEnumConverter))]
        public UsableItemType UsableItemType;

        [JsonProperty] public string NamePrefixId;
        [JsonProperty] public int MaxSpellLevel;
        [JsonProperty] public int BaseItemGoldCost;
        [JsonProperty] public int Charges;
    }

    public class RecipeBasedItemCraftingData : ItemCraftingData {
        [JsonProperty] public string[] RecipeFileNames;

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public ItemsFilter.ItemType[] Slots;

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public SlotRestrictionEnum[] SlotRestrictions;
        
        [JsonProperty] public int MundaneBaseDC;
        [JsonProperty] public bool MundaneEnhancementsStackable;

        // Loaded manually from RecipeFileName
        public RecipeData[] Recipes;

        // Built after load
        public Dictionary<string, List<RecipeData>> SubRecipes;
    }

    public enum RecipeCostType {
        LevelSquared,
        EnhancementLevelSquared,
        CasterLevel,
        Flat
    }

    public enum ItemRestrictions {
        WeaponMelee,
        WeaponRanged,
        WeaponBludgeoning,
        WeaponPiercing,
        WeaponSlashing,
        WeaponNotBludgeoning,
        WeaponNotPiercing,
        WeaponNotSlashing,
        WeaponFinessable,
        WeaponLight,
        WeaponNotLight,
        WeaponMetal,
        WeaponUseAmmunition,
        WeaponNotUseAmmunition,
        ArmourMetal,
        ArmourNotMetal,
        ArmourLight,
        ArmourMedium,
        ArmourHeavy,
        EnhancmentBonus2,
        EnhancmentBonus3,
        EnhancmentBonus4,
        EnhancmentBonus5
    }

    public enum CrafterPrerequisiteType {
        AlignmentLawful,
        AlignmentGood,
        AlignmentChaotic,
        AlignmentEvil,
        FeatureChannelEnergy
    }

    public class RecipeData {
        [JsonProperty] public string Name;
        [JsonProperty] public string NameId;
        [JsonProperty] public string ParentNameId;
        [JsonProperty] public string BonusTypeId;
        [JsonProperty] public string BonusToId;
        [JsonProperty] public BlueprintItemEnchantment[] Enchantments;
        [JsonProperty] public BlueprintItem ResultItem;
        [JsonProperty] public bool EnchantmentsCumulative;
        [JsonProperty] public int CasterLevelStart;
        [JsonProperty] public int CasterLevelMultiplier;
        [JsonProperty] public int BonusMultiplier;
        [JsonProperty] public DiceType BonusDieSize;
        [JsonProperty] public int MundaneDC;
        [JsonProperty] public PhysicalDamageMaterial Material;
        [JsonProperty] public BlueprintAbility[] PrerequisiteSpells;

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public CrafterPrerequisiteType[] CrafterPrerequisites;

        [JsonProperty] public bool AnyPrerequisite;

        [JsonProperty] [JsonConverter(typeof(StringEnumConverter))]
        public RecipeCostType CostType;

        [JsonProperty] public int CostFactor;

        [JsonProperty] public int CostAdjustment;

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public ItemsFilter.ItemType[] OnlyForSlots;

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public ItemRestrictions[] Restrictions;

        [JsonProperty] public bool CanApplyToMundaneItem;

        [JsonProperty] public string[] VisualMappings;
    }

    public class CraftingTypeConverter : CustomCreationConverter<ICraftingData> {
        public override ICraftingData Create(Type objectType) {
            throw new NotImplementedException();
        }

        private ICraftingData Create(JObject jObject) {
            var typeString = (string) jObject.Property("DataType");
            if (!Enum.TryParse<DataTypeEnum>(typeString, out var type)) {
                throw new ApplicationException($"The ItemCraftingData type {typeString} is not supported!");
            }
            switch (type) {
                case DataTypeEnum.SpellBased:
                    return new SpellBasedItemCraftingData();
                case DataTypeEnum.RecipeBased:
                    return new RecipeBasedItemCraftingData();
                default:
                    throw new ApplicationException($"The ItemCraftingData type {typeString} is not supported!");
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            // Load JObject from stream 
            var jObject = JObject.Load(reader);
            // Create target object based on JObject 
            var target = Create(jObject);
            // Populate the object properties 
            serializer.Populate(jObject.CreateReader(), target);
            return target;
        }
    }

    public class CustomLootItem {
        [JsonProperty] public Version AddInVersion;

        [JsonProperty] public string AssetGuid;

        [JsonProperty] public string[] LootTables;
    }
}