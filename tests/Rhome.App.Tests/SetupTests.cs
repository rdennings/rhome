using System.Xml.Linq;
using PKHeX.Core;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App.Tests;

/// <summary>First-run setup (issue #42), on real folders with a pretend platform.</summary>
public sealed class SetupTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;
    readonly FakePlatform platform;

    public SetupTests() => platform = new FakePlatform(Path.Combine(root, "app"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    static KnownEmulator MelonDS => KnownEmulators.Find("melonds")!;
    static KnownEmulator RetroArch => KnownEmulators.Find("retroarch")!;

    string Folder(string name, params (string File, string Fixture)[] saves)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        foreach (var (file, fixture) in saves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(path, file))!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture + ".sav"), Path.Combine(path, file));
        }
        return path;
    }

    [Fact]
    public void Only_installed_emulators_are_found()
    {
        platform.Installed.Add("com.retroarch");

        Assert.Equal([RetroArch], new Setup(platform).FindEmulators());
    }

    [Fact]
    public void Every_folder_of_a_found_emulator_is_a_step()
    {
        var steps = new Setup(platform).FolderSteps([MelonDS, RetroArch]);

        Assert.Equal(["melonds/app", "melonds/roms", "retroarch/saves"], steps.Select(s => $"{s.Emulator.Key}/{s.Folder.Key}"));
        Assert.All(steps, s => Assert.False(s.IsDone));
    }

    [Fact]
    public async Task The_picker_starts_in_the_emulators_folder_and_the_pick_is_remembered()
    {
        var saves = Folder("saves");
        platform.NextPick = new PickedLocation(saves, "Internal storage/saves", IsWhereAsked: true);
        var setup = new Setup(platform);

        var line = await setup.GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);

        Assert.Same(MelonDS.Folder("app"), platform.LastStart);
        Assert.Equal("melonDS · melonDS's own folder: access to Internal storage/saves", line);
        Assert.True(setup.FolderSteps([MelonDS])[0].IsDone);
        Assert.Equal(new SaveSource(saves, "melonds", "app", "Internal storage/saves"), Assert.Single(new Setup(platform).State.Sources));
    }

    [Fact]
    public async Task Picking_another_folder_than_the_emulators_says_so()
    {
        platform.NextPick = new PickedLocation(Folder("elsewhere"), "Internal storage/Download", IsWhereAsked: false);

        var line = await new Setup(platform).GiveAccessAsync(RetroArch, RetroArch.Folder("saves")!);

        Assert.Contains("isn't where RetroArch keeps saves (Internal storage/Android/media/com.retroarch/RetroArch/saves)", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_folder_is_right_for_the_roms()
    {
        platform.NextPick = new PickedLocation(Folder("roms"), "SD card/Games/DS", IsWhereAsked: false);

        var line = await new Setup(platform).GiveAccessAsync(MelonDS, MelonDS.Folder("roms")!);

        Assert.Equal("melonDS · next to the ROMs: access to SD card/Games/DS", line);
    }

    [Fact]
    public async Task Picking_again_replaces_the_folder_and_gives_up_the_old_one()
    {
        var setup = new Setup(platform);
        platform.NextPick = new PickedLocation(Folder("first"), "first");
        await setup.GiveAccessAsync(RetroArch, RetroArch.Folder("saves")!);
        platform.NextPick = new PickedLocation(Folder("second"), "second");
        await setup.GiveAccessAsync(RetroArch, RetroArch.Folder("saves")!);

        Assert.Equal("second", Assert.Single(setup.State.Sources).Name);
        Assert.Equal([Path.Combine(root, "first")], platform.Released);
    }

    [Fact]
    public async Task Cancelling_the_picker_changes_nothing()
    {
        var setup = new Setup(platform);

        var line = await setup.GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);

        Assert.Contains("cancelled", line, StringComparison.Ordinal);
        Assert.Empty(setup.State.Sources);
        Assert.False(platform.Storage.SetupFile.Exists);
    }

    [Fact]
    public async Task A_folder_rhome_cant_keep_is_reported()
    {
        platform.PickFails = new InvalidOperationException("Android wouldn't let rhome keep access");

        var line = await new Setup(platform).GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);

        Assert.Equal("melonDS · melonDS's own folder: Android wouldn't let rhome keep access", line);
    }

    [Fact]
    public async Task A_folder_that_went_away_is_not_done()
    {
        var saves = Folder("saves");
        platform.NextPick = new PickedLocation(saves, "saves");
        var setup = new Setup(platform);
        await setup.GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);

        Directory.Delete(saves);

        var step = setup.FolderSteps([MelonDS])[0];
        Assert.NotNull(step.Source);
        Assert.False(step.IsDone);
    }

    [Fact]
    public async Task The_first_scan_finds_the_saves_and_finishes_setup()
    {
        var setup = new Setup(platform);
        platform.NextPick = new PickedLocation(Folder("melonds", ("Platinum.sav", "platinum-us"), ("White.sav", "white")), "melonds");
        await setup.GiveAccessAsync(MelonDS, MelonDS.Folder("app")!);
        platform.NextPick = new PickedLocation(Folder("retroarch", ("mGBA/Emerald.srm", "emerald")), "retroarch");
        await setup.GiveAccessAsync(RetroArch, RetroArch.Folder("saves")!);
        Assert.False(setup.State.Done);

        var result = new MySaves(setup, platform.Storage).Scan(DateTimeOffset.Now);

        Assert.Equal([GameVersion.E, GameVersion.Pt, GameVersion.W], result.Games.Select(g => g.Save.Game));
        Assert.True(new Setup(platform).State.Done);
    }

    [Fact]
    public async Task A_save_file_picked_by_hand_is_scanned_too()
    {
        var path = Path.Combine(Folder("download", ("backup.bin", "emerald")), "backup.bin");
        platform.NextFile = new PickedLocation(path, "Download/backup.bin");
        var setup = new Setup(platform);

        Assert.Equal("save file: Download/backup.bin", await setup.AddSaveFileAsync());
        Assert.Contains("already", await setup.AddSaveFileAsync(), StringComparison.Ordinal);

        var game = Assert.Single(new MySaves(setup, platform.Storage).Scan(DateTimeOffset.Now).Games);
        Assert.Equal(GameVersion.E, game.Save.Game);
        Assert.Single(setup.PickedFiles);
    }

    [Fact]
    public async Task Removing_a_picked_file_gives_up_access_to_it()
    {
        var path = Path.Combine(Folder("download", ("backup.bin", "emerald")), "backup.bin");
        platform.NextFile = new PickedLocation(path, "backup.bin");
        var setup = new Setup(platform);
        await setup.AddSaveFileAsync();

        setup.Remove(setup.PickedFiles.Single());

        Assert.Empty(new Setup(platform).State.Sources);
        Assert.Equal([path], platform.Released);
    }

    [Fact]
    public void A_broken_setup_file_starts_setup_over()
    {
        Directory.CreateDirectory(platform.Storage.Root);
        File.WriteAllText(platform.Storage.SetupFile.Path, "{ not json");

        var setup = new Setup(platform);

        Assert.Equal(SetupState.Empty, setup.State);
        Assert.NotNull(setup.LoadProblem);
    }

    [Fact]
    public void The_android_app_may_look_for_every_known_emulator()
    {
        // Android 11+ hides other apps unless the manifest lists them in <queries>, so an emulator missing there is
        // never found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rhome.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var manifest = XDocument.Load(Path.Combine(dir.FullName, "src", "Rhome.Android", "Properties", "AndroidManifest.xml"));
        XNamespace android = "http://schemas.android.com/apk/res/android";

        var listed = manifest.Descendants("queries").Elements("package").Select(p => (string?)p.Attribute(android + "name")).ToHashSet();

        Assert.All(KnownEmulators.All.SelectMany(e => e.AppIds(Platform.Android)), id => Assert.Contains(id, listed));
    }
}

