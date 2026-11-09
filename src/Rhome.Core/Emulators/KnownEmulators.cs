using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rhome.Core;

/// <summary>A platform rhome runs on. Each emulator lists its app and save folders per platform.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Platform>))]
public enum Platform
{
    [JsonStringEnumMemberName("android")] Android,
    [JsonStringEnumMemberName("linux")] Linux,
}

/// <summary>The storage a save folder's path starts from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorageArea>))]
public enum StorageArea
{
    /// <summary>Android's internal shared storage (<c>/sdcard</c>).</summary>
    [JsonStringEnumMemberName("shared")] Shared,

    /// <summary>The removable SD card. The platform layer looks up its volume on the device.</summary>
    [JsonStringEnumMemberName("sd-card")] SdCard,

    /// <summary>The user's home folder (Linux).</summary>
    [JsonStringEnumMemberName("home")] Home,
}

/// <summary>An emulator's app on one platform. On Android, <see cref="Id"/> is the package name.</summary>
public sealed record EmulatorApp(Platform Platform, string Id);

/// <summary>A folder where an emulator keeps saves by default.</summary>
/// <param name="Key">Stable within its emulator; <see cref="KnownEmulator.LoadOrder"/> refers to it.</param>
/// <param name="Name">The folder as a person would describe it, e.g. "next to the ROMs".</param>
/// <param name="Path">Inside <paramref name="Area"/>, separated by <c>/</c>, without a leading <c>/</c>.</param>
/// <param name="Depth">How many levels of subfolders under the path can hold saves (0 = only the folder itself).</param>
/// <param name="RomFolder">
/// The folder holds the ROMs and the emulator saves next to them. Its path is only a common default; the user picks
/// where their ROMs really are.
/// </param>
public sealed record EmulatorFolder(
    string Key,
    string Name,
    Platform Platform,
    StorageArea Area,
    string Path,
    int Depth = 0,
    bool RomFolder = false);

