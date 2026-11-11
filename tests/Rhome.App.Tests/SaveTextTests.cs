using Rhome.Core;

namespace Rhome.App.Tests;

/// <summary>How a found game reads in the app (issue #42).</summary>
public sealed class SaveTextTests
{
    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;

    static byte[] Fixture(string name) => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav"));

    static ScannedFile File(string folderKey, byte[] bytes)
    {
        var folder = MelonDS.Folder(folderKey)!;
        var summary = SaveDetector.Detect(bytes) is { } d ? SaveSummary.Of(d) : null;
        return new ScannedFile(new ScanFolder(MelonDS, folder, null!), new FolderEntry("id", "Pokemon White.sav", bytes.Length, default), summary);
    }

    [Fact]
    public void One_copy_shows_the_game_trainer_and_where_it_is()
    {
        var copy = File("app", Fixture("white"));
        var game = new FoundGame([copy], copy, LoadCertainty.OnlyCopy);

        Assert.Equal($"White ({copy.Save!.Trainer})", SaveText.Title(game));
        Assert.Equal($"{copy.Save.PlayTime} played · melonDS · melonDS's own folder · Pokemon White.sav", SaveText.Detail(game));
    }

    [Fact]
    public void Two_copies_say_which_one_the_emulator_loads()
    {
        var roms = File("roms", Fixture("white"));
        var app = File("app", Fixture("white"));

        var detail = SaveText.Detail(new FoundGame([roms, app], roms, LoadCertainty.Known));

        Assert.EndsWith("2 copies: melonDS loads the one next to the ROMs.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_loaded_copy_is_a_warning()
    {
        var roms = File("roms", []);
        var app = File("app", Fixture("white"));

        var detail = SaveText.Detail(new FoundGame([roms, app], roms, LoadCertainty.Known));

        Assert.Contains("That copy is empty, so the game won't see the save in melonDS's own folder.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_guess_says_probably()
    {
        var a = File("roms", Fixture("white"));
        var b = File("app", Fixture("white"));

        Assert.Contains("melonDS probably loads", SaveText.Detail(new FoundGame([a, b], a, LoadCertainty.Guess)), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_copy_says_whether_the_emulator_loads_it()
    {
        var roms = File("roms", []);
        var app = File("app", Fixture("white"));
        var game = new FoundGame([roms, app], roms, LoadCertainty.Known);

        Assert.Contains("melonDS · next to the ROMs · Pokemon White.sav · empty", SaveText.Copy(game, roms), StringComparison.Ordinal);
        Assert.EndsWith("not a save (empty?) · the emulator loads this one", SaveText.Copy(game, roms), StringComparison.Ordinal);
        Assert.EndsWith($"{app.Save!.PlayTime} played · the emulator ignores this one", SaveText.Copy(game, app), StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_copy_doesnt_talk_about_loading()
    {
        var app = File("app", Fixture("white"));

        Assert.EndsWith($"{app.Save!.PlayTime} played", SaveText.Copy(new FoundGame([app], app, LoadCertainty.OnlyCopy), app), StringComparison.Ordinal);
    }

    [Fact]
    public void A_gone_save_points_to_scan_now()
    {
        Assert.Equal("Pokemon White.sav is gone from melonDS · melonDS's own folder. If it moved, tap Scan now.",
            SaveText.Gone(File("app", Fixture("white"))));
    }
}