/// <summary>A platform on plain folders: IDs are paths, and picks are set up by the test.</summary>
sealed class FakePlatform(string storage) : IAppPlatform
{
    public HashSet<string> Installed { get; } = [];
    public PickedLocation? NextPick { get; set; }
    public PickedLocation? NextFile { get; set; }
    public Exception? PickFails { get; set; }
    public EmulatorFolder? LastStart { get; private set; }
    public List<string> Released { get; } = [];

    public string Description => "test";
    public Platform Os => Platform.Android;
    public AppStorage Storage { get; } = new(storage);

    public bool IsInstalled(EmulatorApp app) => Installed.Contains(app.Id);

    public Task<PickedLocation?> PickFolderAsync(EmulatorFolder? startAt)
    {
        LastStart = startAt;
        if (PickFails is { } e)
            throw e;
        return Task.FromResult(NextPick);
    }

    public Task<PickedLocation?> PickSaveFileAsync() => Task.FromResult(NextFile);

    public ISaveFolder Open(string id) => File.Exists(id) ? new OneFile(id) : new FolderOnDisk(id);

    public void Release(string id) => Released.Add(id);

    /// <summary>A picked file, as a folder holding just that file.</summary>
    sealed class OneFile(string path) : ISaveFolder
    {
        public string DisplayName => path;
        public bool IsAvailable() => File.Exists(path);
        public IReadOnlyList<FolderEntry> List(int maxDepth) =>
            [new(path, Path.GetFileName(path), new FileInfo(path).Length, File.GetLastWriteTimeUtc(path))];
        public FileStamp? Stat(string id) =>
            File.Exists(path) ? new FileStamp(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)) : null;
        public ISaveLocation Open(string id) => new SaveOnDisk(path);
    }
}
