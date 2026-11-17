# Costume wardrobe

The wardrobe wears one costume over the armour. The costume changes the body drawn and adds its item's roll to the
character's stats, and takes no equipment slot: the armour under it stays worn. The worn costume is held in the
wardrobe, out of both bag pages, until it is taken off. The server decides what is worn; the client sends the choice
and draws the result.

## Read this when

- You add a costume, change who may wear one, or change the body a costume draws for a class.
- You touch how a costume's roll adds to the stats, or what the shop offers and sells.
- You work on the inventory's wardrobe page, the costume's body on players, or costume wings.
- You change how the worn costume is saved or sent.

## Code map

| Path | Responsibility |
| --- | --- |
| `Shared/PristonTale.Simulation/Items/Data/Costumes.json` | The costume list (embedded resource): item key, gender and the body per armour row |
| `Shared/PristonTale.Simulation/Items/CostumeCatalog.cs` | `CostumeCatalog.Shipped`: `Contains`, `Allows(key, female)`, `Wears(key, count, female)`, `TryBody(key, row)`, `Bodies()`; strict loading |
| `Shared/PristonTale.Simulation/Characters/PlayerCharacter.Costume.cs` | `Costume`, `CostumeItemId`, `CostumeKey`, `MayWearCostume`, `TryWearCostume`, `TryClearCostume`, `RestoreCostume` |
| `Shared/PristonTale.Simulation/Characters/PlayerCharacter.cs` | `Fits`: a costume never goes in an equipment slot |
| `Shared/PristonTale.Simulation/Items/InventoryCommand.cs` | `InventoryAction.WearCostume` and `ClearCostume` |
| `Shared/PristonTale.Simulation/Items/EquipmentTotals.cs`, `Characters/DerivedStats.cs` | The costume's roll in the totals; bare-hand damage |
| `Shared/PristonTale.Simulation/World/Npcs/NpcShopRules.cs` | `CanBuy`: a costume is sold only to whom may wear it |
| `Shared/PristonTale.Application/World/NpcServiceTransactions.cs` | `Catalog`: the offers a buyer sees |
| `Persistence/PristonTale.Persistence.Abstractions/Characters/CharacterStateCodec.cs` | The `Wardrobe` item; an older save's costume in an equipment slot or named by `CostumeItemId` |
| `Shared/PristonTale.Simulation/Characters/OwnerState.cs`, `Shared/PristonTale.Application/World/WorldPlayer.cs`, `Shared/PristonTale.Application/Services/CharacterAppService.cs` | The costume in the owner status (`OwnedItemView.WardrobeSlot`), the appearance others see and the character list |
| `Client/PristonTale.Gameplay/UI/Inventory/HudInventoryController.cs`, `HudInventoryLayout.cs`, `HudInventoryPanel.cs` | The wardrobe page: button, slot, wear and take off |
| `Client/PristonTale.Gameplay/World/Remote/EquipmentVisuals.cs` | `FindCostumeBodies` at start, `Apply(..., costume)` |
| `Client/PristonTale.Gameplay/Effects/CostumeWings.cs` | Wings of the premium costumes |
| `tests/PristonTale.Tests/CostumeWardrobeTests.cs`, `EquipmentTests.cs`, `ClientTests.cs`, `SkillLookTests.cs` | Regression cases |

## The costume list

`Data/Costumes.json` lists 206 items: 132 `CA` and 74 `DA` keys, 100 for men, 102 for women and 4 for anyone. 64 of
them name a body for some armour rows; the others keep the armour's body and still add their roll. The combat armours
and robes among the `DA` keys (Imperial, the Khan sets and the 5H sets: DA130, DA171 to DA175, DA182 to DA184 and the
same ordinals in DA2) are armour, not costumes, and are not listed.

```json
{
  "version": 1,
  "costumes": [
    { "itemKey": "DA131", "gender": "male", "bodies": { "0": "char/tmABCD/Ca001.inx", "1": "char/tmABCD/Cb001.inx" } },
    { "itemKey": "DA153", "gender": "any", "bodies": {} }
  ]
}
```

- `gender` is `male`, `female` or `any`; a character's gender is its class's (`ClassProfiles`).
- `bodies` maps an armour row (`CharacterVisualSet.ArmorTableIndex`: 0 Mechanician, 1 Fighter, 2 Pikeman, 3 Archer,
  4 Knight, 5 Atalanta, 6 Priestess, 7 Magician, 8 Assassin, 9 Shaman, 10 Martial Artist) to an asset-relative `.inx`
  under `char/`. Backslashes become slashes.
- Loading refuses another version, an unknown member or gender, an empty or repeated key (case-insensitive), a row
  outside 0..10 and a body outside `char/`, not `.inx`, with a drive or with an empty, `.` or `..` segment. The list is
  read once, when `CostumeCatalog.Shipped` is first used; a bad list stops the program there.

## Wearing

- A costume is a single item on either bag page. Only the wardrobe wears it: `Fits` refuses it in every equipment
  slot, so the equip command, auto-equip on receipt and moves into a slot all fail.
