using SpikeB.Core;

namespace SpikeB.App;

/// <summary>How a platform gets at an emulator's save folder.</summary>
public interface IFolderAccess
{
    bool IsInstalled(EmulatorProfile emulator);

    /// <summary>The folder the user granted earlier, or null (with the reason) if there's none or it was lost.</summary>
    ISaveFolder? GetFolder(EmulatorProfile emulator, out string status);

    /// <summary>Asks the user for the emulator's save folder. Returns a message to show.</summary>
    Task<string> RequestAccessAsync(EmulatorProfile emulator);
}

/// <summary>What each platform head hands to the shared UI.</summary>
public static class Host
{
    public static IFolderAccess? Folders { get; set; }

    /// <summary>The app's own folder: scan state, backups and logs for adb to pull.</summary>
    public static string DataDir { get; set; } = Path.Combine(Path.GetTempPath(), "rhome-spike-b");

    public static string Platform { get; set; } = Environment.OSVersion.ToString();

    /// <summary>
    /// Puts a control on a second screen (the Ayn Thor's bottom screen) and returns true, or returns
    /// false when there's no second screen. Null on platforms that can't do it.
    /// </summary>
    public static Func<Avalonia.Controls.Control, bool>? ShowOnSecondScreen { get; set; }
}
