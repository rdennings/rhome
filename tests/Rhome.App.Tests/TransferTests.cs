using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Tests;

/// <summary>Moving Pokémon between saves and the bank from the split view (issue #45), on real files.</summary>
public sealed class TransferTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;
    readonly FakePlatform platform;
    MySaves saves = null!;

    public TransferTests() => platform = new FakePlatform(Path.Combine(root, "app"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav"));

    string PlatinumPath => Path.Combine(root, "melonds", "Platinum.sav");
    string EmeraldPath => Path.Combine(root, "retroarch", "mGBA", "Emerald.srm");

    /// <summary>melonDS with Platinum and RetroArch with Emerald, set up and scanned.</summary>
    async Task<PendingChanges> Start()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EmeraldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(PlatinumPath)!);
        File.WriteAllBytes(PlatinumPath, Fixture("platinum-us"));
        File.WriteAllBytes(EmeraldPath, Fixture("emerald"));
        var setup = new Setup(platform);
        platform.NextPick = new PickedLocation(Path.Combine(root, "melonds"), "melonds");
        await setup.GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);
        platform.NextPick = new PickedLocation(Path.Combine(root, "retroarch"), "retroarch");
        await setup.GiveAccessAsync(RetroArch, RetroArch.Folder("saves")!);
        saves = new MySaves(setup, platform.Storage);
        saves.Scan(Now);
        return new PendingChanges(platform.Storage);
    }

    OpenedSave Opened(GameVersion game) => saves.Open(saves.Games.Single(g => g.Save.Game == game));

    static SaveFile Read(string path) => SaveDetector.Detect(File.ReadAllBytes(path))!.Save;

    [Fact]
    public async Task A_save_seen_for_the_first_time_joins_the_bank_only_with_a_saved_change()
    {
        var pending = await Start();

        var platinum = pending.Open(Opened(GameVersion.Pt));
        Assert.Empty(pending.Bank.Saves);
        Assert.Equal(platinum, pending.Open(Opened(GameVersion.Pt))); // the same save again

        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 1), NewId: Guid.NewGuid()), Now).Succeeded);
        Assert.Equal($"Platinum ({Read(PlatinumPath).OT})", Assert.Single(pending.Bank.Saves).DisplayName);
        Assert.False(platform.Storage.BankFile.Exists);

        Assert.True(pending.Save(Now).Written);
        var stored = Assert.Single(platform.Storage.Bank.Load().Saves);
        Assert.Equal(platinum, stored.Id);
        Assert.Equal(SaveHash.Of(File.ReadAllBytes(PlatinumPath)), stored.LastSeenHash);
    }

    [Fact]
    public async Task One_save_moves_a_pokemon_from_one_game_through_the_bank_into_another()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        var emerald = pending.Open(Opened(GameVersion.E));
        var kirlia = Guid.NewGuid();

        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(null, 1), new BankSlot(1, 1), NewId: kirlia), Now).Succeeded);
        Assert.Equal(5, pending.Save(platinum).PartyCount); // shown as the changes leave it
        Assert.True(pending.Stage(new WithdrawChange(kirlia, emerald, new SaveSlot(1, 2)), Now).Succeeded);
        Assert.Equal((ushort)Species.Kirlia, pending.Save(emerald).GetBoxSlotAtIndex(1).Species);
        Assert.Equal((ushort)Species.Kirlia, Read(PlatinumPath).GetPartySlotAtIndex(0).Species); // nothing written yet

        var result = pending.Save(Now);

        Assert.True(result.Written, result.Refusal);
        Assert.True(pending.IsEmpty);
        Assert.Equal(5, Read(PlatinumPath).PartyCount);
        Assert.Equal((ushort)Species.Kirlia, Read(EmeraldPath).GetBoxSlotAtIndex(1).Species);
        var bank = platform.Storage.Bank.Load();
        Assert.Equal(2, bank.Saves.Count);
        Assert.Equal(new CheckOut(emerald, new SaveSlot(1, 2)), Assert.Single(bank.Pokemon).CheckedOut);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(platform.Storage.Backups.Root, "saves")).Length);
    }

    [Fact]
    public async Task After_saving_the_next_changes_start_from_the_saves_as_written()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 1), NewId: Guid.NewGuid()), Now);
        pending.Save(Now);

        Assert.True(pending.Stage(new ImportChange(platinum, new SaveSlot(1, 2), new BankSlot(1, 2), NewId: Guid.NewGuid()), Now).Succeeded);
        Assert.True(pending.Save(Now).Written);

        Assert.Equal(2, platform.Storage.Bank.Load().Pokemon.Count);
        Assert.Equal(0, Read(PlatinumPath).GetBoxSlotAtIndex(0).Species);
        Assert.Equal(0, Read(PlatinumPath).GetBoxSlotAtIndex(1).Species);
    }

    [Fact]
    public async Task A_pokemon_too_new_for_the_game_is_refused_and_not_staged()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        var emerald = pending.Open(Opened(GameVersion.E));
        var buneary = Guid.NewGuid(); // first seen in Gen 4: no place in Emerald
        pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 1), NewId: buneary), Now);

        var result = pending.Stage(new WithdrawChange(buneary, emerald, new SaveSlot(1, 2)), Now);

        Assert.False(result.Succeeded);
        Assert.Contains("Buneary", result.Refusal, StringComparison.Ordinal);
        Assert.Single(pending.Outcomes);
    }

    [Fact]
    public async Task A_save_played_while_changes_to_it_were_pending_is_refused()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 1), NewId: Guid.NewGuid()), Now);

        File.WriteAllBytes(PlatinumPath, Fixture("platinum-us").Select((b, i) => i == 0x100 ? (byte)(b ^ 1) : b).ToArray());
        File.SetLastWriteTimeUtc(PlatinumPath, DateTime.UtcNow.AddMinutes(5));

        var result = pending.Save(Now);
        Assert.False(result.Written);
        Assert.Contains("changed since it was opened", result.Refusal, StringComparison.Ordinal);
        Assert.False(pending.IsEmpty); // nothing lost
        var ex = Assert.Throws<InvalidOperationException>(() => pending.Open(Opened(GameVersion.Pt)));
        Assert.Contains("changed on disk while changes to it were pending", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_bank_saves_with_the_same_game_and_trainer_cant_be_told_apart()
    {
        var pending = await Start();
        var detected = Opened(GameVersion.Pt).Save;
        var trainer = new TrainerInfo(detected.Trainer, detected.Save.TID16, detected.Save.SID16);
        var bank = new Bank();
        bank.Saves.Add(new BankSave(Guid.NewGuid(), detected.Game, trainer, "Platinum (Thor)", null));
        bank.Saves.Add(new BankSave(Guid.NewGuid(), detected.Game, trainer, "Platinum (PC)", null));
        platform.Storage.Bank.Save(bank);

        var ex = Assert.Throws<InvalidOperationException>(() => new PendingChanges(platform.Storage).Open(Opened(GameVersion.Pt)));

        Assert.Contains("can't tell which one", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_the_bank_knows_is_matched_by_game_and_trainer()
    {
        var pending = await Start();
        var detected = Opened(GameVersion.Pt).Save;
        var known = new BankSave(Guid.NewGuid(), detected.Game, new TrainerInfo(detected.Trainer, detected.Save.TID16, detected.Save.SID16), "Platinum", null);
        var bank = new Bank();
        bank.Saves.Add(known);
        platform.Storage.Bank.Save(bank);

        Assert.Equal(known.Id, new PendingChanges(platform.Storage).Open(Opened(GameVersion.Pt)));
    }

    [Fact]
    public async Task A_withdraw_back_into_a_game_can_take_the_moves_it_had_there()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        var emerald = pending.Open(Opened(GameVersion.E));
        var kirlia = Guid.NewGuid();

        // Platinum → Emerald, where Kirlia loses Lucky Chant; then back to the bank.
        pending.Stage(new ImportChange(platinum, new SaveSlot(null, 1), new BankSlot(1, 1), NewId: kirlia), Now);
        pending.Stage(new WithdrawChange(kirlia, emerald, new SaveSlot(1, 2)), Now);
        Assert.Contains("Gen 4 moves", pending.Outcomes[1].Kept, StringComparison.Ordinal);
        Assert.True(pending.Save(Now).Written);
        Assert.True(pending.Stage(new ImportChange(emerald, new SaveSlot(1, 2)), Now.AddHours(1)).Succeeded);
        Assert.True(pending.Save(Now.AddHours(1)).Written);

        // Into Platinum: both movesets are offered, and the newest is the default.
        var staged = pending.Stage(new WithdrawChange(kirlia, platinum), Now.AddHours(2));
        Assert.True(staged.Succeeded, staged.Refusal);
        var offered = Assert.Single(pending.Outcomes);
        Assert.NotNull(offered.Movesets?.LastInThisGame);
        Assert.DoesNotContain((ushort)Move.LuckyChant, new[] { offered.Movesets!.Newest.Move1, offered.Movesets.Newest.Move2, offered.Movesets.Newest.Move3, offered.Movesets.Newest.Move4 });

        Assert.Null(pending.ChooseMoves(0, MovesetChoice.LastInThisGame, Now.AddHours(2)));

        var chosen = Assert.Single(pending.Outcomes);
        Assert.Equal(MovesetChoice.LastInThisGame, Assert.IsType<WithdrawChange>(chosen.Change).Moves);
        Assert.DoesNotContain(chosen.Notes, n => n.Field == ChangedField.Moves);
        Assert.True(pending.Save(Now.AddHours(2)).Written);
        var slot = Assert.IsType<CheckOut>(Assert.Single(platform.Storage.Bank.Load().Pokemon).CheckedOut).Slot;
        var home = Read(PlatinumPath).GetBoxSlotAtIndex((slot.Box!.Value - 1) * 30 + slot.Slot - 1);
        Assert.Equal(offered.Movesets!.LastInThisGame, Rhome.Core.Moveset.Of(home));
        Assert.Contains((ushort)Move.LuckyChant, home.Moves);
    }

    [Fact]
    public async Task A_change_a_later_one_needs_cant_be_undone_on_its_own()
    {
        var pending = await Start();
        var platinum = pending.Open(Opened(GameVersion.Pt));
        var emerald = pending.Open(Opened(GameVersion.E));
        var kirlia = Guid.NewGuid();
        pending.Stage(new ImportChange(platinum, new SaveSlot(null, 1), new BankSlot(1, 1), NewId: kirlia), Now);
        pending.Stage(new ImportChange(platinum, new SaveSlot(1, 1), new BankSlot(1, 2), NewId: Guid.NewGuid()), Now);
        pending.Stage(new WithdrawChange(kirlia, emerald, new SaveSlot(1, 2)), Now);

        var refusal = pending.Undo(0, Now); // the withdraw needs Kirlia in the bank

        Assert.StartsWith("Can't undo that on its own", refusal, StringComparison.Ordinal);
        Assert.Equal(3, pending.Outcomes.Count);

        Assert.Null(pending.Undo(1, Now)); // the other import can go
        Assert.Equal([typeof(ImportChange), typeof(WithdrawChange)], pending.Outcomes.Select(o => o.Change.GetType()));
        Assert.Null(pending.Bank.At(new BankSlot(1, 2)));
        Assert.NotEqual(0, pending.Save(platinum).GetBoxSlotAtIndex(0).Species); // back in Platinum's box, as the changes leave it
    }
}
