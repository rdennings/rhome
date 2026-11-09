using Android.Content;
using Android.Provider;
using Rhome.App;
using Rhome.Core;
using Rhome.FileSystem;
using AndroidBuild = Android.OS.Build;

namespace Rhome.Android;

/// <summary>
/// Android for the shared UI. Save folders come through the system folder picker (Storage Access Framework), as Spike B
/// proved on the Thor (spikes/spike-b): the user picks a folder once, and rhome keeps a persistable grant to it, which
/// survives restarts, reboots and app updates. Android keeps the list of grants, so rhome doesn't store its own.
/// </summary>
sealed class AndroidPlatform(MainActivity activity) : IAppPlatform
{
    ContentResolver Resolver => activity.ContentResolver!;

    public string Description { get; } =
        $"Android {AndroidBuild.VERSION.Release} ({AndroidBuild.Model}), security patch {AndroidBuild.VERSION.SecurityPatch}";

    /// <summary>
    /// The app-specific folder on shared storage (<c>Android/data/&lt;app&gt;/files</c>). Like internal storage it needs
    /// no permission and goes away with the app, but adb can read it from a non-debuggable build, which is how backups
    /// get checked on a device.
    /// </summary>
    public AppStorage Storage { get; } = new((activity.GetExternalFilesDir(null) ?? activity.FilesDir!).AbsolutePath);

    public IReadOnlyList<ISaveFolder> Folders() =>
        Resolver.PersistedUriPermissions
            .Where(p => p.IsReadPermission && p.IsWritePermission && p.Uri is not null && IsTree(p.Uri))
            .OrderBy(p => p.PersistedTime)
            .Select(p => (ISaveFolder)new SafFolder(Resolver, p.Uri!))
            .ToList();

    public async Task<string> AddFolderAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

        var (result, data) = await activity.StartForResultAsync(intent);
        if (result != global::Android.App.Result.Ok || data?.Data is not { } tree)
            return "folder picker cancelled; nothing granted";

        try
        {
            Resolver.TakePersistableUriPermission(tree, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Java.Lang.SecurityException ex)
        {
            return $"picked {SafFolder.Describe(tree)}, but Android wouldn't let rhome keep access: {ex.Message}";
        }
        return $"granted {SafFolder.Describe(tree)}";
    }

    static bool IsTree(global::Android.Net.Uri uri)
    {
        try
        {
            return DocumentsContract.GetTreeDocumentId(uri) is not null;
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            return false; // a single document, not a folder
        }
    }
}
