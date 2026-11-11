using PKHeX.Core;

namespace Rhome.Core.Tests;

/// <summary>The scan of granted folders in first-run setup (issue #42).</summary>
public sealed class SaveScanTests
{
    static readonly DateTimeOffset Day = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    static ScanFolder Folder(KnownEmulator emulator, string key, MemoryFolder access) =>
        new(emulator, emulator.Folder(key)!, access);

    [Fact]
    public void Saves_are_found_and_identified_from_their_bytes()
    {
        var saves = new MemoryFolder("saves")
            .Add("mGBA/Pokemon Emerald.srm", Fixtures.Bytes("emerald"))
            .Add("mGBA/Pokemon Emerald.state", Fixtures.Bytes("emerald")) // not a save name: not even read
            .Add("readme.txt", [1, 2, 3]);

        var result = SaveScan.Run([Folder(RetroArch, "saves", saves)]);

        var game = Assert.Single(result.Games);
        Assert.Equal(GameVersion.E, game.Save.Game);
        Assert.Equal("mGBA/Pokemon Emerald.srm", game.Loaded.Entry.RelativePath);
        Assert.Equal(LoadCertainty.OnlyCopy, game.Certainty);
        Assert.Equal(["mGBA/Pokemon Emerald.srm"], saves.Opened);

        var report = Assert.Single(result.Folders);
        Assert.Equal(FolderScanState.Scanned, report.State);
        Assert.Equal((3, 1), (report.Files, report.Saves));
    }

    [Fact]
    public void Play_time_and_trainer_come_from_the_save()
    {
        var detected = SaveDetector.Detect(Fixtures.Bytes("platinum-us"))!;
        var saves = new MemoryFolder("saves").Add("Platinum.sav", Fixtures.Bytes("platinum-us"));

        var game = Assert.Single(SaveScan.Run([Folder(MelonDS, "app", saves)]).Games);

        Assert.Equal(detected.Trainer, game.Save.Trainer);
        Assert.Equal($"{detected.Save.PlayedHours}:{detected.Save.PlayedMinutes:00}", game.Save.PlayTime);
        Assert.True(game.Save.IsSupported);
    }

    [Fact]
    public void An_empty_copy_next_to_the_rom_is_the_one_melonds_loads()
    {
        // What Spike B found on the Thor: White's real save in melonDS's own folder, and an empty one next to the ROM,
        // which is the file melonDS loads.
        var app = new MemoryFolder("app").Add("Pokemon White.sav", Fixtures.Bytes("white"), Day.AddDays(-2));
        var roms = new MemoryFolder("roms").Add("Pokemon White.sav", [], Day).Add("Pokemon White.nds", new byte[4096]);

        var result = SaveScan.Run([Folder(MelonDS, "app", app), Folder(MelonDS, "roms", roms)]);

        var game = Assert.Single(result.Games);
        Assert.Equal(GameVersion.W, game.Save.Game);
        Assert.Equal(2, game.Copies.Count);
        Assert.Same(roms, game.Loaded.Where.Access);
        Assert.Equal(LoadCertainty.Known, game.Certainty);
        Assert.True(game.LoadedCopyIsNotASave);
    }

    [Fact]
    public void Files_that_arent_pokemon_saves_are_left_out()
    {
        var saves = new MemoryFolder("saves")
            .Add("Zelda.sav", new byte[0x20000])
            .Add("Empty.sav", []);

        var result = SaveScan.Run([Folder(MelonDS, "app", saves)]);

        Assert.Empty(result.Games);
        Assert.Equal(0, Assert.Single(result.Folders).Saves);
    }

    [Fact]
    public void Only_folders_as_deep_as_the_emulator_keeps_saves_are_used()
    {
        var saves = new MemoryFolder("app").Add("old/Platinum.sav", Fixtures.Bytes("platinum-us"));

        Assert.Empty(SaveScan.Run([Folder(MelonDS, "app", saves)]).Games);
    }