- `WearCostume` names the item; the server accepts it while the item is in the owner's bags and `CostumeCatalog.Wears`
  allows it: a single copy, for the wearer's gender. The item leaves its bag for the wardrobe
  (`PlayerCharacter.Costume`); a costume worn before goes back to that place, or to the first free one of the same
  page, and with no room the wear is refused.
- `ClearCostume` puts the costume into the first free place of the page shown; it is refused while that page is full.
- The worn costume is in neither bag, so it cannot be moved, sold, split or thrown until it is taken off. It counts
  in the carried weight and survives bag page switches.
- The item's level, stat and class requirements do not apply: anyone the gender rule allows wears it and gets its roll.
- The commands carry the inventory revision, as every inventory command does: a stale one is refused.

## Stats

`EquipmentTotals.Sum` adds the worn costume's roll once, after the equipment, with the same rules as any worn item:
defence, absorption, attack rating, critical, block, resistances, regeneration, life, mana and stamina, and the class
bonus of the roll only for its class. Two parts differ:

- Magic mastery adds to the equipment's instead of replacing it; a costume gives no potion space.
- Damage: with a weapon, the costume's range counts in `DamageMin`/`DamageMax` like any worn item's (scaled by the
  weapon formula, `WeaponDamage`). Bare-handed, `CostumeDamageMin`/`CostumeDamageMax` add flat to the derived damage.

Wearing, changing or clearing the costume refreshes the derived stats.

## Shop

`NpcShopRules.CanBuy` sells a costume only to a character who may wear it; every other item to anyone.
`NpcServiceTransactions.Catalog` leaves out the costumes the buyer may not wear, and shows a costume that the stock
lists on the weapon tab on the armour tab, after the armour.

## Saving and the wire

- The inventory JSON keeps the worn costume as an item with `Wardrobe = true` ([persistence](persistence.md)). Load
  puts it back in the wardrobe while the character may wear it.
- Older saves move into the wardrobe on load: a costume saved in an equipment slot, and the bag item an older save's
  `CostumeItemId` names. A costume that cannot go in the wardrobe (one is there already, or the gender rule refuses it)
  goes to the first free place of either bag page, then to the hand; only when all are taken does the load fail
  (`InvalidDataException`). The item is never dropped.
- The owner status lists the worn costume among the items at `OwnedItemView.WardrobeSlot` (`Worn`) and carries
  `CostumeItemId` and `CostumeCode`; the player part of `EntityDescription` (version 4) and each
  `CharacterList` entry carry the costume's item code ([protocol](protocol.md)).

## Client

- The inventory's C button (`HudInventoryLayout.CostumeButton`) is the original's costume button
  (`HudArt.CostumeButton`, in the frame of the side arrows): drawn dimmed at rest, lit while the page is open or under
  the pointer, with the hover sprite `HudArt.CostumeHint`. It shows the wardrobe page (`HudArt.CostumePanel`,
  `CostumeArea`) over the equipment slots, which take no clicks under it; the panel's middle frame is the costume slot
  (`CostumeSlot`).
- The worn costume is drawn in the slot only, never on a bag cell (`HudInventoryLayout.Shows`). Right-clicking a bag
  costume, or dropping a carried one on the slot, wears it; clicking or right-clicking the slot takes it off.
- A costume for the other gender shows `Inventory.CostumeOtherGender` without asking the server; these client refusals
  are reported as a server's are (`HudInventoryController.Refuse`). The Hopy costumes (CA547, CA647) are men's and the
  Cupy ones (CA548, CA648) women's. A carried costume dropped on the armour slot shows `Inventory.CostumeInWardrobe`.
- Any other item set down over the open page goes to the equipment under it and closes the page, as does any item put
  on, so the equipment it changes is shown.
- `EquipmentVisuals.FindCostumeBodies` checks each listed body file once at start (`GameClient`); a missing one is
  logged. `Apply` draws the worn costume's body for the class's armour row when its file was found, and the armour's
  body otherwise. The player, other players and the character select all pass the worn costume.
- Costume wings (`CostumeWings`, [effects](effects.md)) follow the worn costume.

## Pitfalls

- A costume can never be armour: a key added to the list stops being wearable in the armour slot, and saved copies in
  that slot move into the wardrobe on the next load.
- A body listed for a row must be a character body of that row's class; an item's drop mesh is not one. A costume
  without a body for a row is still worn and still adds its roll.
- `Shipped` is static: its `Options` field must stay declared before it, as static fields initialize in order.
- The wardrobe page must not cover the weapon-pair buttons or a bag cell (`ClientTests`).
- The armour table (`EquipmentVisualTables`) still maps the costume ordinals (DA131 and on) to the same bodies the list
  names; since a costume is never armour, only a save not yet loaded since the wardrobe reaches those entries.
- An item that is real armour must not be listed: listing it moves it off the armour slot on the next load.

## Related

[Simulation](simulation.md), [persistence](persistence.md), [protocol](protocol.md), [client](client.md),
[effects](effects.md).
