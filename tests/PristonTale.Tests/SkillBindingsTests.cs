using System.Numerics;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Packets;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillBindingsTests
{
    // A magician's Fire Bolt takes either socket; Agony takes only the right one.
    private const uint EitherSide = 0x08010002;
    private const uint RightOnly = 0x08010001;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skill bindings refuse what the class or the socket side does not offer and survive a save and an inventory clone", RulesAndSaveAsync);
        yield return ("skill bindings cross the wire, reach the owner state and are stored by the world for the connected player", WireAndWorldAsync);
        yield return ("the HUD restores saved bindings once, keeps the basic attack for unlearned skills and counts only the owner's changes", HudAsync);
    }

    private static SkillBindings Bindings(uint left, uint right, params SkillShortcut[] keys)
    {
        var shortcuts = new SkillShortcut[SkillBindings.ShortcutCount];
        keys.CopyTo(shortcuts, 0);
        return new SkillBindings(left, right, [.. shortcuts]);
    }

    private static bool Same(SkillBindings left, SkillBindings right) =>
        left.Left == right.Left && left.Right == right.Right && left.Shortcuts.SequenceEqual(right.Shortcuts);

    private static Task RulesAndSaveAsync()
    {
        var classes = new CharacterClassRegistry();
        var mage = classes.CreatePlayer(PlayerClass.Magician, "Binder", 30);
        var chosen = Bindings(EitherSide, RightOnly, new SkillShortcut(EitherSide, SkillMouseUse.Left), new SkillShortcut(RightOnly, SkillMouseUse.Right));
        Assert.True(Same(mage.SkillBindings, SkillBindings.Default), "a new character does not start on the basic attack");
        Assert.True(mage.SetSkillBindings(chosen) && Same(mage.SkillBindings, chosen), "valid bindings were refused");
        var fighterSkill = classes.Get(PlayerClass.Fighter).Skills.First(skill => skill.MouseUse != SkillMouseUse.None).Code;
        var passive = classes.Get(PlayerClass.Magician).Skills.First(skill => skill.MouseUse == SkillMouseUse.None).Code;
        Assert.True(!mage.SetSkillBindings(Bindings(RightOnly, SkillBindings.NormalAttack)), "a right-only skill went on the left socket");
        Assert.True(!mage.SetSkillBindings(Bindings(fighterSkill, SkillBindings.NormalAttack)), "another class's skill was accepted");
        Assert.True(!mage.SetSkillBindings(Bindings(SkillBindings.NormalAttack, passive)), "a passive skill was accepted");
        Assert.True(!mage.SetSkillBindings(Bindings(SkillBindings.NormalAttack, SkillBindings.NormalAttack, new SkillShortcut(RightOnly, SkillMouseUse.Left))),
            "a key selecting the wrong side was accepted");
        Assert.True(!mage.SetSkillBindings(new SkillBindings(EitherSide, EitherSide, [new SkillShortcut(EitherSide, SkillMouseUse.Left)])),
            "bindings without the eight keys were accepted");
        Assert.True(Same(mage.SkillBindings, chosen), "a refused change altered the bindings");
        Assert.True(Same(mage.CloneForInventoryOperation().SkillBindings, chosen), "an inventory operation's candidate lost the bindings");

        var starter = CharacterRecordFactory.NewStarter(classes, 1, mage.Name, 0, PlayerClass.Magician, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, mage, 3, Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Magician, mage.Name, 30);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([]));
        Assert.True(Same(restored.SkillBindings, chosen), "the bindings were not saved");

        var older = classes.CreatePlayer(PlayerClass.Magician, mage.Name, 30);
        CharacterStateCodec.Restore(older, record with { Skills = new JsonText("""{"Version":1,"Points":0,"ElitePoints":0,"Skills":[]}""") },
            new ItemCatalog([]));
        Assert.True(Same(older.SkillBindings, SkillBindings.Default), "a record saved before bindings did not start on the basic attack");

        var stale = classes.CreatePlayer(PlayerClass.Magician, mage.Name, 30);
        stale.RestoreSkillBindings(Bindings(fighterSkill, RightOnly, new SkillShortcut(fighterSkill, SkillMouseUse.Left),
            new SkillShortcut(EitherSide, SkillMouseUse.Right)));
        Assert.True(Same(stale.SkillBindings, Bindings(SkillBindings.NormalAttack, RightOnly, default, new SkillShortcut(EitherSide, SkillMouseUse.Right))),
            "a saved skill the class no longer offers was kept");
        return Task.CompletedTask;
    }

    private static async Task WireAndWorldAsync()
    {
        var chosen = Bindings(EitherSide, RightOnly, new SkillShortcut(RightOnly, SkillMouseUse.Right));
        var buffer = new byte[256];
        var sent = SkillBindingsPacket.Read(buffer.AsSpan(0, new SkillBindingsPacket(WireMapping.Bindings(chosen)).Write(buffer)));
        Assert.True(Same(WireMapping.Bindings(sent.Bindings), chosen), "the bindings message did not round-trip");
        var length = new SkillsPacket(4, [(EitherSide, 2, 10)], WireMapping.Bindings(chosen)).Write(buffer);
        var skills = SkillsPacket.Read(buffer.AsSpan(0, length));
        Assert.True(skills.Revision == 4 && skills.Skills.Single() == (EitherSide, 2, 10) && Same(WireMapping.Bindings(skills.Bindings), chosen),
            "the skill list lost the bindings");
        Assert.Throws<InvalidDataException>(() => SkillsPacket.Read(buffer.AsSpan(0, length - 1)), "a skill list missing a key's side was read");
        var full = new SkillBindingsPacket(new SkillBindingsEntry(0, 0, new (uint, byte)[SkillBindingsEntry.MaxShortcuts])).Write(buffer);
        buffer[8 + Protocol.Binary.WireHeader.Size + 4 + 4]++;
        Assert.Throws<InvalidDataException>(() => SkillBindingsPacket.Read(buffer.AsSpan(0, full)), "bindings with too many keys were read");

        var mage = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Owner", 30);
        mage.SetSkillBindings(chosen);
        var state = OwnerState.Capture(mage);
        var scratch = new byte[Protocol.Binary.FrameCodec.MaxFrameSize];
        var packets = new List<byte[]>
        {
            scratch[..ItemWireMapping.Status(state).Write(scratch)],
            scratch[..WireMapping.Status(state).Write(scratch)],
            scratch[..new InventoryPartPacket(state.InventoryRevision, 0, 1, []).Write(scratch)],
            scratch[..new SkillsPacket(state.SkillsRevision, [], WireMapping.Bindings(state.SkillBindings)).Write(scratch)]
        };
        var decoder = new PresenceDecoder(new WorldRenderCatalog(), static _ => { });
        OwnerState? owner = null;
        foreach (var packet in packets)
        {
            if (decoder.TryHandle(packet, out var update) && update?.Owner is { } received)
            {
                owner = received;
            }
        }

        Assert.True(owner is not null && Same(owner.SkillBindings, chosen), "the decoded owner state lost the bindings");

        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0, playerClass: PlayerClass.Magician, level: 30);
        Assert.True(await fixture.RunAsync(fixture.World.SetSkillBindingsAsync(ann.Id, chosen)) && Same(ann.Session.Character!.SkillBindings, chosen),
            "the world did not keep the player's bindings");
        Assert.True(!await fixture.RunAsync(fixture.World.SetSkillBindingsAsync(ann.Id, Bindings(RightOnly, RightOnly)))
            && Same(ann.Session.Character!.SkillBindings, chosen), "the world kept invalid bindings");
        Assert.True(!await fixture.RunAsync(fixture.World.SetSkillBindingsAsync(ann.Id + 1000, chosen)), "bindings reached a player who is not connected");
    }

    private static Task HudAsync()
    {
        var skills = new CharacterClassRegistry().Get(PlayerClass.Magician).Skills;
        var bindings = new HudSkillBindings();
        bindings.SyncLearned(skills, [new OwnedSkillView(EitherSide, 2)]);
        bindings.Restore(Bindings(EitherSide, RightOnly, new SkillShortcut(RightOnly, SkillMouseUse.Right)));
        Assert.True(bindings.Restored && bindings.Revision == 0 && bindings.LeftSkillId == EitherSide && bindings.RightSkillId == HudSkillBindings.BasicAttackId,
            "the saved sockets were not restored, or an unlearned skill took a socket");
        Assert.True(bindings.Shortcut(0) == new HudSkillShortcut(RightOnly, SkillMouseUse.Right), "a saved key was dropped");
        Assert.True(bindings.Select(EitherSide, SkillMouseUse.Left) && bindings.Revision == 0, "choosing the skill already there counted as a change");
        Assert.True(bindings.Select(EitherSide, SkillMouseUse.Right) && bindings.Revision == 1, "a socket change was not counted");
        Assert.True(bindings.Assign(1, EitherSide, SkillMouseUse.Left) && bindings.Revision == 2, "a key change was not counted");
        var snapshot = bindings.Snapshot();
        Assert.True(snapshot.Left == EitherSide && snapshot.Right == EitherSide && snapshot.Shortcuts[0] == new SkillShortcut(RightOnly, SkillMouseUse.Right)
            && snapshot.Shortcuts[1] == new SkillShortcut(EitherSide, SkillMouseUse.Left), "the snapshot differs from the HUD");
        Assert.True(snapshot.IsValidFor(skills), "the HUD's snapshot would be refused by the server");
        return Task.CompletedTask;
    }
}
