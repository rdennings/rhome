namespace Rhome.Core;

/// <summary>A save file in one of an emulator's folders, as the platform layer found it.</summary>
/// <param name="RelativePath">The file's path inside <paramref name="Folder"/>, separated by <c>/</c>.</param>
public sealed record EmulatorSaveFile(
    KnownEmulator Emulator,
    EmulatorFolder Folder,
    string RelativePath,
    long Size,
    DateTimeOffset LastModified)
{
    /// <summary>The file name without its folders.</summary>
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>The file name without its extension: for these emulators, the ROM's name.</summary>
    public string GameName => FileName.LastIndexOf('.') is > 0 and var dot ? FileName[..dot] : FileName;
}

/// <summary>How sure rhome is about which copy of a save the emulator loads.</summary>
public enum LoadCertainty
{
    /// <summary>The game has only this one save.</summary>
    OnlyCopy,

    /// <summary>The emulator's <see cref="KnownEmulator.LoadOrder"/> settles it.</summary>
    Known,

    /// <summary>A guess: the newest non-empty copy. Say so rather than act on it silently.</summary>
    Guess,
}

/// <summary>One game's saves for one emulator: usually one file, sometimes a copy in each of several folders.</summary>
/// <param name="Copies">Every copy, the loaded one first.</param>
/// <param name="Loaded">The copy the emulator loads; changes to the others won't show in the game.</param>
public sealed record GameSaves(IReadOnlyList<EmulatorSaveFile> Copies, EmulatorSaveFile Loaded, LoadCertainty Certainty)
{
    public bool HasSeveralCopies => Copies.Count > 1;
}

/// <summary>
/// Finds the saves in emulator folders and works out which copy each emulator loads (PLAN.md §2 "One game, two
/// saves"). Works on file listings only: listing and reading folders is the platform layer's job.
/// </summary>
public static class SaveCopies
{
    /// <summary>
    /// Groups save files into games, and picks the copy each emulator loads. Files that aren't saves for their
    /// emulator and folder (<see cref="KnownEmulator.IsSaveFile"/>) are left out.
    /// </summary>
    /// <remarks>
    /// Files are copies of one game when the same emulator has them, on the same platform, in different folders,
    /// with the same name apart from extension and case: these emulators name a save after its ROM. Two files with
    /// one name inside a single folder say nothing (they could be per-core subfolders, or saves that are all called
    /// <c>main</c>), so they stay separate games.
    /// </remarks>
    public static IReadOnlyList<GameSaves> Resolve(IEnumerable<EmulatorSaveFile> files)
    {
        var saves = files.Where(f => f.Emulator.IsSaveFile(f.Folder, f.RelativePath)).Distinct().ToList();
        var games = new List<GameSaves>();

        foreach (var sameName in saves.GroupBy(f => (f.Emulator.Key, f.Folder.Platform, Name: f.GameName.ToUpperInvariant())))
        {
            var copies = sameName.ToList();
            if (copies.GroupBy(f => f.Folder.Key).Any(g => g.Count() > 1))
                games.AddRange(copies.Select(f => new GameSaves([f], f, LoadCertainty.OnlyCopy)));
            else
                games.Add(Pick(copies));
        }
        return games;
    }

    static GameSaves Pick(List<EmulatorSaveFile> copies)
    {
        if (copies.Count == 1)
            return new GameSaves(copies, copies[0], LoadCertainty.OnlyCopy);

        // The load order counts only when it names every folder involved; an empty file in a folder that comes
        // first still wins (melonDS loads an empty save next to the ROM over a real one in its own folder).
        var order = copies[0].Emulator.LoadOrder;
        bool known = copies.All(f => order.Contains(f.Folder.Key));
        var ranked = known
            ? copies.OrderBy(f => IndexOf(order, f.Folder.Key)).ToList()
            : copies.OrderByDescending(f => f.Size > 0).ThenByDescending(f => f.LastModified).ToList();

        return new GameSaves(ranked, ranked[0], known ? LoadCertainty.Known : LoadCertainty.Guess);
    }

    static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == item)
                return i;
        return -1;
    }
}
