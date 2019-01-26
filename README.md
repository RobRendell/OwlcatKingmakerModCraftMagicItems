This is the source for my mod to add crafting of magic items to Owlcat's Pathfinder: Kingmaker CRPG.

For details, see the mod page here: https://www.nexusmods.com/pathfinderkingmaker/mods/54

## To Do
* Disable more stuff if the mod is disabled (PostLoad/MainMenu.Start)
* Crafting skill bonus for favoured school, penalty for opposition school.
* Remove assumption that GUIDs are 32 characters long, given more mods with custom GUIDs are becoming available.
* In BuildCustomRecipeItemGuid, sort enchantments and remove GUIDs lexographically so blueprint is always the same.
* Add "Fabricate" custom spell to instantly do mundane crafting, and to add special materials to existing items.
* Enchanting bonded objects.  Custom buff on caster, contains ref to original item and current item (apply renames to
        original item as well?)  If item is not equipped, switch current and original item in shared stash when the
        current character changes (ideally without printing to the battle log).  Also, if item is not equipped, apply
        concentration check to cast, or if I can't get that to work, RuleCastSpell has a general SpellFailureChance
        percentage which could do an equivalent effect.
* Make Robes fall under Craft Wondrous Items rather than Arms and Armor?
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
