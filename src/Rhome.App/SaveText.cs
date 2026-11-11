using Rhome.Core;

namespace Rhome.App;

/// <summary>How a found game reads in the app.</summary>
public static class SaveText
{
    /// <summary>The game and trainer, e.g. "Platinum (Eve)".</summary>
    public static string Title(FoundGame game) => $"{EnglishNames.Game(game.Save.Game)} ({game.Save.Trainer})";

    /// <summary>
    /// The second line: play time and where it is, then which copy the emulator loads when there's more than one
    /// (PLAN.md §2 "One game, two saves"), so nobody edits the copy the game ignores.
    /// </summary>
    public static string Detail(FoundGame game)
    {
        var where = game.Loaded.Where;
        var text = $"{game.Save.PlayTime} played · {where.Name} · {game.Loaded.Entry.RelativePath}";
        if (!game.Save.IsSupported)
            text += $" · Gen {game.Save.Generation}: not supported yet";
        if (!game.HasSeveralCopies)
            return text;

        var emulator = where.Emulator?.Name ?? "The emulator";
        var loads = game.Certainty == LoadCertainty.Known ? "loads" : "probably loads";
        text += $"\n{game.Copies.Count} copies: {emulator} {loads} the one {Place(where)}.";
        if (game.LoadedCopyIsNotASave)
            text += $" ⚠ That copy is empty, so the game won't see the save {Place(game.Copies.First(c => c.Save is not null).Where)}.";
        return text;
    }

    /// <summary>
    /// One copy of a game, for the opened save: where it is, how big, when it changed, and whether it's the one the
    /// emulator loads.
    /// </summary>
    public static string Copy(FoundGame game, ScannedFile copy)
    {
        var what = copy.Save is null ? "not a save (empty?)" : $"{copy.Save.PlayTime} played";
        var text = $"{copy.Where.Name} · {copy.Entry.RelativePath} · {Size(copy.Entry.Size)} · " +
                   $"changed {copy.Entry.LastModified.ToLocalTime():g} · {what}";
        if (!game.HasSeveralCopies)
            return text;
        return ReferenceEquals(copy, game.Loaded)
            ? text + (game.Certainty == LoadCertainty.Known ? " · the emulator loads this one" : " · the emulator probably loads this one")
            : text + " · the emulator ignores this one";
    }

    /// <summary>A save the launch check found gone.</summary>
    public static string Gone(ScannedFile file) =>
        $"{file.Entry.RelativePath} is gone from {file.Where.Name}. If it moved, tap Scan now.";

    /// <summary>Folders that couldn't be reached; what was known about their saves stays.</summary>
    public static string CantReach(IEnumerable<string> folders) =>
        $"Can't reach {string.Join(", ", folders)} right now, so its saves show as they were. " +
        "Is the SD card in? Tap Set up again to pick it again.";

    static string Size(long bytes) => bytes switch
    {
        0 => "empty",
        < 1024 => $"{bytes} bytes",
        _ => $"{bytes / 1024} KB",
    };

    static string Place(ScanFolder folder) => folder.Folder is { } f ? (f.RomFolder ? f.Name : "in " + f.Name) : "in " + folder.Name;
}
