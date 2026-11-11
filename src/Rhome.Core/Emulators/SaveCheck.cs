using System.Diagnostics;

namespace Rhome.Core;

/// <summary>What the launch check found about a save file rhome already knew.</summary>
public enum SaveChange
{
    /// <summary>The same size and time as when it was last read.</summary>
    Same,

    /// <summary>Changed on disk since it was last read (played and saved in the emulator, say), so it was read again.</summary>
    Changed,

    /// <summary>The file is gone: deleted, renamed or moved. Scan now finds it again if it moved.</summary>
    Gone,

    /// <summary>
    /// Its folder can't be reached (SD card out, access taken away), or the file couldn't be checked or read. What was
    /// known about it stays, and the next check tries again.
    /// </summary>
    Unreachable,
}

/// <summary>A known save file and what the check found.</summary>
/// <param name="File">The file as it is now: read again when it changed, as it was otherwise.</param>
public sealed record CheckedFile(ScannedFile File, SaveChange Change);

/// <param name="Files">Every file that was checked, in the order given.</param>
/// <param name="Games">The games again, from the files that are still there.</param>
/// <param name="Problems">Files that couldn't be checked or read, one line each.</param>
/// <param name="Took">How long the check took (PLAN.md §2: about 150 ms on the Thor).</param>
public sealed record SaveCheckResult(IReadOnlyList<CheckedFile> Files, IReadOnlyList<FoundGame> Games, IReadOnlyList<string> Problems, TimeSpan Took)
{
    public IEnumerable<ScannedFile> In(SaveChange change) => Files.Where(f => f.Change == change).Select(f => f.File);

    /// <summary>Whether anything differs from what was known (a file changed or went), so it's worth storing.</summary>
    public bool AnythingChanged => Files.Any(f => f.Change is SaveChange.Changed or SaveChange.Gone);
}

/// <summary>
/// The launch check (PLAN.md §2 "Loading saves"): looks only at the save files rhome already knows, never lists a
/// folder. Each folder is asked once whether it can be reached, then each file for its size and time; only files that
/// changed are read again. New saves need a full <see cref="SaveScan"/> (Scan now): listing a big ROM folder took
/// 0.4–2 s in Spike B.
/// </summary>
public static class SaveCheck
{
    /// <summary>Checks <paramref name="known"/>. Never throws for a folder or file it can't reach: it reports it.</summary>
    public static SaveCheckResult Run(IEnumerable<ScannedFile> known)
    {
        var clock = Stopwatch.StartNew();
        var reachable = new Dictionary<ISaveFolder, bool>(ReferenceEqualityComparer.Instance);
        var files = new List<CheckedFile>();
        var problems = new List<string>();

        foreach (var file in known)
        {
            var access = file.Where.Access;
            if (!reachable.TryGetValue(access, out var up))
                reachable[access] = up = SaveScan.Reachable(access);
            files.Add(up ? Check(file, problems) : new CheckedFile(file, SaveChange.Unreachable));
        }

        var games = SaveScan.Group(files.Where(f => f.Change != SaveChange.Gone).Select(f => f.File));
        return new SaveCheckResult(files, games, problems, clock.Elapsed);
    }

    static CheckedFile Check(ScannedFile file, List<string> problems)
    {
        FileStamp? now;
        try
        {
            now = file.Where.Access.Stat(file.Entry.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            problems.Add($"couldn't check {file.Entry.RelativePath} in {file.Where.Name}: {ex.Message}");
            return new CheckedFile(file, SaveChange.Unreachable);
        }

        if (now is not { } stamp)
            return new CheckedFile(file, SaveChange.Gone);
        if (stamp == file.Entry.Stamp)
            return new CheckedFile(file, SaveChange.Same);

        var entry = file.Entry with { Size = stamp.Size, LastModified = stamp.LastModified };
        var (save, problem) = SaveScan.TryRead(file.Where, entry);
        if (problem is not null)
        {
            // Keep the old stamp, so the next check reads it again.
            problems.Add(problem);
            return new CheckedFile(file, SaveChange.Unreachable);
        }
        return new CheckedFile(file with { Entry = entry, Save = save }, SaveChange.Changed);
    }
}
