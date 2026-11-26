using Avalonia.Controls;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>What each platform head (Rhome.Android, later Linux) hands to the shared UI.</summary>
public interface IAppPlatform
{
    /// <summary>One line about the device, for the screen and the log.</summary>
    string Description { get; }

    /// <summary>Which platform this is, to pick the emulator apps and folders that apply.</summary>
    Platform Os { get; }

    /// <summary>The app's own storage: the bank, the backups and the setup.</summary>
    AppStorage Storage { get; }

    /// <summary>Whether this emulator app is installed.</summary>
    bool IsInstalled(EmulatorApp app);

    /// <summary>
    /// Asks the user for a folder, starting the picker inside <paramref name="startAt"/> when there is one. On Android,
    /// rhome keeps access to the folder across restarts.
    /// </summary>
    /// <returns>The folder, or null if the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The user picked a folder, but rhome couldn't keep access to it.</exception>
    Task<PickedLocation?> PickFolderAsync(EmulatorFolder? startAt);

    /// <summary>Asks the user for one save file, for an emulator rhome doesn't know (PLAN.md §2 "Fallback").</summary>
    /// <returns>The file, or null if the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The user picked a file, but rhome couldn't keep access to it.</exception>
    Task<PickedLocation?> PickSaveFileAsync();

    /// <summary>
    /// A folder or file the user picked, by its <see cref="PickedLocation.Id"/>. A picked file is a folder holding just
    /// that file. When access is gone, <see cref="ISaveFolder.IsAvailable"/> says so.
    /// </summary>
    ISaveFolder Open(string id);

    /// <summary>Gives up access to a folder or file rhome no longer uses.</summary>
    void Release(string id);

    /// <summary>The device's second screen (the Thor's bottom screen, issue #49), or null when the platform has none.</summary>
    ISecondScreen? SecondScreen => null;
}

/// <summary>
/// A second screen rhome can draw on (issue #49): the bottom screen of a two-screen handheld such as the Ayn Thor.
/// While rhome is in the background the platform takes it down, so an emulator gets the screen back, and puts it back
/// when rhome returns.
/// </summary>
public interface ISecondScreen
{
    /// <summary>Whether there's a second screen now. The layout follows this, not whether it's showing this moment.</summary>
    bool IsPresent { get; }

    /// <summary>A second screen was connected or went away. Raised on the UI thread.</summary>
    event Action? PresenceChanged;

    /// <summary>Puts <paramref name="content"/> on the second screen, or clears it.</summary>
    void Show(Control? content);
}

/// <summary>A folder or file the user picked.</summary>
/// <param name="Id">What the platform uses to open it again, also after a restart (on Android, the content URI).</param>
/// <param name="DisplayName">Where it is, as a person would say it, e.g. "SD card 12A8-6020/ROMS/nds".</param>
/// <param name="IsWhereAsked">Whether it's the folder the picker started in; null when the picker couldn't be started
/// in a particular folder.</param>
public sealed record PickedLocation(string Id, string DisplayName, bool? IsWhereAsked = null);

/// <summary>The running platform. Set by the head before the UI starts.</summary>
public static class Host
{
    public static IAppPlatform? Platform { get; set; }
}
