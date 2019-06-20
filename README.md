This is the source for my mod to add crafting of magic items to Owlcat's Pathfinder: Kingmaker CRPG.

For details, see the mod page here: https://www.nexusmods.com/pathfinderkingmaker/mods/54

## To Do

* Alchemist / Grenadier archetype shouldn't get Brew Potion
* Wizard / Scroll Savant archetype should get Scribe Scroll at 1st level.
* Enchantments with restrictions (such as Celestial "minimum +3") should be displayed in the UI.
* Make characters in town work from the "stash" (and their own equipment) rather than from the party inventory.
* On load, Fast Healing 1 effect from regeneration ring becomes Fast Healing 5... doesn't change GUID?
* It's possible to sell your bonded item to a merchant, and the "downgraded" version appears in your inventory, so you
        can re-equip it and do it again.
* After upgrading the Cloak of Sold Souls a few times its effects duplicate, I have 2 -4 penalties to con and I get +4 to necromancy spells DC
* Treat surface for Tenebrous Depths in rogue-like mode as a safe resting place: Since, you know, that's a fancy banquet table under the
        watchful eye of a silver dragon. This implies time to relax and get other crap done.
* Check current mundane weapons are all in ItemTypes.json:
StandardKama	46e685e26290d2c468d96439198e6896
StandardNunchaku	c0ce1d36d1d3ae246a7587bb17296f07
StandardSai	d60d0d4c78570ef408a0402f9d4313a6
StandardSiangham	1726a42004cf7fa438f47de692763f09
StandardBastardSword	07531989333442348b7d0102b24af236
StandardDuelingSword	4697667a33a6774489e5265a955675a5
StandardDwarvenWaraxe	30711e40771796340ac67172abfd3279
StandardEstoc	4c863a47d69b63647b3b16bbb01d5ba8
StandardFalcata	ee28827188e12a64ba75222b37ac8092
StandardTongi	b47455bac4f039747ad5e4ddc4e981a4
StandardBola	310257322f1f424429243b4d78e79a43
StandardHandCrossbow	215d900cb16e641408fdbcc320eb371e
StandardHeavyRepeatingCrossbow	ff406b9b549af4a4ead4bb180b6d0978
StandardLightRepeatedCrossbow	b50d6e17d6c3c244c84a42b49edda025
StandardShuriken	ab88f557dcf30bb4cb0b758c72cec08e
StandardSlingStaff	dda1a4f8cbf8ad34ca7845ca17313e86
StandardBastardSwordOversized	85c8efabf99ccc143b0fc2d73e795634
StandardDoubleAxe	1d9acaa3c344c1244bcea18095652955
StandardDoubleAxeSecond	b4fe35bae9ef0f64182a0345007f8eb0
StandardDoubleSword	b665770f14e49bc49999d7c3c11c1d61
StandardDoubleSwordSecond	83a19b2e36d74f24daa8978e3a029309
StandardDwarvenUrgrosh	c20f347c84b6605479c9a7b28ccb236b
StandardDwarvenUrgroshSecond	78e7be71f5e44fb448434217fe2942d1
StandardElvenCurvedBlade	f58e421cb8b4ed64ba195123df754055
StandardFauchard	c91f331cdf7e331468490323a2e1613d
StandardGnomeHookedHammer	8998da2cfe0884f47943bd28823c3a51
StandardGnomehookedHammerSecond	e8be459f78e10624596268c316a65a21
StandardHandaxe	238ac092fad27144c9514f82917fbec9
StandardKukri	3125ac6c819db9f4697312710699b637
StandardLightHammer	79e044277b90a05448a71ae3bcaf581a
StandardLightPick	f20e85bd5bb8dc74785afc129284bcda
StandardShortsword	f717b39c351b8b44388c471d4d272f4e
StandardSpikedLightShield	b12650bdb547d7e499cdc29e913088cb
StandardStarknife	d19662d357d752447a801951b7bec798
StandardThrowingAxe	b9ed902d07b622b4f8ec223808f754c1
StandardWeaponLightShield	62c90581f9892e9468f0d8229c7321c4
StandardBattleaxe	6080738f7e97b5646980a0efad2da676
StandardFlail	8bdfa4f81bbc7b540919d770095720be
StandardHeavyPick	e7d3a58b4eb3d7e419ab4cc40f283a32
StandardLongsword	6fd0a849531617844b195f452661b2cd
StandardRapier	1546a05eb151d424eb9132832d5511bb
StandardScimitar	5363519e36752d84698e03a86fb33afb
StandardSpikedHeavyShield	7c8f6712c444cf446a4bd3b8b717cb5c
StandardTrident	231f325de2b32dd4585707f8d0c87af3
StandardWarhammer	3f35d5c01e11d564daa59938dec3db4b
StandardWeaponHeavyShield	ff8047f887565284e93773b4a698c393
StandardCompositeLongbow	d23f6f5d3cbf715488b1f73e130dca99
StandardCompositeShortbow	2ae1cfe7ea8e60d459948193b6a0f7fe
StandardLongbow	201f6150321e09048bd59e9b7f558cb0
StandardShortbow	6d40d84e239bdf345b349ff52e3c00a9
StandardBardiche	5bfdaaa6416cc604cba121d003db11ef
StandardEarthBreaker	fc47ddc975f1f804bbef320e1c574cd7
StandardFalchion	0f0e6834458d01049a408cd304053b1b
StandardGlaive	f83415c0e7ea1994d8a7f3dec8f5a861
StandardGreataxe	6efea466862f014469cec6c3f2b85cb7
StandardGreatsword	2fff2921851568a4d80ed52f76cccdb6
StandardHeavyFlail	7f7c8e1e4fdd99e438b30ed9622e9e3f
StandardScythe	1052a1f7128861942aa0c2ee6078531e



* Rebase custom items on more valuable vanilla items?  E.g. increasing the enhancement bonus of a magic weapon might change to being based on the vanilla weapon with that bonus.
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
