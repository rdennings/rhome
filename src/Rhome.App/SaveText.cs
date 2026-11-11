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

    static string Place(ScanFolder folder) => folder.Folder is { } f ? (f.RomFolder ? f.Name : "in " + f.Name) : "in " + folder.Name;
}
