using System.Text;
using Rhome.Core;

namespace Rhome.Core.Tests;

public class KnownEmulatorsTests
{
    static readonly DateTimeOffset Day = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    static EmulatorSaveFile File(KnownEmulator emulator, string folder, string path, long size = 512 * 1024, int day = 0) =>
        new(emulator, emulator.Folder(folder)!, path, size, Day.AddDays(day));

    static IReadOnlyList<KnownEmulator> Parse(string json) =>
        KnownEmulators.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static GameSaves GameNamed(IReadOnlyList<GameSaves> games, string name) =>
        Assert.Single(games, g => g.Loaded.GameName == name);

    // --- The built-in list ---

    [Fact]
    public void The_built_in_list_has_the_MVP_emulators()
    {
        Assert.Equal(["melonds", "retroarch"], KnownEmulators.All.Select(e => e.Key));
    }

    [Fact]
    public void Installed_apps_are_found_by_Android_package()
    {
        Assert.Same(MelonDS, KnownEmulators.ForApp(Platform.Android, "me.magnum.melonds"));
        Assert.Same(RetroArch, KnownEmulators.ForApp(Platform.Android, "com.retroarch"));
        Assert.Null(KnownEmulators.ForApp(Platform.Android, "dev.eden.eden_emulator"));
        Assert.Null(KnownEmulators.ForApp(Platform.Linux, "me.magnum.melonds"));
    }

    [Fact]
    public void MelonDS_has_its_own_folder_and_the_ROM_folder_and_loads_the_save_next_to_the_ROM()
    {
        var folders = MelonDS.FoldersOn(Platform.Android).ToList();
        Assert.Equal(["app", "roms"], folders.Select(f => f.Key));
        Assert.Equal("Android/data/me.magnum.melonds/files/saves", folders[0].Path);
        Assert.Equal(StorageArea.Shared, folders[0].Area);
        Assert.True(folders[1].RomFolder);
        Assert.Equal(StorageArea.SdCard, folders[1].Area);
        Assert.Equal(["roms", "app"], MelonDS.LoadOrder);
    }

    [Fact]
    public void RetroArch_keeps_srm_saves_in_its_media_folder()
    {
        var folder = Assert.Single(RetroArch.FoldersOn(Platform.Android));
        Assert.Equal("Android/media/com.retroarch/RetroArch/saves", folder.Path);
        Assert.Equal([".srm"], RetroArch.SaveExtensions);
    }

    // --- Which files are saves ---

    [Theory]
    [InlineData("Pokemon - Platinum Version (USA) (Rev 1).sav", true)]
    [InlineData("POKEMON W.SAV", true)]
    [InlineData("Pokemon - Platinum Version (USA) (Rev 1).ml1", false)] // a save state
    [InlineData("Pokemon - Platinum Version (USA) (Rev 1).nds", false)]
    [InlineData("Pokemon - Platinum Version (USA) (Rev 1).zip", false)]
    [InlineData(".sav", false)]
    [InlineData("._Pokemon White.sav", false)]
    [InlineData("old/Pokemon White.sav", false)] // melonDS doesn't look in subfolders
    public void MelonDS_saves_are_sav_files_in_the_folder_itself(string path, bool isSave)
    {
        Assert.Equal(isSave, MelonDS.IsSaveFile(MelonDS.Folder("app")!, path));
    }

    [Theory]
    [InlineData("Pokemon - Emerald Version (USA, Europe).srm", true)]
    [InlineData("mGBA/Pokemon - Emerald Version (USA, Europe).srm", true)]
    [InlineData("mGBA/gba/Pokemon - Emerald Version (USA, Europe).srm", true)]
    [InlineData("a/b/c/Pokemon - Emerald Version (USA, Europe).srm", false)]
    [InlineData("mGBA/Pokemon - Emerald Version (USA, Europe).sav", false)]
    [InlineData("mGBA/../Pokemon Emerald.srm", false)]
    [InlineData("mGBA//Pokemon Emerald.srm", false)]
    public void RetroArch_saves_are_srm_files_up_to_two_folders_down(string path, bool isSave)
    {
        Assert.Equal(isSave, RetroArch.IsSaveFile(RetroArch.Folder("saves")!, path));
    }

    // --- Sample folder layouts ---

