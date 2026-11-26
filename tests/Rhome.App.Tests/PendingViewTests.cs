using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Tests;

/// <summary>What the screens show about pending changes (issue #49): the pending list, and the slots marked until Save.</summary>
public sealed class PendingViewTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 0, 0, TimeSpan.Zero);

    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;
    readonly FakePlatform platform;
    MySaves saves = null!;

    public PendingViewTests() => platform = new FakePlatform(Path.Combine(root, "app"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav"));

    async Task<PendingChanges> Start()
    {
        var melonDS = KnownEmulators.Find("melonds")!;
        var retroArch = KnownEmulators.Find("retroarch")!;
        Directory.CreateDirectory(Path.Combine(root, "melonds"));
        Directory.CreateDirectory(Path.Combine(root, "retroarch", "mGBA"));
        File.WriteAllBytes(Path.Combine(root, "melonds", "Platinum.sav"), Fixture("platinum-us"));
        File.WriteAllBytes(Path.Combine(root, "retroarch", "mGBA", "Emerald.srm"), Fixture("emerald"));
        var setup = new Setup(platform);
        platform.NextPick = new PickedLocation(Path.Combine(root, "melonds"), "melonds");
        await setup.GiveAccessAsync(melonDS, melonDS.Folder("app")!);
        platform.NextPick = new PickedLocation(Path.Combine(root, "retroarch"), "retroarch");
        await setup.GiveAccessAsync(retroArch, retroArch.Folder("saves")!);
        saves = new MySaves(setup, platform.Storage);
        saves.Scan(Now);
        return new PendingChanges(platform.Storage);
    }

    Guid Open(PendingChanges pending, GameVersion game) => pending.Open(saves.Open(saves.Games.Single(g => g.Save.Game == game)));

    [Fact]
    public async Task Nothing_pending_shows_nothing()
    {
        var pending = await Start();
        var platinum = Open(pending, GameVersion.Pt);

        Assert.Empty(PendingView.Lines(pending));
        Assert.Empty(PendingView.ArrivingInBank(pending));
        Assert.Empty(PendingView.InSave(pending, platinum).Leaving);
    }

    [Fact]
    public async Task Each_move_says_where_the_pokemon_goes_and_marks_the_slots_it_leaves_and_arrives_in()
    {
        var pending = await Start();
        var platinum = Open(pending, GameVersion.Pt);
        var emerald = Open(pending, GameVersion.E);
        var id = Guid.NewGuid();
        var from = new SaveSlot(1, 1);
        var pk = pending.Save(platinum).GetBoxSlotAtIndex(0);
        var name = $"{EnglishNames.Species(pk.Species)} · Lv{pk.CurrentLevel}";

        Assert.True(pending.Stage(new ImportChange(platinum, from, new BankSlot(1, 1), NewId: id), Now).Succeeded);
        var import = Assert.Single(PendingView.Lines(pending));
        Assert.Equal(new PendingLine(name, $"{pending.SaveName(platinum)} {SaveContents.BoxName(pending.Save(platinum), 1)} → Bank Box 1",
            PendingDirection.IntoBank, 0), import);
        Assert.Equal([from], PendingView.InSave(pending, platinum).Leaving);
        Assert.Equal([new BankSlot(1, 1)], PendingView.ArrivingInBank(pending));

        // Kirlia, first in Platinum's party, can go on to Emerald.
        var kirlia = Guid.NewGuid();
        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(null, 1), new BankSlot(1, 2), NewId: kirlia), Now).Succeeded);
        var to = new SaveSlot(1, 2);
        Assert.True(pending.Stage(new WithdrawChange(kirlia, emerald, to), Now).Succeeded);
        var withdraw = PendingView.Lines(pending)[2];
        Assert.Equal(PendingDirection.OutToGame, withdraw.Direction);
        Assert.Equal($"Bank Box 1 → {pending.SaveName(emerald)} {SaveContents.BoxName(pending.Save(emerald), 1)}", withdraw.Route);
        Assert.Equal([to], PendingView.InSave(pending, emerald).Arriving);
        Assert.Empty(PendingView.InSave(pending, emerald).Leaving);
    }

    [Fact]
    public async Task A_party_slot_is_never_marked_as_left_empty()
    {
        var pending = await Start();
        var platinum = Open(pending, GameVersion.Pt);

        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(null, 1), new BankSlot(1, 1), NewId: Guid.NewGuid()), Now).Succeeded);
        Assert.Empty(PendingView.InSave(pending, platinum).Leaving); // the party closes up behind it
    }

    [Fact]
    public async Task A_move_inside_the_bank_says_where_to()
    {
        var pending = await Start();
        var platinum = Open(pending, GameVersion.Pt);
        var id = Guid.NewGuid();
        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 1), NewId: id), Now).Succeeded);

        Assert.True(pending.Stage(new MoveInBankChange(id, new BankSlot(2, 5)), Now).Succeeded);
        var move = PendingView.Lines(pending)[1];
        Assert.Equal(PendingDirection.InBank, move.Direction);
        Assert.Equal("To Bank Box 2 slot 5", move.Route);
    }
}
