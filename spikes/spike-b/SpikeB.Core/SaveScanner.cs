using System.Diagnostics;
using System.Text.Json;
using PKHeX.Core;

namespace SpikeB.Core;

public enum SaveStatus { Unchanged, New, Changed }

/// <summary>A Pokémon save found by a scan, ready to show in a "My saves" list.</summary>
public sealed record FoundSave(
    EmulatorProfile Emulator,
    SaveEntry Entry,
    SaveStatus Status,
    string Game,
    string Trainer,
    string PlayTime,
    string? Note = null);

/// <param name="FilesListed">Files seen by listing folders (0 when every folder was only checked).</param>
/// <param name="FilesChecked">Known saves looked up one by one, without listing their folder.</param>
public sealed record ScanResult(
    IReadOnlyList<FoundSave> Saves,
    int FilesListed,
    int FilesChecked,
    int CandidateFiles,
    int FilesRead,
    TimeSpan ListTime,
    TimeSpan ReadTime,
    IReadOnlyList<string> Errors);

/// <summary>What the last scan saw, so the next one only reads new or changed files.</summary>
public sealed class ScanState
{
    /// <summary>
    /// A save file seen before. Files that aren't Pokémon saves are kept too (Game is null), so they're
    /// not read again, and so are empty ones, because an emulator may load an empty save over a real one.
    /// </summary>
    public sealed record KnownFile(long Size, long LastModifiedMs, string? Game, string? Trainer, string? PlayTime,
        string? Name = null, string? RelativePath = null);

    public Dictionary<string, KnownFile> Files { get; set; } = [];
    public HashSet<string> ScannedEmulators { get; set; } = [];

    public static ScanState Load(string path)
    {
        try { return JsonSerializer.Deserialize<ScanState>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));
}

public static class SaveScanner
{
    /// <summary>Saves larger than this are skipped (the biggest MVP save is 512 KB; Switch saves are ~1.5 MB).</summary>
    const long MaxSaveSize = 32 * 1024 * 1024;

    /// <summary>
    /// "Scan now": lists every folder to find new saves, reads only files that are new or changed since
    /// <paramref name="state"/>, and updates <paramref name="state"/> in place. Saves in a folder scanned
    /// for the first time are reported as Unchanged, so the first scan doesn't flag everything as new.
    /// </summary>
    public static ScanResult Scan(IEnumerable<(EmulatorProfile Emulator, ISaveFolder Folder)> sources, ScanState state) =>
        Run(sources, state, listAll: true);

    /// <summary>
    /// Every launch: looks up only the saves <paramref name="state"/> already knows, one by one, so a
    /// folder full of ROMs costs nothing. Finds changed and deleted saves, not new ones (that's
    /// <see cref="Scan"/>). A folder that was never scanned is listed in full once.
    /// </summary>
    public static ScanResult Check(IEnumerable<(EmulatorProfile Emulator, ISaveFolder Folder)> sources, ScanState state) =>
        Run(sources, state, listAll: false);

