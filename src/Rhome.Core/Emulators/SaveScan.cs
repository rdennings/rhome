using System.Diagnostics;
using System.Text.Json.Serialization;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>A folder to scan, and what it is.</summary>
/// <param name="Emulator">The emulator whose folder it is, or null for a save the user picked by hand (PLAN.md §2
/// "Fallback"), which is taken as it is.</param>
/// <param name="Folder">Which of the emulator's folders it is; null when <paramref name="Emulator"/> is.</param>
/// <param name="Access">The folder as the platform reaches it.</param>
public sealed record ScanFolder(KnownEmulator? Emulator, EmulatorFolder? Folder, ISaveFolder Access)
{
    /// <summary>What to call the folder in the app, e.g. "melonDS · next to the ROMs".</summary>
    public string Name => Emulator is null || Folder is null ? Access.DisplayName : $"{Emulator.Name} · {Folder.Name}";
}

/// <summary>What a save says about itself, for the saves list (PLAN.md §2: game, trainer, play time).</summary>
public sealed record SaveSummary(GameVersion Game, byte Generation, string Trainer, bool IsSupported, int Hours, int Minutes)
{
    public static SaveSummary Of(DetectedSave save) =>
        new(save.Game, save.Generation, save.Trainer, save.IsSupported, save.Save.PlayedHours, save.Save.PlayedMinutes);

    /// <summary>Play time as the games show it, e.g. "15:22".</summary>
    [JsonIgnore]
    public string PlayTime => $"{Hours}:{Minutes:00}";
}

/// <summary>A save file a scan found.</summary>
/// <param name="Save">What it is, or null when it isn't a Pokémon save rhome recognizes (an empty file, another
/// game's save).</param>
public sealed record ScannedFile(ScanFolder Where, FolderEntry Entry, SaveSummary? Save);

/// <summary>
/// One game's save, with every copy of it (PLAN.md §2 "One game, two saves"). Only games where at least one copy is a
/// Pokémon save are reported, but empty copies stay in the list: melonDS loads an empty file next to the ROM over a
/// real save in its own folder.
/// </summary>
/// <param name="Copies">Every copy, the one the emulator loads first.</param>
/// <param name="Loaded">The copy the emulator loads; changes to the others won't show in the game.</param>
public sealed record FoundGame(IReadOnlyList<ScannedFile> Copies, ScannedFile Loaded, LoadCertainty Certainty)
{
    /// <summary>What the game is: the loaded copy if that's a Pokémon save, otherwise the first copy that is.</summary>
    public SaveSummary Save => Loaded.Save ?? Copies.First(c => c.Save is not null).Save!;

    public bool HasSeveralCopies => Copies.Count > 1;

    /// <summary>The copy the emulator loads isn't a Pokémon save (it's empty, say), but another copy is.</summary>
    public bool LoadedCopyIsNotASave => Loaded.Save is null;
}

/// <summary>How scanning one folder went.</summary>
public enum FolderScanState
{
    Scanned,

    /// <summary>The folder can't be reached: the SD card is out, or access was taken away.</summary>
    Unreachable,

    /// <summary>The folder was there, but listing it failed.</summary>
    Failed,
}

/// <param name="Files">Files listed in the folder (all of them, saves or not).</param>
/// <param name="Saves">Pokémon saves found in it.</param>
/// <param name="Error">Why it failed, for <see cref="FolderScanState.Failed"/>.</param>
public sealed record FolderReport(ScanFolder Folder, FolderScanState State, int Files, int Saves, TimeSpan Took, string? Error = null);

/// <param name="Problems">Files that couldn't be read, one line each.</param>
public sealed record ScanResult(IReadOnlyList<FolderReport> Folders, IReadOnlyList<FoundGame> Games, IReadOnlyList<string> Problems);

