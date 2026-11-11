using PKHeX.Core;

namespace Rhome.Core.Tests;

/// <summary>The launch check of known saves (issue #43): only the files it knows, never a folder listing.</summary>
public sealed class SaveCheckTests
{
    static readonly DateTimeOffset Day = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    static ScanFolder Folder(KnownEmulator emulator, string key, MemoryFolder access) =>
        new(emulator, emulator.Folder(key)!, access);

    /// <summary>What a first scan found: the files the next launch checks.</summary>
    static List<ScannedFile> Scan(params ScanFolder[] folders) =>
        SaveScan.Run(folders).Games.SelectMany(g => g.Copies).ToList();

    [Fact]
    public void Unchanged_saves_are_neither_listed_nor_read()
    {
        var app = new MemoryFolder("app")
            .Add("Platinum.sav", Fixtures.Bytes("platinum-us"), Day)
            .Add("White.sav", Fixtures.Bytes("white"), Day);
        var known = Scan(Folder(MelonDS, "app", app));
        app.Opened.Clear();

        var result = SaveCheck.Run(known);

        Assert.All(result.Files, f => Assert.Equal(SaveChange.Same, f.Change));
        Assert.Equal([GameVersion.Pt, GameVersion.W], result.Games.Select(g => g.Save.Game));
        Assert.Equal(1, app.Listed); // the first scan's only
        Assert.Empty(app.Opened);
        Assert.False(result.AnythingChanged);
    }

    [Fact]
    public void A_save_that_changed_is_read_again()
    {
        var app = new MemoryFolder("app").Add("Platinum.sav", Fixtures.Bytes("platinum-us"), Day);
        var known = Scan(Folder(MelonDS, "app", app));
        app.Add("Platinum.sav", Fixtures.Bytes("platinum-eu"), Day.AddHours(1)); // played and saved
        app.Opened.Clear();

        var result = SaveCheck.Run(known);

        var file = Assert.Single(result.Files);
        Assert.Equal(SaveChange.Changed, file.Change);
        Assert.Equal(Day.AddHours(1), file.File.Entry.LastModified);
        Assert.Equal(SaveDetector.Detect(Fixtures.Bytes("platinum-eu"))!.Trainer, Assert.Single(result.Games).Save.Trainer);
        Assert.Equal(["Platinum.sav"], app.Opened);
        Assert.True(result.AnythingChanged);
    }

    [Fact]
    public void A_save_that_went_is_gone_and_leaves_the_list()
    {
        var app = new MemoryFolder("app")
            .Add("Platinum.sav", Fixtures.Bytes("platinum-us"))
            .Add("White.sav", Fixtures.Bytes("white"));
        var known = Scan(Folder(MelonDS, "app", app));
        app.Remove("White.sav");

        var result = SaveCheck.Run(known);

        Assert.Equal("White.sav", Assert.Single(result.In(SaveChange.Gone)).Entry.RelativePath);
        Assert.Equal(GameVersion.Pt, Assert.Single(result.Games).Save.Game);
    }

    [Fact]
    public void Saves_in_a_folder_that_cant_be_reached_stay_as_they_were()
    {
        var card = new MemoryFolder("roms").Add("Emerald.srm", Fixtures.Bytes("emerald"));
        var known = Scan(Folder(RetroArch, "saves", card));
        card.Available = false; // SD card out

        var result = SaveCheck.Run(known);

        Assert.Equal(SaveChange.Unreachable, Assert.Single(result.Files).Change);
        Assert.Equal(GameVersion.E, Assert.Single(result.Games).Save.Game);
        Assert.False(result.AnythingChanged);
    }

    [Fact]
    public void When_the_copy_melonds_loads_goes_the_other_copy_is_loaded()
    {
        // Spike B's White case: an empty copy next to the ROM wins over the real one, until it's deleted.
        var app = new MemoryFolder("app").Add("Pokemon White.sav", Fixtures.Bytes("white"), Day.AddDays(-2));
        var roms = new MemoryFolder("roms").Add("Pokemon White.sav", [], Day);
        var known = Scan(Folder(MelonDS, "app", app), Folder(MelonDS, "roms", roms));
        roms.Remove("Pokemon White.sav");

        var game = Assert.Single(SaveCheck.Run(known).Games);

        Assert.Same(app, game.Loaded.Where.Access);
        Assert.False(game.HasSeveralCopies);
        Assert.False(game.LoadedCopyIsNotASave);
    }

    [Fact]
    public void An_empty_copy_that_now_holds_the_save_is_read()
    {
        var app = new MemoryFolder("app").Add("Pokemon White.sav", Fixtures.Bytes("white"), Day.AddDays(-2));
        var roms = new MemoryFolder("roms").Add("Pokemon White.sav", [], Day);
        var known = Scan(Folder(MelonDS, "app", app), Folder(MelonDS, "roms", roms));
        roms.Add("Pokemon White.sav", Fixtures.Bytes("white"), Day.AddHours(1)); // the game saved over the empty file

        var game = Assert.Single(SaveCheck.Run(known).Games);

        Assert.Same(roms, game.Loaded.Where.Access);
        Assert.False(game.LoadedCopyIsNotASave);
    }

    [Fact]
    public void A_changed_save_that_cant_be_read_keeps_what_was_known_and_is_tried_again()
    {
        var app = new MemoryFolder("app").Add("Platinum.sav", Fixtures.Bytes("platinum-us"), Day);
        var known = Scan(Folder(MelonDS, "app", app));
        app.Add("Platinum.sav", Fixtures.Bytes("platinum-us"), Day.AddHours(1));
        app.ReadFails["Platinum.sav"] = new IOException("I/O error");

        var result = SaveCheck.Run(known);

        var file = Assert.Single(result.Files);
        Assert.Equal(SaveChange.Unreachable, file.Change);
        Assert.Equal(Day, file.File.Entry.LastModified); // the old time, so the next check reads it again
        Assert.Equal(GameVersion.Pt, Assert.Single(result.Games).Save.Game);
        Assert.Contains("I/O error", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_cant_be_checked_is_a_problem_not_a_failure()
    {
        var app = new MemoryFolder("app").Add("Platinum.sav", Fixtures.Bytes("platinum-us"));
        var known = Scan(Folder(MelonDS, "app", app));
        app.StatFails = new UnauthorizedAccessException("access revoked");

        var result = SaveCheck.Run(known);

        Assert.Equal(SaveChange.Unreachable, Assert.Single(result.Files).Change);
        Assert.Contains("access revoked", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_folder_is_asked_once_whether_it_can_be_reached()
    {
        var app = new MemoryFolder("app")
            .Add("Platinum.sav", Fixtures.Bytes("platinum-us"))
            .Add("White.sav", Fixtures.Bytes("white"))
            .Add("Diamond.sav", Fixtures.Bytes("diamond"));
        var known = Scan(Folder(MelonDS, "app", app));
        app.AvailableChecks = 0;

        SaveCheck.Run(known);

        Assert.Equal(1, app.AvailableChecks);
    }
}
