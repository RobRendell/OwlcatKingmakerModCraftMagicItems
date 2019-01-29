This is the source for my mod to add crafting of magic items to Owlcat's Pathfinder: Kingmaker CRPG.

For details, see the mod page here: https://www.nexusmods.com/pathfinderkingmaker/mods/54

## To Do
* Positive Channeling (and presumably negative) are in the SRD: https://www.d20pfsrd.com/magic-items/wondrous-items/m-p/phylactery-of-positive-channeling/
* Double weapons
* Crafting skill bonus for favoured school, penalty for opposition school.
* Remove assumption that GUIDs are 32 characters long, given more mods with custom GUIDs are becoming available.
* In BuildCustomRecipeItemGuid, sort enchantments and remove GUIDs lexographically so blueprint is always the same.
* Add "Fabricate" custom spell to instantly do mundane crafting, and to add special materials to existing items.
* Make Robes fall under Craft Wondrous Items rather than Arms and Armor
* Ioun stone enchantments?
* Ghost Touch?
* Impact and Oversize are not stacking.
* Custom "quiver" items which can contain arrows of a particular type, which modify the damage of any ranged weapons you
        use when they're equipped in your belt slots?
* Dragonhide is called "dragonscale"

* Enchanting bonded objects.  Custom buff on caster, contains ref to original item and current item (apply renames to
        original item as well?)  If item is not equipped, switch current and original item in shared stash when the
        current character changes (ideally without printing to the battle log).  Also, if item is not equipped, apply
        concentration check to cast, or if I can't get that to work, RuleCastSpell has a general SpellFailureChance
        percentage which could do an equivalent effect.
* Mighty Fists enchantment is stacking - looks like a bug in Owlcat's code
* Tabletop Amulet of Mighty Fists actually allows all relevant weapon enhancements (such as Flaming or Agile)... not
        sure if the engine can even do that. 
* Add new magic items to random loot drops?  They don't seem to go very high in CR...
* Mundane crafting - craft shields with shield spikes?  Tricky, the base spiked shield blueprints are weapons...
* Craft Rod?
* In-game GUI
* Metamagic effects on spells
* Bracers of archery (lesser/greater) - requires both Craft Wondrous Items and Craft Arms and Armor
* Fortification enchantment - doesn't appear to be in the base game?
* Craft Construct?  Either a build-your-own-pet, Eidolon style, or a Golem-in-a-bottle like arrangement.

there are some dragonhide equipment entities, but there doesn't seem to be any blueprints associated with them?
EE_Armor_BreastplateDragonhideBlack_F    0e8cac2f5731bab47a491d68c64c687e    EE_Armor_BreastplateDragonhideBlack_F    EquipmentEntity    Object    2786126
EE_Armor_BreastplateDragonhideBlue_F    4fda804453f14c84abe5675088143264    EE_Armor_BreastplateDragonhideBlue_F    EquipmentEntity    Object    2789596
EE_Armor_BreastplateDragonhideRed_F    9cbd42ee72bd9e04ab500893c2869d09    EE_Armor_BreastplateDragonhideRed_F    EquipmentEntity    Object    2790462
EE_Armor_BreastplateDragonhideWhite_F    f668a7a4c2c7a5b459446aaed098e823    EE_Armor_BreastplateDragonhideWhite_F    EquipmentEntity    Object    2793650