/// <summary>
/// The full scan of the folders rhome has access to (PLAN.md §2 "First scan" and "Scan now"): lists each folder, reads
/// the files that can be saves, identifies each game from the save itself (save-first), and groups copies of one game.
/// Listing and reading go through <see cref="ISaveFolder"/>, so this runs the same on every platform.
/// </summary>
public static class SaveScan
{
    /// <summary>Scans <paramref name="folders"/>. Never throws for a folder or file it can't reach: it reports it.</summary>
    public static ScanResult Run(IEnumerable<ScanFolder> folders)
    {
        var reports = new List<FolderReport>();
        var files = new List<ScannedFile>();
        var problems = new List<string>();

        foreach (var folder in folders)
        {
            var clock = Stopwatch.StartNew();
            if (!Reachable(folder.Access))
            {
                reports.Add(new FolderReport(folder, FolderScanState.Unreachable, 0, 0, clock.Elapsed));
                continue;
            }

            IReadOnlyList<FolderEntry> entries;
            try
            {
                entries = folder.Access.List(folder.Folder?.Depth ?? 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                reports.Add(new FolderReport(folder, FolderScanState.Failed, 0, 0, clock.Elapsed, ex.Message));
                continue;
            }

            int saves = 0;
            foreach (var entry in entries.Where(e => IsCandidate(folder, e)))
            {
                var file = new ScannedFile(folder, entry, Read(folder, entry, problems));
                files.Add(file);
                if (file.Save is not null)
                    saves++;
            }
            reports.Add(new FolderReport(folder, FolderScanState.Scanned, entries.Count, saves, clock.Elapsed));
        }

        return new ScanResult(reports, Group(files), problems);
    }

    internal static bool Reachable(ISaveFolder folder)
    {
        try
        {
            return folder.IsAvailable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A file that can be a save: the emulator's save name, or anything the user picked by hand.</summary>
    static bool IsCandidate(ScanFolder folder, FolderEntry entry) =>
        folder.Emulator is not { } emulator || folder.Folder is not { } f || emulator.IsSaveFile(f, entry.RelativePath);

    static SaveSummary? Read(ScanFolder folder, FolderEntry entry, List<string> problems)
    {
        var (save, problem) = TryRead(folder, entry);
        if (problem is not null)
            problems.Add(problem);
        return save;
    }

    /// <summary>What the file is, or why it couldn't be read. A file that isn't a Pokémon save is no problem.</summary>
    internal static (SaveSummary? Save, string? Problem) TryRead(ScanFolder folder, FolderEntry entry)
    {
        // Only files the size of a save are read, so a stray big file in a ROM folder isn't loaded.
        if (entry.Size is <= 0 or > int.MaxValue || !SaveUtil.IsSizeValid((int)entry.Size))
            return (null, null);
        try
        {
            return (SaveDetector.Detect(folder.Access.Open(entry.Id).Read()) is { } detected ? SaveSummary.Of(detected) : null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"couldn't read {entry.RelativePath} in {folder.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Groups files into games with <see cref="SaveCopies"/>, and says which copy each emulator loads; a hand-picked
    /// file is a game of its own. Games with no Pokémon save among their copies are left out.
    /// </summary>
    public static List<FoundGame> Group(IEnumerable<ScannedFile> files)
    {
        var byFile = new Dictionary<EmulatorSaveFile, ScannedFile>(ReferenceEqualityComparer.Instance);
        var games = new List<FoundGame>();

        foreach (var file in files)
        {
            if (file.Where is { Emulator: { } emulator, Folder: { } folder })
                byFile[new EmulatorSaveFile(emulator, folder, file.Entry.RelativePath, file.Entry.Size, file.Entry.LastModified)] = file;
            else if (file.Save is not null)
                games.Add(new FoundGame([file], file, LoadCertainty.OnlyCopy));
        }

        foreach (var game in SaveCopies.Resolve(byFile.Keys))
        {
            var copies = game.Copies.Select(c => byFile[c]).ToList();
            if (copies.Any(c => c.Save is not null))
                games.Add(new FoundGame(copies, byFile[game.Loaded], game.Certainty));
        }

        return games
            .OrderBy(g => g.Save.Generation)
            .ThenBy(g => g.Save.Game)
            .ThenBy(g => g.Save.Trainer, StringComparer.Ordinal)
            .ToList();
    }
}