    /// <summary>The Thor as Spike B found it on 2026-11-06, before the saves were copied next to the ROMs.</summary>
    static List<EmulatorSaveFile> ThorLayout()
    {
        var files = new List<EmulatorSaveFile>
        {
            // melonDS's own folder: the real runs, and save states.
            File(MelonDS, "app", "Pokemon - Platinum Version (USA) (Rev 1).sav", day: -10),
            File(MelonDS, "app", "Pokemon - Platinum Version (USA) (Rev 1).ml1", day: -10),
            File(MelonDS, "app", "Pokemon - White Version (USA, Europe).sav", day: -5),
            File(MelonDS, "app", "Pokemon - Platinum Version (Europe).sav", day: -20),

            // Next to the ROMs: an old Platinum start, and the empty White save melonDS made after its grant.
            File(MelonDS, "roms", "Pokemon - Platinum Version (USA) (Rev 1).sav", day: -28),
            File(MelonDS, "roms", "Pokemon - White Version (USA, Europe).sav", size: 0, day: -3),

            // RetroArch, sorted by core.
            File(RetroArch, "saves", "mGBA/Pokemon - Emerald Version (USA, Europe).srm", size: 128 * 1024),
            File(RetroArch, "saves", "mGBA/Pokemon - Emerald Version (USA, Europe).state", size: 300 * 1024),
            File(RetroArch, "saves", "mGBA/Pokemon - Blue Version (USA, Europe).srm", size: 32 * 1024),
        };
        // The ROM folder is mostly ROMs.
        for (int i = 0; i < 200; i++)
            files.Add(File(MelonDS, "roms", $"Game {i:000} (USA).zip", size: 64L << 20));
        return files;
    }

    [Fact]
    public void A_sample_layout_resolves_to_the_saves_in_it()
    {
        var games = SaveCopies.Resolve(ThorLayout());

        Assert.Equal(
            [
                "Pokemon - Blue Version (USA, Europe)",
                "Pokemon - Emerald Version (USA, Europe)",
                "Pokemon - Platinum Version (Europe)",
                "Pokemon - Platinum Version (USA) (Rev 1)",
                "Pokemon - White Version (USA, Europe)",
            ],
            games.Select(g => g.Loaded.GameName).Order(StringComparer.Ordinal));
        Assert.Equal(7, games.Sum(g => g.Copies.Count));
    }

    [Fact]
    public void MelonDS_loads_the_copy_next_to_the_ROM_even_when_it_is_older()
    {
        var platinum = GameNamed(SaveCopies.Resolve(ThorLayout()), "Pokemon - Platinum Version (USA) (Rev 1)");

        Assert.True(platinum.HasSeveralCopies);
        Assert.Equal(LoadCertainty.Known, platinum.Certainty);
        Assert.Equal("roms", platinum.Loaded.Folder.Key);
        Assert.Equal(["roms", "app"], platinum.Copies.Select(c => c.Folder.Key));
    }

    [Fact]
    public void MelonDS_loads_an_empty_save_next_to_the_ROM_over_a_real_one()
    {
        // What happened to White: the game showed New Game, because melonDS read the empty file.
        var white = GameNamed(SaveCopies.Resolve(ThorLayout()), "Pokemon - White Version (USA, Europe)");

        Assert.Equal(LoadCertainty.Known, white.Certainty);
        Assert.Equal("roms", white.Loaded.Folder.Key);
        Assert.Equal(0, white.Loaded.Size);
    }

    [Fact]
    public void A_save_in_one_place_is_the_only_copy()
    {
        var games = SaveCopies.Resolve(ThorLayout());

        foreach (var name in new[] { "Pokemon - Platinum Version (Europe)", "Pokemon - Emerald Version (USA, Europe)" })
        {
            var game = GameNamed(games, name);
            Assert.False(game.HasSeveralCopies);
            Assert.Equal(LoadCertainty.OnlyCopy, game.Certainty);
        }
    }

    [Fact]
    public void Copies_match_by_name_ignoring_case()
    {
        var games = SaveCopies.Resolve(
        [
            File(MelonDS, "app", "pokemon white.sav"),
            File(MelonDS, "roms", "POKEMON WHITE.SAV"),
        ]);

        var white = Assert.Single(games);
        Assert.Equal("roms", white.Loaded.Folder.Key);
    }

    [Fact]
    public void Saves_with_one_name_in_one_folder_stay_separate_games()
    {
        // Same ROM name under two RetroArch cores: which one loads depends on the core, not the folder.
        var games = SaveCopies.Resolve(
        [
            File(RetroArch, "saves", "mGBA/Pokemon Emerald.srm"),
            File(RetroArch, "saves", "gpSP/Pokemon Emerald.srm"),
        ]);

        Assert.Equal(2, games.Count);
        Assert.All(games, g => Assert.Equal(LoadCertainty.OnlyCopy, g.Certainty));
    }

    [Fact]
    public void Saves_with_one_name_for_different_emulators_are_different_games()
    {
        var games = SaveCopies.Resolve(
        [
            File(MelonDS, "app", "Pokemon.sav"),
            File(RetroArch, "saves", "Pokemon.srm"),
        ]);

        Assert.Equal(2, games.Count);
    }

    [Fact]
    public void The_same_file_found_twice_counts_once()
    {
        var file = File(MelonDS, "app", "Pokemon White.sav");
        Assert.Single(Assert.Single(SaveCopies.Resolve([file, file with { }])).Copies);
    }

