using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Tests;

/// <summary>My saves (issue #43): the stored list, the launch check, Scan now and opening a save, on real folders.</summary>
public sealed class MySavesTests : IDisposable
{
    static readonly DateTime Day = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;
    readonly FakePlatform platform;

    public MySavesTests() => platform = new FakePlatform(Path.Combine(root, "app"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav"));

    /// <summary>Writes a save at a set time, so a change is seen whatever the file system's clock resolution.</summary>
    string Put(string folder, string file, byte[] bytes, DateTime when)
    {
        var path = Path.Combine(root, folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, when);
        return path;
    }

    async Task<Setup> SetUp(params (KnownEmulator Emulator, string Folder, string Path)[] folders)
    {
        var setup = new Setup(platform);
        foreach (var (emulator, key, path) in folders)
        {
            Directory.CreateDirectory(Path.Combine(root, path));
            platform.NextPick = new PickedLocation(Path.Combine(root, path), path);
            await setup.GiveAccessAsync(emulator, emulator.Folder(key)!);
        }
        return setup;
    }

    /// <summary>The app starting again: everything read back from app storage.</summary>
    MySaves Restart() => new(new Setup(platform), platform.Storage);

    [Fact]
    public async Task A_scan_is_kept_so_the_next_launch_knows_the_saves_without_scanning()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        Put("retroarch", "mGBA/Emerald.srm", Fixture("emerald"), Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds"), (RetroArch, "saves", "retroarch")), platform.Storage);
        Assert.Null(saves.ScannedAt);

        saves.Scan(new DateTimeOffset(Day));

        var again = Restart();
        Assert.Equal(new DateTimeOffset(Day), again.ScannedAt);
        Assert.Equal([GameVersion.E, GameVersion.Pt], again.Games.Select(g => g.Save.Game));
        Assert.True(again.Setup.State.Done);
    }

    [Fact]
    public async Task The_launch_check_reads_a_changed_save_again_and_keeps_it()
    {
        var path = Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        new MySaves(await SetUp((MelonDS, "app", "melonds")), platform.Storage).Scan(DateTimeOffset.Now);
        Put("melonds", "Platinum.sav", Fixture("platinum-eu"), Day.AddHours(1));

        var result = Restart().Check();

        Assert.Equal(path, Assert.Single(result.In(SaveChange.Changed)).Entry.Id);
        var trainer = SaveDetector.Detect(Fixture("platinum-eu"))!.Trainer;
        Assert.Equal(trainer, Assert.Single(Restart().Games).Save.Trainer); // stored, not just shown
    }

    [Fact]
    public async Task The_launch_check_forgets_a_save_that_went()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        var white = Put("melonds", "White.sav", Fixture("white"), Day);
        new MySaves(await SetUp((MelonDS, "app", "melonds")), platform.Storage).Scan(DateTimeOffset.Now);
        File.Delete(white);

        var result = Restart().Check();

        Assert.Equal(white, Assert.Single(result.In(SaveChange.Gone)).Entry.Id);
        Assert.Equal(GameVersion.Pt, Assert.Single(Restart().Games).Save.Game);
    }

    [Fact]
    public async Task A_new_save_shows_up_with_scan_now_not_with_the_launch_check()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds")), platform.Storage);
        saves.Scan(DateTimeOffset.Now);
        Put("melonds", "White.sav", Fixture("white"), Day);

        Assert.Single(saves.Check().Games);
        Assert.Equal(2, saves.Scan(DateTimeOffset.Now).Games.Count);
        Assert.Equal(2, Restart().Games.Count);
    }

    [Fact]
    public async Task Scan_now_keeps_the_saves_of_a_folder_it_cant_reach()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        Put("card", "mGBA/Emerald.srm", Fixture("emerald"), Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds"), (RetroArch, "saves", "card")), platform.Storage);
        saves.Scan(DateTimeOffset.Now);
        Directory.Move(Path.Combine(root, "card"), Path.Combine(root, "card-out")); // SD card out

        var result = saves.Scan(DateTimeOffset.Now);

        Assert.Equal(GameVersion.Pt, Assert.Single(result.Games).Save.Game);
        Assert.Equal([GameVersion.E, GameVersion.Pt], saves.Games.Select(g => g.Save.Game));
        Assert.Equal(SaveChange.Unreachable, Restart().Check().Files.Single(f => f.File.Save!.Game == GameVersion.E).Change);
    }

    [Fact]
    public async Task Saves_of_a_folder_no_longer_set_up_are_forgotten()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        Put("retroarch", "mGBA/Emerald.srm", Fixture("emerald"), Day);
        var setup = await SetUp((MelonDS, "app", "melonds"), (RetroArch, "saves", "retroarch"));
        new MySaves(setup, platform.Storage).Scan(DateTimeOffset.Now);
        setup.Remove(setup.State.Sources.Single(s => s.Emulator == "retroarch"));

        var saves = Restart();
        saves.Check();

        Assert.Equal(GameVersion.Pt, Assert.Single(saves.Games).Save.Game);
        Assert.Single(Restart().Games);
    }

    [Fact]
    public async Task Opening_reads_the_save_as_it_is_on_disk_now()
    {
        Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds")), platform.Storage);
        saves.Scan(DateTimeOffset.Now);
        var row = Assert.Single(saves.Games);

        var unchanged = saves.Open(row);
        Assert.False(unchanged.ReadAgain);
        Assert.Equal(Fixture("platinum-us"), unchanged.Bytes);

        Put("melonds", "Platinum.sav", Fixture("platinum-eu"), Day.AddHours(1)); // played since the list was drawn
        var opened = saves.Open(row);

        Assert.True(opened.ReadAgain);
        Assert.Equal(Fixture("platinum-eu"), opened.Bytes);
        Assert.Equal(opened.Save.Trainer, opened.Game.Save.Trainer);
        Assert.Equal(opened.Save.Trainer, Assert.Single(saves.Games).Save.Trainer);
        Assert.Equal(opened.Save.Trainer, Assert.Single(Restart().Games).Save.Trainer);
    }

    [Fact]
    public async Task A_save_that_went_doesnt_open()
    {
        var path = Put("melonds", "Platinum.sav", Fixture("platinum-us"), Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds")), platform.Storage);
        saves.Scan(DateTimeOffset.Now);
        File.Delete(path);

        var ex = Assert.Throws<InvalidOperationException>(() => saves.Open(saves.Games.Single()));

        Assert.Contains("Scan now", ex.Message, StringComparison.Ordinal);
        Assert.Empty(saves.Games);
    }

    [Fact]
    public async Task An_empty_copy_the_emulator_loads_doesnt_open()
    {
        Put("melonds", "Pokemon White.sav", Fixture("white"), Day.AddDays(-2));
        Put("roms", "Pokemon White.sav", [], Day);
        var saves = new MySaves(await SetUp((MelonDS, "app", "melonds"), (MelonDS, "roms", "roms")), platform.Storage);
        saves.Scan(DateTimeOffset.Now);
        var game = Assert.Single(saves.Games);
        Assert.Equal(2, game.Copies.Count);

        var ex = Assert.Throws<InvalidOperationException>(() => saves.Open(game));

        Assert.Contains("isn't a save", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_saves_file_starts_the_list_empty()
    {
        Directory.CreateDirectory(platform.Storage.Root);
        File.WriteAllText(platform.Storage.SavesFile.Path, "{ not json");

        var saves = Restart();

        Assert.Empty(saves.Games);
        Assert.Null(saves.ScannedAt);
        Assert.NotNull(saves.LoadProblem);
    }
}
