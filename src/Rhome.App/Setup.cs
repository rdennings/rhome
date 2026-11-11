using System.Text.Json;
using System.Text.Json.Serialization;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>A place rhome reads saves from: one of a known emulator's folders, or a save file picked by hand.</summary>
/// <param name="Id">The platform's ID for it (<see cref="PickedLocation.Id"/>).</param>
/// <param name="Emulator">The emulator's key, or null for a save file picked by hand.</param>
/// <param name="Folder">The key of the emulator's folder this is; null when <paramref name="Emulator"/> is.</param>
/// <param name="Name">Where it is, as the platform described it when it was picked.</param>
public sealed record SaveSource(string Id, string? Emulator, string? Folder, string Name)
{
    [JsonIgnore]
    public bool IsPickedFile => Emulator is null;
}

/// <summary>What <c>setup.json</c> holds.</summary>
/// <param name="Done">First-run setup has finished once (its first scan ran), so the app opens on the saves.</param>
public sealed record SetupState(IReadOnlyList<SaveSource> Sources, bool Done)
{
    public static SetupState Empty { get; } = new([], false);
}

/// <summary>One step of "Give access": an emulator's folder, and the folder the user picked for it, if any.</summary>
/// <param name="Reachable">Whether rhome can reach the picked folder right now.</param>
public sealed record FolderStep(KnownEmulator Emulator, EmulatorFolder Folder, SaveSource? Source, bool Reachable)
{
    public bool IsDone => Source is not null && Reachable;
}

/// <summary>
/// First-run setup (PLAN.md §2 "Loading saves", issue #42): find the known emulators that are installed, give access
/// to each one's save folders, then scan them. It can be run again later, e.g. after installing another emulator.
/// Keeps which folder is which in <c>setup.json</c>, because the platform only remembers the grants themselves.
/// </summary>
public sealed class Setup
{
    readonly IAppPlatform platform;
    readonly FileOnDisk file;

    public Setup(IAppPlatform platform)
    {
        this.platform = platform;
        file = platform.Storage.SetupFile;
        (State, LoadProblem) = Load(file);
    }

    public SetupState State { get; private set; }

    /// <summary>Why <c>setup.json</c> couldn't be read, if it couldn't; setup then starts over.</summary>
    public string? LoadProblem { get; }

    /// <summary>Step 1: the known emulators with an app installed on this device.</summary>
    public IReadOnlyList<KnownEmulator> FindEmulators() =>
        KnownEmulators.All
            .Where(e => e.Apps.Any(a => a.Platform == platform.Os && platform.IsInstalled(a)))
            .ToList();

    /// <summary>Step 2: every save folder of these emulators on this platform, and whether it has been picked.</summary>
    public IReadOnlyList<FolderStep> FolderSteps(IEnumerable<KnownEmulator> emulators) =>
        emulators
            .SelectMany(e => e.FoldersOn(platform.Os).Select(f => (e, f)))
            .Select(x =>
            {
                var source = SourceFor(x.e, x.f);
                return new FolderStep(x.e, x.f, source, source is not null && Reachable(source));
            })
            .ToList();

    /// <summary>Save files picked by hand.</summary>
    public IEnumerable<SaveSource> PickedFiles => State.Sources.Where(s => s.IsPickedFile);

    /// <summary>
    /// Opens the picker inside the emulator's folder and keeps the folder the user picks for it, replacing an earlier
    /// pick. Returns a line for the log.
    /// </summary>
    public async Task<string> GiveAccessAsync(KnownEmulator emulator, EmulatorFolder folder)
    {
        var name = $"{emulator.Name} · {folder.Name}";
        PickedLocation? picked;
        try
        {
            picked = await platform.PickFolderAsync(folder);
        }
        catch (InvalidOperationException ex)
        {
            return $"{name}: {ex.Message}";
        }
        if (picked is null)
            return $"{name}: cancelled, nothing changed";

        var old = SourceFor(emulator, folder);
        Save(State with
        {
            Sources = [.. State.Sources.Where(s => s != old), new SaveSource(picked.Id, emulator.Key, folder.Key, picked.DisplayName)],
        });
        if (old is not null && old.Id != picked.Id)
            ReleaseIfUnused(old.Id);

        // A ROM folder is wherever the user keeps ROMs, so any folder is right there.
        return picked.IsWhereAsked == false && !folder.RomFolder
            ? $"{name}: access to {picked.DisplayName}. That isn't where {emulator.Name} keeps saves " +
              $"({Describe(folder)}), so its saves may not be found."
            : $"{name}: access to {picked.DisplayName}";
    }

