using System.Text.Json;
using System.Text.Json.Serialization;
using PKHeX.Core;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>A save file the last scan found, as <c>saves.json</c> keeps it.</summary>
/// <param name="Source">The <see cref="SaveSource.Id"/> of the folder or file it was found in.</param>
/// <param name="Emulator">With <paramref name="Folder"/>, which <see cref="SaveSource"/> that is.</param>
/// <param name="Id">The file's <see cref="FolderEntry.Id"/>.</param>
/// <param name="Path">The file's <see cref="FolderEntry.RelativePath"/>.</param>
/// <param name="Size">Its size when rhome last read it.</param>
/// <param name="Modified">Its time when rhome last read it.</param>
/// <param name="Save">What it was then; null for a copy that isn't a save (an empty file the emulator loads).</param>
public sealed record KnownFile(
    string Source,
    string? Emulator,
    string? Folder,
    string Id,
    string Path,
    long Size,
    DateTimeOffset Modified,
    SaveSummary? Save);

/// <summary>What <c>saves.json</c> holds.</summary>
/// <param name="ScannedAt">When the last full scan ran; null if none has since saves.json existed.</param>
public sealed record SavesState(DateTimeOffset? ScannedAt, IReadOnlyList<KnownFile> Files)
{
    public static SavesState Empty { get; } = new(null, []);
}

/// <summary>A save opened from My saves, read from disk just now.</summary>
/// <param name="Game">The game as it is now, which may differ from the row that was tapped.</param>
/// <param name="ReadAgain">The save had changed on disk since rhome last read it.</param>
public sealed record OpenedSave(FoundGame Game, DetectedSave Save, byte[] Bytes, bool ReadAgain);

/// <summary>
/// The My saves list (PLAN.md §2 "Loading saves", issue #43). A full scan (the first one, and Scan now) lists the
/// folders and keeps what it found in <c>saves.json</c>. Every launch then checks only those files
/// (<see cref="SaveCheck"/>), never lists a folder, so it stays fast with thousands of ROMs next to the saves.
/// </summary>
/// <remarks>
/// Not thread-safe: the screens run one operation at a time, off the UI thread.
/// </remarks>
public sealed class MySaves
{
    readonly FileOnDisk file;
    SavesState state;
    FolderMap? map;

    public MySaves(Setup setup, AppStorage storage)
    {
        Setup = setup;
        file = storage.SavesFile;
        (state, LoadProblem) = Load(file);
        Games = SaveScan.Group(Known(Map()));
    }

    public Setup Setup { get; }

    /// <summary>Why <c>saves.json</c> couldn't be read, if it couldn't; the list then starts empty.</summary>
    public string? LoadProblem { get; }

    /// <summary>When the last full scan ran, or null if none has: the list is then empty, and needs one.</summary>
    public DateTimeOffset? ScannedAt => state.ScannedAt;

    /// <summary>The games as the last scan, check or open left them. Reading this does no I/O.</summary>
    public IReadOnlyList<FoundGame> Games { get; private set; }

    /// <summary>
    /// The launch check: whether each known save changed or went, without listing any folder. Changed saves are read
    /// again. Saves that went are forgotten (Scan now finds them again if they moved). Call it off the UI thread.
    /// </summary>
    public SaveCheckResult Check()
    {
        var folders = Map();
        var known = Known(folders);
        var result = SaveCheck.Run(known);
        Games = result.Games;
        // Files of a folder that's no longer set up were left out of the check: forget them too.
        if (result.AnythingChanged || known.Count != state.Files.Count)
            Store(state with { Files = Remember(folders, result.Files.Where(f => f.Change != SaveChange.Gone).Select(f => f.File)) });
        return result;
    }

    /// <summary>
    /// The full scan (first-run setup's step 3, and Scan now): lists every folder set up and finds every save.
    /// Slow on a big ROM folder (Spike B: about 2 s for 2,800 files), so call it off the UI thread.
    /// </summary>
    public ScanResult Scan(DateTimeOffset now)
    {
        var folders = Map();
        var result = SaveScan.Run(folders.All);

        // A folder that couldn't be scanned (SD card out) keeps the saves known in it, rather than forgetting them.
        var scanned = result.Folders.Where(f => f.State == FolderScanState.Scanned).Select(f => f.Folder)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var files = Known(folders).Where(f => !scanned.Contains(f.Where))
            .Concat(result.Games.SelectMany(g => g.Copies))
            .ToList();

        Store(new SavesState(now, Remember(folders, files)));
        Games = SaveScan.Group(files);
        Setup.Finish();
        return result;
    }

