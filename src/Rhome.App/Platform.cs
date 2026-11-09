using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>What each platform head (Rhome.Android, later Linux) hands to the shared UI.</summary>
public interface IAppPlatform
{
    /// <summary>One line about the device, for the screen and the log.</summary>
    string Description { get; }

    /// <summary>The app's own storage: the bank and the backups.</summary>
    AppStorage Storage { get; }

    /// <summary>
    /// The save folders rhome may use, including ones that can't be reached right now (see
    /// <see cref="ISaveFolder.IsAvailable"/>). On Android: the folders the user granted, kept across restarts.
    /// </summary>
    IReadOnlyList<ISaveFolder> Folders();

    /// <summary>Asks the user for a save folder. Returns a line for the log.</summary>
    Task<string> AddFolderAsync();
}

/// <summary>The running platform. Set by the head before the UI starts.</summary>
public static class Host
{
    public static IAppPlatform? Platform { get; set; }
}