    [Fact]
    public void A_folder_that_cant_be_reached_is_reported_and_the_rest_still_scanned()
    {
        var gone = new MemoryFolder("roms") { Available = false };
        var broken = new MemoryFolder("saves") { ListFails = new UnauthorizedAccessException("access revoked") };
        var app = new MemoryFolder("app").Add("Platinum.sav", Fixtures.Bytes("platinum-us"));

        var result = SaveScan.Run([Folder(MelonDS, "roms", gone), Folder(RetroArch, "saves", broken), Folder(MelonDS, "app", app)]);

        Assert.Equal([FolderScanState.Unreachable, FolderScanState.Failed, FolderScanState.Scanned], result.Folders.Select(f => f.State));
        Assert.Equal("access revoked", result.Folders[1].Error);
        Assert.Single(result.Games);
    }

    [Fact]
    public void A_file_that_cant_be_read_is_a_problem_not_a_failure()
    {
        var saves = new MemoryFolder("app")
            .Add("Platinum.sav", Fixtures.Bytes("platinum-us"))
            .Add("White.sav", Fixtures.Bytes("white"));
        saves.ReadFails["White.sav"] = new IOException("I/O error");

        var result = SaveScan.Run([Folder(MelonDS, "app", saves)]);

        Assert.Equal(GameVersion.Pt, Assert.Single(result.Games).Save.Game);
        Assert.Contains("White.sav", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_picked_by_hand_is_a_game_of_its_own_whatever_its_name()
    {
        var picked = new MemoryFolder("Download/backup.bin").Add("backup.bin", Fixtures.Bytes("emerald"));

        var result = SaveScan.Run([new ScanFolder(null, null, picked)]);

        var game = Assert.Single(result.Games);
        Assert.Equal(GameVersion.E, game.Save.Game);
        Assert.Equal("Download/backup.bin", game.Loaded.Where.Name);
    }

    [Fact]
    public void Games_are_listed_by_generation()
    {
        var saves = new MemoryFolder("app")
            .Add("White.sav", Fixtures.Bytes("white"))
            .Add("Platinum.sav", Fixtures.Bytes("platinum-us"));
        var srm = new MemoryFolder("saves").Add("Emerald.srm", Fixtures.Bytes("emerald"));

        var result = SaveScan.Run([Folder(MelonDS, "app", saves), Folder(RetroArch, "saves", srm)]);

        Assert.Equal([GameVersion.E, GameVersion.Pt, GameVersion.W], result.Games.Select(g => g.Save.Game));
    }
}

/// <summary>A folder of saves in memory. File IDs are their relative paths.</summary>
sealed class MemoryFolder(string name) : ISaveFolder
{
    readonly SortedDictionary<string, (byte[] Bytes, DateTimeOffset Modified)> files = new(StringComparer.Ordinal);

    public bool Available { get; set; } = true;
    public Exception? ListFails { get; set; }
    public Dictionary<string, Exception> ReadFails { get; } = [];
    public List<string> Opened { get; } = [];

    public string DisplayName => name;

    public MemoryFolder Add(string path, byte[] bytes, DateTimeOffset? modified = null)
    {
        files[path] = (bytes, modified ?? DateTimeOffset.UnixEpoch);
        return this;
    }

    public bool IsAvailable() => Available;

    public IReadOnlyList<FolderEntry> List(int maxDepth)
    {
        if (ListFails is { } e)
            throw e;
        return files
            .Where(f => f.Key.Count(c => c == '/') <= maxDepth)
            .Select(f => new FolderEntry(f.Key, f.Key, f.Value.Bytes.Length, f.Value.Modified))
            .ToList();
    }

    public ISaveLocation Open(string id)
    {
        Opened.Add(id);
        if (ReadFails.TryGetValue(id, out var e))
            throw e;
        return new MemorySave(files[id].Bytes);
    }
}
