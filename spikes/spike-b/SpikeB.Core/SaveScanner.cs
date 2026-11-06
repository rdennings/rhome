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

public sealed record ScanResult(
    IReadOnlyList<FoundSave> Saves,
    int FilesListed,
    int CandidateFiles,
    int FilesRead,
    TimeSpan ListTime,
    TimeSpan ReadTime,
    IReadOnlyList<string> Errors);

/// <summary>What the last scan saw, so the next one only reads new or changed files.</summary>
public sealed class ScanState
{
    public sealed record KnownFile(long Size, long LastModifiedMs, string? Game, string? Trainer, string? PlayTime);

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
    /// Lists every folder, reads only files that are new or changed since <paramref name="state"/>,
    /// and updates <paramref name="state"/> in place. Saves in a folder scanned for the first time
    /// are reported as Unchanged, so the first launch doesn't flag everything as new.
    /// </summary>
    public static ScanResult Scan(IEnumerable<(EmulatorProfile Emulator, ISaveFolder Folder)> sources, ScanState state)
    {
        var saves = new List<FoundSave>();
        var errors = new List<string>();
        var seen = new HashSet<string>();
        var listedEmulators = new HashSet<string>();
        var saveFiles = new List<(EmulatorProfile Emulator, SaveEntry Entry)>();
        int listed = 0, candidates = 0, read = 0;
        var listTime = TimeSpan.Zero;
        var readTime = TimeSpan.Zero;

        foreach (var (emulator, folder) in sources)
        {
            bool firstScan = !state.ScannedEmulators.Contains(emulator.Key);
            var sw = Stopwatch.StartNew();
            IReadOnlyList<SaveEntry> entries;
            try { entries = folder.List(emulator.MaxDepth); }
            catch (Exception ex)
            {
                errors.Add($"{emulator.Name}: can't list folder ({ex.GetType().Name}: {ex.Message})");
                continue;
            }
            listTime += sw.Elapsed;
            listed += entries.Count;
            listedEmulators.Add(emulator.Key);

            foreach (var entry in entries)
            {
                if (!emulator.IsSaveFile(entry.Name) || entry.Size > MaxSaveSize)
                    continue;
                // Empty saves aren't Pokémon saves yet, but the emulator may load one in place of
                // the real save, so keep them for the "two saves for one game" check.
                saveFiles.Add((emulator, entry));
                if (entry.Size <= 0)
                    continue;
                candidates++;
                string key = $"{emulator.Key}|{entry.Id}";
                seen.Add(key);
                long modified = entry.LastModified.ToUnixTimeMilliseconds();

                var known = state.Files.GetValueOrDefault(key);
                SaveStatus status;
                if (known is not null && known.Size == entry.Size && known.LastModifiedMs == modified)
                {
                    status = SaveStatus.Unchanged;
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

                if (known.Game is not null)
                    saves.Add(new FoundSave(emulator, entry, status, known.Game, known.Trainer ?? "", known.PlayTime ?? ""));
            }
            state.ScannedEmulators.Add(emulator.Key);
        }

        // Forget files that are gone, but only for emulators we could list this time.
        foreach (var key in state.Files.Keys.ToList())
        {
            if (!seen.Contains(key) && listedEmulators.Contains(key[..key.IndexOf('|')]))
                state.Files.Remove(key);
        }

        var ordered = saves.Select(s => s with { Note = DuplicateNote(s, saveFiles) })
                           .OrderByDescending(s => s.Status != SaveStatus.Unchanged)
                           .ThenByDescending(s => s.Entry.LastModified)
                           .ToList();
        return new ScanResult(ordered, listed, candidates, read, listTime, readTime, errors);
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
            return new ScanState.KnownFile(entry.Size, modified, null, null, null);
        return new ScanState.KnownFile(entry.Size, modified, GameName(sav), sav.OT,
            $"{sav.PlayedHours}:{sav.PlayedMinutes:D2}");
    }

    public static string GameName(SaveFile sav)
    {
        var list = GameInfo.Strings.gamelist;
        int v = (int)sav.Version;
        var name = v > 0 && v < list.Length && !string.IsNullOrWhiteSpace(list[v]) ? list[v] : sav.Version.ToString();
        return $"Pokémon {name}";
    }
}
