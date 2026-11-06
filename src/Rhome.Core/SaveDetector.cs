using PKHeX.Core;

namespace Rhome.Core;

/// <summary>A save file recognized from its bytes.</summary>
/// <param name="Game">The game, as precisely as the save tells (Gen 3 Ruby/Sapphire saves may only say "RS").</param>
/// <param name="Generation">3 for GBA games, 4 and 5 for DS games, and so on.</param>
/// <param name="IsSupported">True for the games rhome can work with today (the MVP's Tier 1: every GBA and DS game).</param>
public sealed record DetectedSave(
    GameVersion Game,
    byte Generation,
    string Trainer,
    bool IsSupported,
    SaveFile Save);

/// <summary>
/// Identifies a game from the save itself (PLAN.md §2, "save-first"), without trusting the file name.
/// Works on bytes only: finding and reading files is the platform layer's job.
/// </summary>
public static class SaveDetector
{
    /// <summary>
    /// Returns what the bytes are, or null when they aren't a save PKHeX recognizes.
    /// The caller's bytes are copied first, so nothing done to the returned save can change them.
    /// </summary>
    public static DetectedSave? Detect(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || !SaveUtil.IsSizeValid(data.Length))
            return null;

        if (!SaveUtil.TryGetSaveFile(data.ToArray(), out var sav) || sav is null)
            return null;

        return new DetectedSave(sav.Version, sav.Generation, sav.OT, IsSupported(sav), sav);
    }

    /// <summary>
    /// Tier 1 = the mainline GBA and DS games: Ruby to Emerald and FireRed/LeafGreen (SAV3), Diamond to
    /// SoulSilver (SAV4), Black to White 2 (SAV5). Spin-offs with their own formats (Colosseum, XD,
    /// Box, Battle Revolution, Ranch) are separate classes in PKHeX, so they don't match.
    /// </summary>
    public static bool IsSupported(SaveFile sav) => sav is SAV3 or SAV4 or SAV5;
}
