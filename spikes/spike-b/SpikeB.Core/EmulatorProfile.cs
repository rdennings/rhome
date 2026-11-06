namespace SpikeB.Core;

/// <summary>
/// Where an emulator keeps its saves, and which files in there are saves.
/// </summary>
/// <param name="StoragePath">Path under the storage volume (the part after <c>/sdcard/</c> or the SD card's root).</param>
/// <param name="Extensions">File extensions that are saves (lower case, with the dot).</param>
/// <param name="FileNames">Exact file names that are saves (Switch saves have no extension).</param>
/// <param name="MaxDepth">How many levels of subfolders to search.</param>
/// <param name="OnSdCard">The folder is on the removable SD card, not internal shared storage.</param>
/// <param name="LoadPriority">
/// When the same game has a save in more than one of this emulator's folders, the emulator loads the
/// one from the folder with the highest priority.
/// </param>
public sealed record EmulatorProfile(
    string Key,
    string Name,
    string PackageName,
    string StoragePath,
    string[] Extensions,
    string[] FileNames,
    int MaxDepth,
    bool OnSdCard = false,
    int LoadPriority = 0)
{
    public bool IsSaveFile(string fileName) =>
        FileNames.Contains(fileName, StringComparer.Ordinal) ||
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder as a person would describe it.</summary>
    public string Where => OnSdCard ? $"SD card/{StoragePath}" : $"/sdcard/{StoragePath}";

    public static readonly EmulatorProfile MelonDS = new(
        "melonds", "melonDS", "me.magnum.melonds",
        "Android/data/me.magnum.melonds/files/saves",
        [".sav", ".dsv"], [], MaxDepth: 0);

    // melonDS saves next to the ROM when it can write the ROM's folder, and then loads that file
    // instead of the one in Android/data. ROMS/nds is where the Thor's frontend keeps DS ROMs; the
    // user can pick any other folder.
    public static readonly EmulatorProfile MelonDSRoms = new(
        "melonds-roms", "melonDS (next to ROMs)", "me.magnum.melonds",
        "ROMS/nds",
        [".sav", ".dsv"], [], MaxDepth: 0, OnSdCard: true, LoadPriority: 1);

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

    public static readonly IReadOnlyList<EmulatorProfile> All = [MelonDS, MelonDSRoms, RetroArch, Eden];
}
