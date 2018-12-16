using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace CraftMagicItems {
    public interface ICraftingData {
        // ReSharper disable once UnusedMember.Global
        string DataType { get; set; }
    }

    public class ItemCraftingData : ICraftingData {
        public string DataType { get; set; }
        [JsonProperty] public string Name;
        [JsonProperty] public string NameId;
        [JsonProperty] public string FeatGuid;
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
        [JsonProperty] public string RecipeFileName;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public ItemsFilter.ItemType[] Slots;

        // Loaded manually from RecipeFileName
        public RecipeData[] Recipes;
        // Built after load
        public Dictionary<string, List<RecipeData>> SubRecipes;
    }

    public enum RecipeCostType {
        Level,
        LevelSquared
    }
    
    public class RecipeData {
        [JsonProperty] public string Name;
        [JsonProperty] public string NameId;
        [JsonProperty] public string ParentNameId;
        [JsonProperty] public string BonusTypeId;
        [JsonProperty] public BlueprintItemEnchantment[] Enchantments;
        [JsonProperty] public int CasterLevelStart;
        [JsonProperty] public int CasterLevelMultiplier;
        [JsonProperty] public BlueprintAbility[] Prerequisites;
        [JsonProperty] [JsonConverter(typeof(StringEnumConverter))]
        public RecipeCostType CostType;
        [JsonProperty] public int CostFactor;
    }

    public class CraftingTypeConverter : CustomCreationConverter<ICraftingData> {
        public override ICraftingData Create(Type objectType) {
            throw new NotImplementedException();
        }

        private ICraftingData Create(JObject jObject) {
            var type = (string) jObject.Property("DataType");
            switch (type) {
                case "SpellBased":
                    return new SpellBasedItemCraftingData();
                case "RecipeBased":
                    return new RecipeBasedItemCraftingData();
                default:
                    throw new ApplicationException($"The ItemCraftingData type {type} is not supported!");
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
}