    /// <summary>The fallback: one save file picked by hand. Returns a line for the log.</summary>
    public async Task<string> AddSaveFileAsync()
    {
        PickedLocation? picked;
        try
        {
            picked = await platform.PickSaveFileAsync();
        }
        catch (InvalidOperationException ex)
        {
            return $"save file: {ex.Message}";
        }
        if (picked is null)
            return "save file: cancelled, nothing changed";
        if (State.Sources.Any(s => s.Id == picked.Id))
            return $"{picked.DisplayName} is already set up";

        Save(State with { Sources = [.. State.Sources, new SaveSource(picked.Id, null, null, picked.DisplayName)] });
        return $"save file: {picked.DisplayName}";
    }

    /// <summary>Stops using a folder or file, and gives up access to it.</summary>
    public void Remove(SaveSource source)
    {
        Save(State with { Sources = [.. State.Sources.Where(s => s != source)] });
        ReleaseIfUnused(source.Id);
    }

    /// <summary>
    /// Every folder and file set up, ready to scan or check. Opening them does no I/O. A folder of an emulator that has
    /// left the list is scanned like a picked file: every file, no grouping.
    /// </summary>
    public IReadOnlyList<(SaveSource Source, ScanFolder Folder)> ScanFolders() =>
        State.Sources.Select(s =>
        {
            var emulator = s.Emulator is null ? null : KnownEmulators.Find(s.Emulator);
            var folder = s.Folder is null ? null : emulator?.Folder(s.Folder);
            return (s, new ScanFolder(folder is null ? null : emulator, folder, platform.Open(s.Id)));
        }).ToList();

    /// <summary>Step 3 ran (<see cref="MySaves.Scan"/>), so from now on the app opens on the saves.</summary>
    public void Finish()
    {
        if (!State.Done)
            Save(State with { Done = true });
    }

    /// <summary>Where an emulator folder is by default, e.g. "Internal storage/Android/media/…".</summary>
    public static string Describe(EmulatorFolder folder) =>
        folder.Area switch
        {
            StorageArea.Shared => "Internal storage",
            StorageArea.SdCard => "SD card",
            _ => "~",
        } + "/" + folder.Path;

    SaveSource? SourceFor(KnownEmulator emulator, EmulatorFolder folder) =>
        State.Sources.FirstOrDefault(s => s.Emulator == emulator.Key && s.Folder == folder.Key);

    bool Reachable(SaveSource source)
    {
        try
        {
            return platform.Open(source.Id).IsAvailable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    void ReleaseIfUnused(string id)
    {
        if (State.Sources.All(s => s.Id != id))
            platform.Release(id);
    }

    void Save(SetupState state)
    {
        file.Replace(JsonSerializer.SerializeToUtf8Bytes(state, SetupJsonContext.Default.SetupState));
        State = state;
    }

    static (SetupState, string?) Load(FileOnDisk file)
    {
        if (!file.Exists)
            return (SetupState.Empty, null);
        try
        {
            using var stream = file.OpenRead();
            return (JsonSerializer.Deserialize(stream, SetupJsonContext.Default.SetupState) ?? SetupState.Empty, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return (SetupState.Empty, $"setup.json couldn't be read, so setup starts over: {ex.Message}");
        }
    }
}

// Source-generated, so reading needs no reflection (safe for trimmed builds).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(SetupState))]
partial class SetupJsonContext : JsonSerializerContext;