    static ScanResult Run(IEnumerable<(EmulatorProfile Emulator, ISaveFolder Folder)> sources, ScanState state, bool listAll)
    {
        var saves = new List<FoundSave>();
        var errors = new List<string>();
        var saveFiles = new List<(EmulatorProfile Emulator, SaveEntry Entry)>();
        int listed = 0, checkedFiles = 0, candidates = 0, read = 0;
        var listTime = TimeSpan.Zero;
        var readTime = TimeSpan.Zero;

        foreach (var (emulator, folder) in sources)
        {
            bool firstScan = !state.ScannedEmulators.Contains(emulator.Key);
            string prefix = emulator.Key + "|";
            var sw = Stopwatch.StartNew();
            var entries = new List<SaveEntry>();
            try
            {
                if (listAll || firstScan)
                {
                    var all = folder.List(emulator.MaxDepth);
                    listed += all.Count;
                    entries.AddRange(all.Where(e => emulator.IsSaveFile(e.Name) && e.Size <= MaxSaveSize));
                }
                else
                {
                    // Check the folder first: if the SD card is out, every save would look deleted.
                    if (!folder.IsAvailable())
                    {
                        errors.Add($"{emulator.Name}: folder not available right now; kept what we knew");
                        KeepKnown(emulator, state, saves, saveFiles);
                        continue;
                    }
                    foreach (var (key, known) in state.Files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                    {
                        checkedFiles++;
                        var entry = folder.Find(key[prefix.Length..], known.RelativePath);
                        if (entry is not null)
                            entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{emulator.Name}: can't {(listAll || firstScan ? "list folder" : "check saves")} ({ex.GetType().Name}: {ex.Message})");
                KeepKnown(emulator, state, saves, saveFiles);
                continue;
            }
            listTime += sw.Elapsed;

            var seen = new HashSet<string>();
            foreach (var entry in entries)
            {
                saveFiles.Add((emulator, entry));
                string key = prefix + entry.Id;
                seen.Add(key);
                long modified = entry.LastModified.ToUnixTimeMilliseconds();

                var known = state.Files.GetValueOrDefault(key);
                SaveStatus status;
                if (known is not null && known.Size == entry.Size && known.LastModifiedMs == modified)
                {
                    status = SaveStatus.Unchanged;
                    if (known.Name is null) // state from an older version of the spike
                        state.Files[key] = known = known with { Name = entry.Name, RelativePath = entry.RelativePath };
                }
                else if (entry.Size <= 0)
                {
                    // Nothing to read, but remember it for the "two saves for one game" check.
                    state.Files[key] = new ScanState.KnownFile(0, modified, null, null, null, entry.Name, entry.RelativePath);
                    continue;
                }
                else
                {
                    status = firstScan ? SaveStatus.Unchanged : known is null ? SaveStatus.New : SaveStatus.Changed;
                    sw.Restart();
                    try
                    {
                        known = Identify(folder.ReadAll(entry), entry, modified);
                        read++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{emulator.Name}: can't read {entry.RelativePath} ({ex.GetType().Name}: {ex.Message})");
                        continue;
                    }
                    finally { readTime += sw.Elapsed; }
                    state.Files[key] = known;
                }

                if (known.Size > 0)
                    candidates++;
                if (known.Game is not null)
                    saves.Add(new FoundSave(emulator, entry, status, known.Game, known.Trainer ?? "", known.PlayTime ?? ""));
            }

            // Forget this folder's files that are gone (a full listing or a lookup didn't find them).
            foreach (var key in state.Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(k)).ToList())
                state.Files.Remove(key);
            state.ScannedEmulators.Add(emulator.Key);
        }

        var ordered = saves.Select(s => s with { Note = DuplicateNote(s, saveFiles) })
                           .OrderByDescending(s => s.Status != SaveStatus.Unchanged)
                           .ThenByDescending(s => s.Entry.LastModified)
                           .ToList();
        return new ScanResult(ordered, listed, checkedFiles, candidates, read, listTime, readTime, errors);
    }

    /// <summary>When a folder can't be reached, still show the saves we knew were in it.</summary>
    static void KeepKnown(EmulatorProfile emulator, ScanState state, List<FoundSave> saves,
        List<(EmulatorProfile, SaveEntry)> saveFiles)
    {
        string prefix = emulator.Key + "|";
        foreach (var (key, known) in state.Files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var name = known.Name ?? Path.GetFileName(key);
            var entry = new SaveEntry(key[prefix.Length..], name, known.RelativePath ?? name, known.Size,
                DateTimeOffset.FromUnixTimeMilliseconds(known.LastModifiedMs));
            saveFiles.Add((emulator, entry));
            if (known.Game is not null)
                saves.Add(new FoundSave(emulator, entry, SaveStatus.Unchanged, known.Game, known.Trainer ?? "", known.PlayTime ?? ""));
        }
    }

    /// <summary>
    /// The same game can have a save in more than one of an emulator's folders (melonDS: in
    /// Android/data and next to the ROM). Emulators name the save after the ROM, so saves with the
    /// same name for the same emulator app, in different folders, are the same game. (Within one
    /// folder the name says nothing: every Eden save is called "main".) Returns null when there's only one.
    /// </summary>
    public static string? DuplicateNote(FoundSave save, IReadOnlyList<(EmulatorProfile Emulator, SaveEntry Entry)> saveFiles)
    {
        string name = Path.GetFileNameWithoutExtension(save.Entry.Name);
        var copies = saveFiles
            .Where(f => f.Emulator.PackageName == save.Emulator.PackageName &&
                        (f.Emulator.Key != save.Emulator.Key || f.Entry.Id == save.Entry.Id) &&
                        string.Equals(Path.GetFileNameWithoutExtension(f.Entry.Name), name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (copies.Count < 2)
            return null;

        // The emulator decides which one it loads; when the folders don't say, guess the newest non-empty one.
        var live = copies.OrderByDescending(f => f.Emulator.LoadPriority)
                         .ThenByDescending(f => f.Entry.Size > 0)
                         .ThenByDescending(f => f.Entry.LastModified)
                         .First();
        bool known = copies.Select(f => f.Emulator.LoadPriority).Distinct().Count() > 1;
        string app = save.Emulator.Name.Split(' ')[0];
        if (live.Entry.Id == save.Entry.Id && live.Emulator.Key == save.Emulator.Key)
            return $"{copies.Count} saves for this game. {app} {(known ? "loads" : "probably loads")} this one.";

        string other = live.Entry.Size == 0 ? "an empty save" : "the save";
        return $"{copies.Count} saves for this game. {app} {(known ? "loads" : "probably loads")} {other} in " +
               $"{live.Emulator.Where}/{live.Entry.RelativePath} instead, so changes here won't show in the game.";
    }

    static ScanState.KnownFile Identify(byte[] data, SaveEntry entry, long modified)
    {
        if (!SaveUtil.TryGetSaveFile(data, out var sav, entry.Name))
            return new ScanState.KnownFile(entry.Size, modified, null, null, null, entry.Name, entry.RelativePath);
        return new ScanState.KnownFile(entry.Size, modified, GameName(sav), sav.OT,
            $"{sav.PlayedHours}:{sav.PlayedMinutes:D2}", entry.Name, entry.RelativePath);
    }

    public static string GameName(SaveFile sav)
    {
        var list = GameInfo.Strings.gamelist;
        int v = (int)sav.Version;
        var name = v > 0 && v < list.Length && !string.IsNullOrWhiteSpace(list[v]) ? list[v] : sav.Version.ToString();
        return $"Pokémon {name}";
    }
}