    /// <summary>
    /// Opens a game's save: checks its copies first, so a save that changed since rhome last read it is read again
    /// (and the list updated), then reads the copy the emulator loads.
    /// </summary>
    /// <exception cref="InvalidOperationException">The save is gone, or the copy the emulator loads isn't a save.</exception>
    /// <exception cref="IOException">The save couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">rhome no longer has access to it.</exception>
    public OpenedSave Open(FoundGame game)
    {
        var check = SaveCheck.Run(game.Copies);
        if (check.AnythingChanged)
        {
            var folders = Map();
            var files = Known(folders).Where(f => !game.Copies.Any(c => Same(c, f)))
                .Concat(check.Files.Where(f => f.Change != SaveChange.Gone).Select(f => f.File))
                .ToList();
            Store(state with { Files = Remember(folders, files) });
            Games = SaveScan.Group(Known(folders));
        }

        var title = SaveText.Title(game);
        var now = check.Games.FirstOrDefault()
            ?? throw new InvalidOperationException($"{title} is gone from {game.Loaded.Where.Name}. If it moved, tap Scan now.");
        if (now.LoadedCopyIsNotASave)
            throw new InvalidOperationException(
                $"{title}: the copy {now.Loaded.Where.Name} loads, {now.Loaded.Entry.RelativePath}, isn't a save (it's empty?), so there's nothing to open.");

        var loaded = now.Loaded;
        var bytes = loaded.Where.Access.Open(loaded.Entry.Id).Read();
        var detected = SaveDetector.Detect(bytes)
            ?? throw new InvalidOperationException($"{loaded.Entry.RelativePath} changed while opening and isn't a Pokémon save now.");
        return new OpenedSave(now, detected, bytes, check.In(SaveChange.Changed).Any());
    }

    static bool Same(ScannedFile a, ScannedFile b) => ReferenceEquals(a.Where, b.Where) && a.Entry.Id == b.Entry.Id;

    /// <summary>The known files whose folder is still set up, as scanned files in it.</summary>
    List<ScannedFile> Known(FolderMap folders) =>
        state.Files
            .Select(f => folders.Find(f) is { } where
                ? new ScannedFile(where, new FolderEntry(f.Id, f.Path, f.Size, f.Modified), f.Save)
                : null)
            .OfType<ScannedFile>()
            .ToList();

    static IReadOnlyList<KnownFile> Remember(FolderMap folders, IEnumerable<ScannedFile> files) =>
        files
            .Select(f => folders.SourceOf(f.Where) is { } s
                ? new KnownFile(s.Id, s.Emulator, s.Folder, f.Entry.Id, f.Entry.RelativePath, f.Entry.Size, f.Entry.LastModified, f.Save)
                : null)
            .OfType<KnownFile>()
            .ToList();

    /// <summary>The folders set up now. Kept while setup doesn't change, so games found earlier still match them.</summary>
    FolderMap Map()
    {
        var sources = Setup.State.Sources;
        if (map is null || !ReferenceEquals(map.Sources, sources))
            map = new FolderMap(sources, Setup.ScanFolders());
        return map;
    }

    void Store(SavesState next)
    {
        file.Replace(JsonSerializer.SerializeToUtf8Bytes(next, SavesJsonContext.Default.SavesState));
        state = next;
    }

    static (SavesState, string?) Load(FileOnDisk file)
    {
        if (!file.Exists)
            return (SavesState.Empty, null);
        try
        {
            using var stream = file.OpenRead();
            return (JsonSerializer.Deserialize(stream, SavesJsonContext.Default.SavesState) ?? SavesState.Empty, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return (SavesState.Empty, $"saves.json couldn't be read, so the list starts empty until Scan now: {ex.Message}");
        }
    }

    sealed class FolderMap(IReadOnlyList<SaveSource> sources, IReadOnlyList<(SaveSource Source, ScanFolder Folder)> folders)
    {
        readonly Dictionary<ScanFolder, SaveSource> bySource =
            folders.ToDictionary(f => f.Folder, f => f.Source, (IEqualityComparer<ScanFolder>)ReferenceEqualityComparer.Instance);

        public IReadOnlyList<SaveSource> Sources => sources;

        public IEnumerable<ScanFolder> All => folders.Select(f => f.Folder);

        public ScanFolder? Find(KnownFile file) =>
            folders.FirstOrDefault(f => f.Source.Id == file.Source && f.Source.Emulator == file.Emulator && f.Source.Folder == file.Folder).Folder;

        public SaveSource? SourceOf(ScanFolder folder) => bySource.GetValueOrDefault(folder);
    }
}

// Source-generated, so reading needs no reflection (safe for trimmed builds). Games are stored by name.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SavesState))]
partial class SavesJsonContext : JsonSerializerContext;