/// <summary>An emulator rhome knows: its apps, where it keeps saves, and which copy it loads (PLAN.md §2).</summary>
/// <param name="Key">Stable ID; remembered saves and folder grants refer to it.</param>
/// <param name="SaveExtensions">File extensions that are saves, lower case, with the dot.</param>
/// <param name="SaveFileNames">Exact file names that are saves, for emulators whose saves have no extension.</param>
/// <param name="LoadOrder">
/// Folder keys, the one the emulator loads first when a game has a save in several of them. Empty when that isn't
/// known.
/// </param>
public sealed record KnownEmulator(
    string Key,
    string Name,
    IReadOnlyList<EmulatorApp> Apps,
    IReadOnlyList<string> SaveExtensions,
    IReadOnlyList<EmulatorFolder> Folders,
    IReadOnlyList<string>? SaveFileNames = null,
    IReadOnlyList<string>? LoadOrder = null)
{
    public IReadOnlyList<string> SaveFileNames { get; init; } = SaveFileNames ?? [];

    public IReadOnlyList<string> LoadOrder { get; init; } = LoadOrder ?? [];

    /// <summary>The emulator's app IDs on <paramref name="platform"/> (Android package names).</summary>
    public IEnumerable<string> AppIds(Platform platform) =>
        Apps.Where(a => a.Platform == platform).Select(a => a.Id);

    /// <summary>The emulator's save folders on <paramref name="platform"/>.</summary>
    public IEnumerable<EmulatorFolder> FoldersOn(Platform platform) => Folders.Where(f => f.Platform == platform);

    /// <summary>The folder with this key, or null.</summary>
    public EmulatorFolder? Folder(string key) => Folders.FirstOrDefault(f => f.Key == key);

    /// <summary>
    /// Whether a file found in <paramref name="folder"/> is one of this emulator's saves: the right name or
    /// extension, and no deeper than the folder's <see cref="EmulatorFolder.Depth"/>.
    /// </summary>
    /// <param name="relativePath">The file's path inside the folder, separated by <c>/</c>.</param>
    public bool IsSaveFile(EmulatorFolder folder, string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length - 1 > folder.Depth || parts.Any(p => p.Length == 0 || p == "." || p == ".."))
            return false;

        var name = parts[^1];
        if (name.StartsWith('.')) // hidden files, e.g. macOS's ._ files on a card
            return false;
        return SaveFileNames.Contains(name, StringComparer.Ordinal)
               || SaveExtensions.Any(e => name.Length > e.Length && name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The list of emulators rhome knows, read from <c>emulators.json</c> in this assembly. It's data, not code: adding
/// an emulator is a new entry in that file.
/// </summary>
public static class KnownEmulators
{
    static readonly Lazy<IReadOnlyList<KnownEmulator>> BuiltIn = new(() =>
    {
        using var stream = typeof(KnownEmulators).Assembly.GetManifestResourceStream("Rhome.Core.emulators.json")
                           ?? throw new InvalidOperationException("emulators.json is missing from Rhome.Core.");
        return Read(stream);
    });

    /// <summary>Every emulator rhome knows.</summary>
    public static IReadOnlyList<KnownEmulator> All => BuiltIn.Value;

    /// <summary>The emulator with this key, or null.</summary>
    public static KnownEmulator? Find(string key) => All.FirstOrDefault(e => e.Key == key);

    /// <summary>The emulator an installed app belongs to (on Android, by package name), or null.</summary>
    public static KnownEmulator? ForApp(Platform platform, string appId) =>
        All.FirstOrDefault(e => e.AppIds(platform).Contains(appId, StringComparer.Ordinal));

    /// <summary>Reads and checks an emulator list in the format of <c>emulators.json</c>.</summary>
    /// <exception cref="FormatException">The list can't be read or breaks one of its rules.</exception>
    public static IReadOnlyList<KnownEmulator> Read(Stream json)
    {
        EmulatorList? list;
        try
        {
            list = JsonSerializer.Deserialize(json, EmulatorJsonContext.Default.EmulatorList);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The emulator list isn't valid: " + ex.Message, ex);
        }

        var emulators = list?.Emulators ?? throw new FormatException("The emulator list is empty.");
        var problems = Problems(emulators);
        if (problems.Count > 0)
            throw new FormatException("The emulator list breaks its rules: " + string.Join("; ", problems));
        return emulators;
    }

    /// <summary>Everything wrong with a list, so a bad entry fails the tests rather than a scan on someone's device.</summary>
    static List<string> Problems(IReadOnlyList<KnownEmulator> emulators)
    {
        var problems = new List<string>();
        foreach (var key in emulators.GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.Key))
            problems.Add($"two emulators use the key \"{key}\"");

        foreach (var e in emulators)
        {
            var at = $"{e.Key}:";
            if (string.IsNullOrWhiteSpace(e.Key) || string.IsNullOrWhiteSpace(e.Name))
                problems.Add($"{at} needs a key and a name");
            if (e.Apps.Count == 0)
                problems.Add($"{at} needs at least one app");
            if (e.Apps.Any(a => string.IsNullOrWhiteSpace(a.Id)))
                problems.Add($"{at} has an app without an ID");
            if (e.SaveExtensions.Count == 0 && e.SaveFileNames.Count == 0)
                problems.Add($"{at} needs save extensions or save file names");
            foreach (var x in e.SaveExtensions.Where(x => x.Length < 2 || x[0] != '.' || x.Contains('/') || x != x.ToLowerInvariant()))
                problems.Add($"{at} save extension \"{x}\" should be lower case and start with a dot");
            if (e.Folders.Count == 0)
                problems.Add($"{at} needs at least one save folder");

            foreach (var key in e.Folders.GroupBy(f => f.Key).Where(g => g.Count() > 1).Select(g => g.Key))
                problems.Add($"{at} two folders use the key \"{key}\"");
            foreach (var f in e.Folders)
            {
                if (string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Name))
                    problems.Add($"{at} every folder needs a key and a name");
                if (f.Path.Length == 0 || f.Path.StartsWith('/') || f.Path.EndsWith('/') || f.Path.Contains('\\')
                    || f.Path.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                    problems.Add($"{at} folder \"{f.Key}\" path \"{f.Path}\" should be relative, separated by single /");
                if (f.Depth < 0)
                    problems.Add($"{at} folder \"{f.Key}\" has a negative depth");
                if (!e.Apps.Any(a => a.Platform == f.Platform))
                    problems.Add($"{at} folder \"{f.Key}\" is for a platform the emulator has no app on");
            }

            foreach (var key in e.LoadOrder.Where(k => e.Folder(k) is null))
                problems.Add($"{at} load order names \"{key}\", which isn't one of its folders");
            if (e.LoadOrder.Distinct().Count() != e.LoadOrder.Count)
                problems.Add($"{at} load order names a folder twice");
        }
        return problems;
    }
}

/// <summary>The top level of <c>emulators.json</c>.</summary>
sealed record EmulatorList(IReadOnlyList<KnownEmulator> Emulators);

// Source-generated, so reading needs no reflection (safe for trimmed and NativeAOT builds).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(EmulatorList))]
partial class EmulatorJsonContext : JsonSerializerContext;
