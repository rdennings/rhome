namespace SpikeB.Core;

/// <summary>
/// Where an emulator keeps its saves by default, and which files in there are saves.
/// </summary>
/// <param name="StoragePath">Path under the device's shared storage (the part after <c>/sdcard/</c>).</param>
/// <param name="Extensions">File extensions that are saves (lower case, with the dot).</param>
/// <param name="FileNames">Exact file names that are saves (Switch saves have no extension).</param>
/// <param name="MaxDepth">How many levels of subfolders to search.</param>
public sealed record EmulatorProfile(
    string Key,
    string Name,
    string PackageName,
    string StoragePath,
    string[] Extensions,
    string[] FileNames,
    int MaxDepth)
{
    public bool IsSaveFile(string fileName) =>
        FileNames.Contains(fileName, StringComparer.Ordinal) ||
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static readonly EmulatorProfile MelonDS = new(
        "melonds", "melonDS", "me.magnum.melonds",
        "Android/data/me.magnum.melonds/files/saves",
        [".sav", ".dsv"], [], MaxDepth: 0);

    public static readonly EmulatorProfile RetroArch = new(
        "retroarch", "RetroArch", "com.retroarch",
        "Android/media/com.retroarch/RetroArch/saves",
        [".srm", ".sav"], [], MaxDepth: 2);

    // Not in the MVP (Switch is v1.1), but it's the other emulator that keeps saves in Android/data.
    // nand/user/save/<0000…>/<user id>/<title id>/main
    public static readonly EmulatorProfile Eden = new(
        "eden", "Eden", "dev.eden.eden_emulator",
        "Android/data/dev.eden.eden_emulator/files/nand/user/save",
        [], ["main"], MaxDepth: 3);

    public static readonly IReadOnlyList<EmulatorProfile> All = [MelonDS, RetroArch, Eden];
}
