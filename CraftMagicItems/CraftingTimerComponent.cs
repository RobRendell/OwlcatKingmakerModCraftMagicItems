using System;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Items;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace CraftMagicItems {

    public class CraftingProjectData {
        [JsonProperty]
        public int Progress;

        [JsonProperty]
        public int TargetCost;

        [JsonProperty]
        public int CasterLevel;

        [JsonProperty]
        public BlueprintAbility[] Prerequisites;

        [JsonProperty]
        public BlueprintItem ItemBlueprint;

        [JsonProperty]
        public string ItemType;

        [JsonProperty]
        public string LastMessage;
        
        public CraftingProjectData(int targetCost, int casterLevel, BlueprintItem itemBlueprint, string itemType,
                BlueprintAbility[] prerequisites = null, string message = "") {
            TargetCost = targetCost;
            CasterLevel = casterLevel;
            ItemBlueprint = itemBlueprint;
            ItemType = itemType;
            Prerequisites = prerequisites;
            Progress = 0;
            LastMessage = message;
        }

        public void AddMessage(string message) {
            LastMessage = message;
        }
    }

    [AllowedOn(typeof(BlueprintBuff))]
    public class CraftingTimerComponent : BuffLogic {
        [JsonProperty]
        public List<CraftingProjectData> CraftingProjects;

        [JsonProperty]
        public TimeSpan LastUpdated;

        public CraftingTimerComponent() {
            CraftingProjects = new List<CraftingProjectData>();
            LastUpdated = Game.Instance.Player.GameTime;
        }

        public void AddProject(CraftingProjectData project) {
            if (!CraftingProjects.Any()) {
                LastUpdated = Game.Instance.Player.GameTime;
            }
            CraftingProjects.Add(project);
        }
    }


}