    // --- Data, not code: other entries work with no new logic ---

    const string TwoFoldersNoLoadOrder = """
        { "emulators": [ {
            "key": "someds", "name": "SomeDS",
            "apps": [ { "platform": "android", "id": "org.example.someds" } ],
            "saveExtensions": [".sav", ".dsv"],
            "folders": [
                { "key": "a", "name": "A", "platform": "android", "area": "shared", "path": "SomeDS/saves" },
                { "key": "b", "name": "B", "platform": "android", "area": "sd-card", "path": "ROMS/nds", "romFolder": true }
            ]
        } ] }
        """;

    [Fact]
    public void Without_a_load_order_the_newest_non_empty_copy_is_a_guess()
    {
        var someDS = Assert.Single(Parse(TwoFoldersNoLoadOrder));
        EmulatorSaveFile At(string folder, long size, int day) => new(someDS, someDS.Folder(folder)!, "Game.dsv", size, Day.AddDays(day));

        var newestEmpty = Assert.Single(SaveCopies.Resolve([At("a", 512, day: 1), At("b", 0, day: 2)]));
        Assert.Equal(LoadCertainty.Guess, newestEmpty.Certainty);
        Assert.Equal("a", newestEmpty.Loaded.Folder.Key);

        var bothReal = Assert.Single(SaveCopies.Resolve([At("a", 512, day: 1), At("b", 512, day: 2)]));
        Assert.Equal(LoadCertainty.Guess, bothReal.Certainty);
        Assert.Equal("b", bothReal.Loaded.Folder.Key);
    }

    [Fact]
    public void Linux_entries_fit_the_same_shape()
    {
        var emulator = Assert.Single(Parse("""
            { "emulators": [ {
                "key": "melonds", "name": "melonDS",
                "apps": [
                    { "platform": "android", "id": "me.magnum.melonds" },
                    { "platform": "linux", "id": "net.kuribo64.melonDS" }
                ],
                "saveExtensions": [".sav"],
                "folders": [
                    { "key": "app", "name": "melonDS's own folder", "platform": "android", "area": "shared", "path": "Android/data/me.magnum.melonds/files/saves" },
                    { "key": "roms-linux", "name": "next to the ROMs", "platform": "linux", "area": "home", "path": "Games/nds", "romFolder": true }
                ],
            } ] }
            """));

        var linux = Assert.Single(emulator.FoldersOn(Platform.Linux));
        Assert.Equal(StorageArea.Home, linux.Area);
        Assert.Equal(["net.kuribo64.melonDS"], emulator.AppIds(Platform.Linux));

        // A Linux save and an Android save with one name aren't copies of each other: they're on different devices.
        var games = SaveCopies.Resolve(
        [
            new(emulator, emulator.Folder("app")!, "Pokemon.sav", 1, Day),
            new(emulator, linux, "Pokemon.sav", 1, Day),
        ]);
        Assert.Equal(2, games.Count);
    }

    // --- A broken list fails here, not on someone's device ---

    [Theory]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [], \"saveExtensions\": [\".sav\"], \"folders\": [] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\"SAV\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"shared\", \"path\": \"x\" } ] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"shared\", \"path\": \"/sdcard/x\" } ] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"shared\", \"path\": \"x\" } ], \"loadOrder\": [\"b\", \"a\"] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"linux\", \"area\": \"home\", \"path\": \"x\" } ] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"shared\", \"path\": \"x\", \"depht\": 1 } ] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"name\": \"X\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"usb\", \"path\": \"x\" } ] } ] }")]
    [InlineData("{ \"emulators\": [ { \"key\": \"x\", \"apps\": [ { \"platform\": \"android\", \"id\": \"x\" } ], \"saveExtensions\": [\".sav\"], \"folders\": [ { \"key\": \"a\", \"name\": \"A\", \"platform\": \"android\", \"area\": \"shared\", \"path\": \"x\" } ] } ] }")]
    [InlineData("{ \"emulators\": null }")]
    [InlineData("not json")]
    public void A_broken_list_is_refused(string json)
    {
        Assert.Throws<FormatException>(() => Parse(json));
    }

    [Fact]
    public void Two_emulators_with_one_key_are_refused()
    {
        const string entry = """{ "key": "x", "name": "X", "apps": [ { "platform": "android", "id": "x" } ], "saveExtensions": [".sav"], "folders": [ { "key": "a", "name": "A", "platform": "android", "area": "shared", "path": "x" } ] }""";
        var ex = Assert.Throws<FormatException>(() => Parse($$"""{ "emulators": [ {{entry}}, {{entry}} ] }"""));
        Assert.Contains("two emulators use the key \"x\"", ex.Message);
    }
}
