using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// DA131 is a man's costume with bodies, DA133 a woman's, DA153 for anyone with no body; DA130 is armour (Data/Costumes.json).
internal static class CostumeWardrobeTests
{
    private static readonly CharacterClassRegistry Classes = new();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("costume list: the shipped list loads and a bad list is refused", CatalogAsync);
        yield return ("costume wardrobe: a costume is never real armour and only the wardrobe wears it", NeverArmorAsync);
        yield return ("costume wardrobe: wearing needs the owner's item, the current revision and the right gender", WearAsync);
        yield return ("costume wardrobe: the worn costume leaves the bag and comes back to the page shown", OutOfTheBagAsync);
        yield return ("costume shop: a costume is sold and offered only to whom may wear it", ShopAsync);
        yield return ("costume wardrobe: the worn costume survives a copy, a page switch and a relog in the wardrobe", PersistenceAsync);
        yield return ("costume wardrobe: an older save's costume, in the armour slot or worn from a bag, moves into the wardrobe", LegacyArmorSlotAsync);
        yield return ("costume wardrobe: the costume's roll adds to the armour once, on either bag page", AdditiveStatsAsync);
        yield return ("costume wire: roster, owner status and description carry the costume and the clan", WireAsync);
    }

    private static ItemDefinition Of(string key, ItemKind kind = ItemKind.Armor) =>
        new(ItemCodes.Parse(key), key, kind, kind == ItemKind.Armor ? EquipmentSlot.Armor : null) { CatalogKey = key, Price = 100 };

    private static Task CatalogAsync()
    {
        var shipped = CostumeCatalog.Shipped;
        Assert.True(shipped.Count == 206 && shipped.Contains("da131") && !shipped.Contains("DA101") && !shipped.Contains(null!),
            "the shipped costume list is not the expected one");
        Assert.True(!shipped.Contains("DA130") && !shipped.Contains("DA171") && !shipped.Contains("DA284"), "a combat armour or robe is listed as a costume");
        Assert.True(shipped.Allows("DA131", female: false) && !shipped.Allows("DA131", female: true)
            && shipped.Allows("DA153", female: true) && shipped.Allows("DA153", female: false), "costume genders were read wrong");
        Assert.True(!shipped.TryBody("DA153", 1, out _) && !shipped.TryBody("DA131", 3, out _), "a costume drew a body it does not have");
        var loaded = CostumeCatalog.FromJson("""{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"0":"char\\tmABCD\\Ca001.inx"}}]}""");
        Assert.True(loaded.TryBody("da147", 0, out var body) && body == "char/tmABCD/Ca001.inx", "a body path was not normalized");
        foreach (var invalid in new[]
        {
            """{"version":2,"costumes":[]}""",
            """{"version":1,"costumes":[{"itemKey":"","gender":"any","bodies":{}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"11":"char/a.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"0":"char/../a.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"0":"image/a.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"0":"char/a.smd"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{"0":"char/c:/a.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"any","bodies":{}},{"itemKey":"da147","gender":"any","bodies":{}}]}"""
        })
        {
            Assert.Throws<InvalidDataException>(() => CostumeCatalog.FromJson(invalid), invalid);
        }

        foreach (var malformed in new[]
        {
            """{"version":1,"costumes":[{"itemKey":"DA147","gender":"other","bodies":{}}]}""",
            """{"version":1,"costumes":[],"extra":1}"""
        })
        {
            Assert.Throws<System.Text.Json.JsonException>(() => CostumeCatalog.FromJson(malformed), malformed);
        }

        return Task.CompletedTask;
    }

    private static Task NeverArmorAsync()
    {
        var character = Classes.CreatePlayer(PlayerClass.Fighter, "Costume", 30);
        foreach (var key in new[] { "DA131", "DA153", "CA547" })
        {
            var item = new ItemInstance(Of(key));
            Assert.True(!character.CanHold(EquipmentSlot.Armor, item) && !character.CanEquip(EquipmentSlot.Armor, item),
                $"{key} could be worn as armour");
            Assert.True(character.TryReceive(item) && character.GetEquipped(EquipmentSlot.Armor) is null, $"{key} was put on as armour");
            var equip = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Equip, item.InstanceId, 0, 0, EquipmentSlot.Armor);
            Assert.True(!InventoryCommands.Apply(character, equip) && character.GetEquipped(EquipmentSlot.Armor) is null,
                $"the equip command put {key} in the armour slot");
        }

        return Task.CompletedTask;
    }

    private static (PlayerCharacter Character, ItemDefinition Costume, ItemDefinition Armor, ItemInstance Item) Setup()
    {
        var character = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        var costume = Of("DA131");
        var armor = Of("DA108");
        Assert.True(character.Inventory.TryPlace(new ItemInstance(costume), 0), "the costume does not fit in the bag");
        character.Equip(EquipmentSlot.Armor, new ItemInstance(armor));
        return (character, costume, armor, character.Inventory[0]);
    }

    private static InventoryCommand Command(PlayerCharacter character, InventoryAction action, Guid itemId) =>
        new(Guid.NewGuid(), character.InventoryRevision, action, itemId, 0, 0, default);

    private static Task WearAsync()
    {
        var (character, _, armor, item) = Setup();
        var before = character.Derived;
        Assert.True(!InventoryCommands.Apply(character, Command(character, InventoryAction.WearCostume, Guid.NewGuid())), "a stranger's item was worn");
        Assert.True(!InventoryCommands.Apply(character, Command(character, InventoryAction.ClearCostume, Guid.Empty)), "nothing was taken off");
        var wear = Command(character, InventoryAction.WearCostume, item.InstanceId);
        Assert.True(InventoryCommands.Apply(character, wear) && character.CostumeKey == "DA131" && character.CostumeItemId == item.InstanceId,
            "the owner's costume was refused");
        Assert.True(character.GetEquipped(EquipmentSlot.Armor)?.Definition.Code == armor.Code && character.Derived == before,
            "a costume with no roll changed the armour or the stats");
        Assert.True(!InventoryCommands.Apply(character, wear), "a stale revision was accepted");
        Assert.True(InventoryCommands.Apply(character, Command(character, InventoryAction.ClearCostume, Guid.Empty)) && character.CostumeKey == "",
            "the costume did not come off");

        var priestess = Classes.CreatePlayer(PlayerClass.Priestess, "Priestess", 30);
        var male = new ItemInstance(Of("DA131"));
        var female = new ItemInstance(Of("DA133"));
        var anyone = new ItemInstance(Of("DA153"));
        Assert.True(priestess.Inventory.TryPlace(male, 0) && priestess.Inventory.TryPlace(female, 2) && priestess.Inventory.TryPlace(anyone, 4),
            "the costumes do not fit in the bag");
        Assert.True(!priestess.TryWearCostume(male.InstanceId) && priestess.TryWearCostume(female.InstanceId)
            && priestess.TryWearCostume(anyone.InstanceId) && priestess.CostumeKey == "DA153",
            "the wardrobe did not follow the costume's gender or a costume without a body was refused");
        Assert.True(priestess.Inventory.Find(female.InstanceId) == 4 && priestess.Inventory.Find(anyone.InstanceId) < 0,
            "the costume worn before did not go back to the place of the one put on");
        return Task.CompletedTask;
    }

    private static Task OutOfTheBagAsync()
    {
        var (character, _, _, item) = Setup();
        Assert.True(character.TrySwitchBagPage(1) && character.TrySwitchBagPage(0), "the bag pages did not switch");
        Assert.True(character.TryWearCostume(item.InstanceId) && character.Inventory.Find(item.InstanceId) < 0
            && character.HiddenBag.Find(item.InstanceId) < 0 && ReferenceEquals(character.Costume, item), "the worn costume stayed in the bag");
        var owned = OwnerState.Capture(character).Items.Where(view => view.InstanceId == item.InstanceId).ToList();
        Assert.True(owned.Count == 1 && owned[0].Worn && !owned[0].InBag && !owned[0].Equipped && !owned[0].Held,
            "the owner status did not show the costume in the wardrobe only");

        while (character.Inventory.TryPlaceFirstFree(new ItemInstance(Of("DA108"))))
        {
        }

        var revision = character.InventoryRevision;
        Assert.True(!character.TryClearCostume() && ReferenceEquals(character.Costume, item) && character.InventoryRevision == revision,
            "a costume came off into a full page");
        Assert.True(character.Inventory.TakeAt(0) is not null && character.TryClearCostume() && character.Costume is null
            && character.Inventory.Find(item.InstanceId) >= 0, "the costume did not come back to the page shown");
        return Task.CompletedTask;
    }

    private static Task ShopAsync()
    {
        var priestess = Classes.CreatePlayer(PlayerClass.Priestess, "Priestess", 30);
        var magician = Classes.CreatePlayer(PlayerClass.Magician, "Magician", 30);
        priestess.RestoreProgress(0, 1_000);
        Assert.True(!NpcShopRules.CanBuy(priestess, Of("DA131")) && NpcShopRules.CanBuy(magician, Of("DA131"))
            && NpcShopRules.CanBuy(priestess, Of("DA133")) && !NpcShopRules.CanBuy(magician, Of("DA133"))
            && NpcShopRules.CanBuy(priestess, Of("DA153")) && NpcShopRules.CanBuy(priestess, Of("DA101")),
            "the shop did not follow the costume's gender, or refused an ordinary item");
        Assert.True(NpcShopRules.TryBuy(priestess, Of("DA131"), 1, -1, out _) == NpcServiceStatus.Rejected
            && NpcShopRules.TryBuy(priestess, Of("DA133"), 1, -1, out _) == NpcServiceStatus.Ok, "a purchase ignored the costume's gender");

        var items = new ItemCatalog([Of("DA131"), Of("DA133"), Of("DA101")]);
        var stock = new NpcShopStock([ItemCodes.Parse("DA133")], [ItemCodes.Parse("DA131"), ItemCodes.Parse("DA101")], []);
        var offers = NpcServiceTransactions.Catalog(stock, items, 0, priestess);
        Assert.True(offers.Count == 2 && offers.All(static offer => offer.Tab == NpcShopStock.ArmorTab)
            && offers.Single(static offer => offer.CatalogKey == "DA133").Ordinal > offers.Single(static offer => offer.CatalogKey == "DA101").Ordinal,
            "the shop offered a costume the buyer cannot wear, or did not list a weapon-tab costume after the armour");
        Assert.True(NpcServiceTransactions.Catalog(stock, items, 0, magician).Count == 2, "the shop hid a costume the buyer may wear");
        return Task.CompletedTask;
    }

    private static PlayerCharacter Relog(PlayerCharacter character, params ItemDefinition[] definitions)
    {
        var record = CharacterRecordFactory.NewStarter(Classes, 1, "Wardrobe", 0, character.Class.Class, 3);
        var stored = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero).Encode();
        var restored = Classes.CreatePlayer(character.Class.Class, "Wardrobe", 50);
        CharacterStateCodec.Restore(restored, stored, new ItemCatalog(definitions));
        return restored;
    }

    private static Task PersistenceAsync()
    {
        var (character, costume, armor, item) = Setup();
        Assert.True(character.TryWearCostume(item.InstanceId), "the costume was refused");
        var clone = character.CloneForInventoryOperation();
        Assert.True(clone.CostumeItemId == item.InstanceId && !ReferenceEquals(clone.Costume, item), "the copy lost or shared the worn costume");
        Assert.True(character.TrySwitchBagPage(1) && character.CostumeItemId == item.InstanceId, "a page switch took the costume off");
        var restored = Relog(clone, costume, armor);
        Assert.True(restored.CostumeKey == "DA131" && restored.CostumeItemId == item.InstanceId && restored.Inventory.Find(item.InstanceId) < 0
            && restored.HiddenBag.Find(item.InstanceId) < 0 && restored.GetEquipped(EquipmentSlot.Armor)?.Definition.Code == armor.Code,
            "the relog lost the costume, put it in a bag or changed the armour");
        return Task.CompletedTask;
    }

    private static Task LegacyArmorSlotAsync()
    {
        var record = CharacterRecordFactory.NewStarter(Classes, 1, "Wardrobe", 0, PlayerClass.Fighter, 3);
        var catalog = new ItemCatalog([Of("DA108"), Of("DA131")]);
        var character = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        var armor = new ItemInstance(Of("DA108"));
        character.Equip(EquipmentSlot.Armor, armor);
        var restored = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        CharacterStateCodec.Restore(restored, AsCostume(CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero)).Encode(), catalog);
        Assert.True(restored.GetEquipped(EquipmentSlot.Armor) is null && restored.Inventory.Find(armor.InstanceId) < 0
            && restored.CostumeItemId == armor.InstanceId, "a costume saved as armour was not moved into the wardrobe");

        // A second one, beside a costume already in the wardrobe, goes to the bag.
        var wearing = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        var worn = new ItemInstance(Of("DA131"));
        var second = new ItemInstance(Of("DA108"));
        wearing.Equip(EquipmentSlot.Armor, second);
        Assert.True(wearing.Inventory.TryPlace(worn, 0) && wearing.TryWearCostume(worn.InstanceId), "the costume was refused");
        var both = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        CharacterStateCodec.Restore(both, AsCostume(CharacterRecordFactory.Capture(record, wearing, 3, Vector3.Zero)).Encode(), catalog);
        Assert.True(both.CostumeItemId == worn.InstanceId && both.Inventory.Find(second.InstanceId) >= 0 && both.GetEquipped(EquipmentSlot.Armor) is null,
            "a second costume saved as armour was not set down in the bag");

        // A save that wore its costume from a bag names it beside the items.
        var fromBag = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        var bagged = new ItemInstance(Of("DA131"));
        Assert.True(fromBag.Inventory.TryPlace(bagged, 0), "the costume does not fit in the bag");
        var snapshot = CharacterRecordFactory.Capture(record, fromBag, 3, Vector3.Zero);
        var older = snapshot with { Inventory = snapshot.Inventory with { CostumeItemId = bagged.InstanceId } };
        var moved = Classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        CharacterStateCodec.Restore(moved, older.Encode(), catalog);
        Assert.True(moved.CostumeItemId == bagged.InstanceId && moved.Inventory.Find(bagged.InstanceId) < 0,
            "a costume an older save wore from the bag was not moved into the wardrobe");
        return Task.CompletedTask;
    }

    // The save with its armour-slot item turned into the costume DA131, as a save from before the wardrobe could hold.
    private static CharacterSaveSnapshot AsCostume(CharacterSaveSnapshot snapshot)
    {
        var saved = snapshot.Equipment.Items.Single(static saved => saved.Slot == EquipmentSlot.Armor);
        return snapshot with
        {
            Equipment = snapshot.Equipment with
            {
                Items = [.. snapshot.Equipment.Items.Where(other => other != saved), saved with { Code = ItemCodes.Parse("DA131") }]
            }
        };
    }

    private static Task AdditiveStatsAsync()
    {
        var (character, costume, armor, item) = Setup();
        var equipped = character.GetEquipped(EquipmentSlot.Armor);
        equipped.Roll = equipped.Roll with { Defence = 100, Absorb = 4, AddLife = 20 };
        item.Roll = item.Roll with
        {
            Defence = 120, Absorb = 9, AddLife = 50, AddMana = 20, AddStamina = 30,
            DamageMin = 7, DamageMax = 12, AttackRating = 40, LifeRegen = 2,
            SpecJob = (uint)character.Definition.JobBitCode,
            Spec = ItemSpecBonus.None with { Defence = 15, Absorb = 2 }
        };
        character.TouchInventory();
        var baseline = character.Derived;
        var totals = character.EquipmentTotals;
        Assert.True(character.TryWearCostume(item.InstanceId), "the costume was refused");
        var added = character.Derived;
        Assert.True(ReferenceEquals(character.GetEquipped(EquipmentSlot.Armor), equipped), "the armour was replaced");
        Assert.True(character.EquipmentTotals.Defence == totals.Defence + 135 && added.Defence == baseline.Defence + 135,
            "the costume's defence and its class bonus were not added to the armour's");
        Assert.Near(totals.Absorb + 11, character.EquipmentTotals.Absorb);
        Assert.Near(totals.LifeRegen + 2, character.EquipmentTotals.LifeRegen);
        Assert.True(added.MaxLife == baseline.MaxLife + 50 && added.MaxMana == baseline.MaxMana + 20
            && added.MaxStamina == baseline.MaxStamina + 30 && added.AttackRating == baseline.AttackRating + 40,
            "the costume's pools or rating were not added");
        Assert.True(added.AttackDamageMin == baseline.AttackDamageMin + 7 && added.AttackDamageMax == baseline.AttackDamageMax + 12,
            "bare-hand damage lost the costume's damage");
        var owner = OwnerState.Capture(character);
        Assert.True(owner.Defence == added.Defence && owner.MaxLife == added.MaxLife && owner.CostumeItemId == item.InstanceId
            && owner.CostumeKey == "DA131", "the owner status did not carry the worn costume and its bonuses");
        Assert.True(character.TrySwitchBagPage(1) && character.Derived == added, "a costume on the hidden page lost its bonuses");
        Assert.True(character.CloneForInventoryOperation().Derived == added, "a copy lost or doubled the bonuses");
        Assert.True(Relog(character, costume, armor).Derived == added, "the relog lost the costume's roll");

        item.Roll = item.Roll with { SpecJob = 0 };
        character.TouchInventory();
        Assert.True(character.Derived.Defence == baseline.Defence + 120, "a class bonus for another class was added");
        Assert.True(character.TryClearCostume() && character.Derived == baseline, "a costume taken off kept its bonuses");

        var weapon = new ItemInstance(new ItemDefinition(ItemCodes.Parse("WA101"), "Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA101" });
        weapon.Roll = weapon.Roll with { DamageMin = 15, DamageMax = 25 };
        character.Equip(EquipmentSlot.Weapon, weapon);
        var armed = PristonTale.Simulation.Combat.WeaponDamage.ComputeForHit(character);
        Assert.True(character.TryWearCostume(item.InstanceId), "the costume was refused with a weapon held");
        var armedWithCostume = PristonTale.Simulation.Combat.WeaponDamage.ComputeForHit(character);
        Assert.True(armedWithCostume.Min > armed.Min && armedWithCostume.Max > armed.Max && ReferenceEquals(character.GetEquipped(EquipmentSlot.Weapon), weapon),
            "armed damage left out the costume's damage or the weapon was replaced");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var cosmetic = ItemCodes.Parse("DA147");
        var owner = new OwnerStatusPacket(50, 0, 0, 1, 0, 100, 100, 100, 100, 100, 100, 0, 0, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1f, 4, 1, 1, 0, 0, 0, 0, Guid.NewGuid(), cosmetic);
        var ownerBuffer = new byte[OwnerStatusPacket.Size];
        var readOwner = OwnerStatusPacket.Read(ownerBuffer.AsSpan(0, owner.Write(ownerBuffer)));
        Assert.True(readOwner.CostumeItemId == owner.CostumeItemId && readOwner.CostumeCode == cosmetic,
            "owner status omitted vanity identity");
        var entry = new CharacterListEntry("Wardrobe", PlayerClass.Fighter, 50, 0, 0, 0, 0, 0, 0, true, "Guild")
            { CostumeCode = cosmetic };
        var list = new CharacterListPacket([entry]);
        var roster = new byte[CharacterListPacket.MaxSize];
        var copy = CharacterListPacket.Read(roster.AsSpan(0, list.Write(roster))).Characters[0];
        Assert.True(copy.CostumeCode == cosmetic && copy.ClanName == "Guild", "roster overwrote the guild or costume");
        var look = new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, true, false, 0, 0)
            { Costume = cosmetic, ClanId = 7UL, ClanName = "Guild", ClanMark = 2 };
        var packet = new EntityDescriptionPacket(42, 1, EntityKind.Player, 50, 3, 1f, "Wardrobe", "", look);
        var wire = new byte[EntityDescriptionPacket.MaximumSize];
        var result = EntityDescriptionPacket.Read(wire.AsSpan(0, packet.Write(wire)));
        Assert.True(result.Player?.Costume == cosmetic && result.Player.ClanId == 7UL && result.Player.ClanName == "Guild"
            && result.Player.ClanMark == 2, "description v4 lost costume or clan");
        return Task.CompletedTask;
    }
}
