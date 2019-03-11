This is the source for my mod to add crafting of magic items to Owlcat's Pathfinder: Kingmaker CRPG.

For details, see the mod page here: https://www.nexusmods.com/pathfinderkingmaker/mods/54

## To Do

* Bug: Magus adding "Cast Flare Burst (CL 1) 1/day" to a Light Crossbow +1 resulted in a cost of 0.
* Make characters in town work from the "stash" (and their own equipment) rather than from the party inventory.
* On load, Fast Healing 1 effect from regeneration ring becomes Fast Healing 5... doesn't change GUID?

* Trailblazer's Helm can cast two different spells!
* Perhaps crafting projects for characters in your party should be advanced at the adventuring rate when you first enter
        the capital, to account things more accurately?
* Enchanting mundane belts, rings, amulets, necklaces?
* Request: Is it possible to add the option for bonus spell slots, for ring of wizardry.
* Perhaps put the "Mod disabled" text into Strings_enGB.json anyway, so it can be translated?  Load manually?
* Crafting using mithral ingots, silver ingots etc.  Consume like material components, reduce the required progress by
        say 150% of the sale price of the ingot (and can be done without having to go back to town to sell the ingot)
* Remove assumption that GUIDs are 32 characters long, given more mods with custom GUIDs are becoming available.
* In BuildCustomRecipeItemGuid, sort enchantments and remove GUIDs lexographically so blueprint is always the same.
* Add "Fabricate" custom spell to instantly do mundane crafting, and to add special materials to existing items.
* Make Robes fall under Craft Wondrous Items rather than Arms and Armor.  "Monk's Robe"
* Ioun stone enchantments?  Could also do as a specific item recipe, a la Bag of Holding.
* Custom "quiver" items which can contain arrows of a particular type, which modify the damage of any ranged weapons you
        use when they're equipped in your belt slots?
* Metamagic effects on spells
* Dragonhide is called "dragonscale".  Ironwood?
* Request for https://www.d20pfsrd.com/magic-items/wondrous-items/m-p/page-of-spell-knowledge/
* Wild enchantment, which allows a druid to retain an armor's bonus when they shapeshift.

* Mighty Fists enchantment is stacking - looks like a bug in Owlcat's code
* Tabletop Amulet of Mighty Fists actually allows all relevant weapon enhancements (such as Flaming or Agile)... not
        sure if the engine can even do that. 
* Mundane crafting - craft shields with shield spikes?  Tricky, the base spiked shield blueprints are weapons...
* Craft Rod?
* In-game GUI
* Bracers of archery (lesser/greater) - requires both Craft Wondrous Items and Craft Arms and Armor
* Fortification enchantment - doesn't appear to be in the base game?
* Craft Construct?  Either a build-your-own-pet, Eidolon style, or a Golem-in-a-bottle like arrangement.

there are some dragonhide equipment entities, but there doesn't seem to be any blueprints associated with them?
EE_Armor_BreastplateDragonhideBlack_F    0e8cac2f5731bab47a491d68c64c687e    EE_Armor_BreastplateDragonhideBlack_F    EquipmentEntity    Object    2786126
EE_Armor_BreastplateDragonhideBlue_F    4fda804453f14c84abe5675088143264    EE_Armor_BreastplateDragonhideBlue_F    EquipmentEntity    Object    2789596
EE_Armor_BreastplateDragonhideRed_F    9cbd42ee72bd9e04ab500893c2869d09    EE_Armor_BreastplateDragonhideRed_F    EquipmentEntity    Object    2790462
EE_Armor_BreastplateDragonhideWhite_F    f668a7a4c2c7a5b459446aaed098e823    EE_Armor_BreastplateDragonhideWhite_F    EquipmentEntity    Object    2793650