using Android.Content;
using Android.Content.PM;
using Android.OS.Storage;
using Android.Provider;
using Rhome.App;
using Rhome.Core;
using Rhome.FileSystem;
using AndroidBuild = Android.OS.Build;
using Uri = Android.Net.Uri;

namespace Rhome.Android;

/// <summary>
/// Android for the shared UI. Save folders come through the system folder picker (Storage Access Framework), as Spike B
/// proved on the Thor (spikes/spike-b): the picker opens inside the emulator's folder, the user picks it once, and
/// rhome keeps a persistable grant to it, which survives restarts, reboots and app updates.
/// </summary>
sealed class AndroidPlatform(MainActivity activity) : IAppPlatform
{
    const string ExternalStorage = "com.android.externalstorage.documents";
    const ActivityFlags ReadWrite = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;

    ContentResolver Resolver => activity.ContentResolver!;

    public string Description { get; } =
        $"Android {AndroidBuild.VERSION.Release} ({AndroidBuild.Model}), security patch {AndroidBuild.VERSION.SecurityPatch}";

    public Platform Os => Platform.Android;

    /// <summary>
    /// The app-specific folder on shared storage (<c>Android/data/&lt;app&gt;/files</c>). Like internal storage it needs
    /// no permission and goes away with the app, but adb can read it from a non-debuggable build, which is how backups
    /// get checked on a device.
    /// </summary>
    public AppStorage Storage { get; } = new((activity.GetExternalFilesDir(null) ?? activity.FilesDir!).AbsolutePath);

    /// <summary>Whether the app is installed. Only works for packages listed under &lt;queries&gt; in the manifest.</summary>
    public bool IsInstalled(EmulatorApp app)
    {
        var packages = activity.PackageManager!;
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                packages.GetPackageInfo(app.Id, PackageManager.PackageInfoFlags.Of(0L));
            else
#pragma warning disable CA1422 // the only way before Android 13
                packages.GetPackageInfo(app.Id, 0);
#pragma warning restore CA1422
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public async Task<PickedLocation?> PickFolderAsync(EmulatorFolder? startAt)
    {
        var expected = startAt is null ? null : DocumentId(startAt);
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        if (expected is not null && OperatingSystem.IsAndroidVersionAtLeast(26)) // older pickers start where they like
            intent.PutExtra(DocumentsContract.ExtraInitialUri, DocumentsContract.BuildDocumentUri(ExternalStorage, expected));
        intent.AddFlags(ReadWrite | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

        if (await PickAsync(intent) is not { } tree)
            return null;
        var picked = DocumentsContract.GetTreeDocumentId(tree);
        return new PickedLocation(tree.ToString()!, SafFolder.Describe(tree),
            expected is null ? null : tree.Authority == ExternalStorage && picked == expected);
    }

    public async Task<PickedLocation?> PickSaveFileAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*"); // saves have no MIME type of their own
        intent.AddFlags(ReadWrite | ActivityFlags.GrantPersistableUriPermission);

        if (await PickAsync(intent) is not { } document)
            return null;
        return new PickedLocation(document.ToString()!, new SafFile(Resolver, document).DisplayName);
    }

    /// <summary>Starts a picker and keeps read and write access to what the user picks.</summary>
    async Task<Uri?> PickAsync(Intent intent)
    {
        var (result, data) = await activity.StartForResultAsync(intent);
        if (result != global::Android.App.Result.Ok || data?.Data is not { } uri)
            return null;
        try
        {
            Resolver.TakePersistableUriPermission(uri, ReadWrite);
        }
        catch (Java.Lang.SecurityException ex)
        {
            throw new InvalidOperationException($"Android wouldn't let rhome keep access to {uri.LastPathSegment}: {ex.Message}", ex);
        }
        return uri;
    }

    public ISaveFolder Open(string id)
    {
        var uri = Uri.Parse(id)!;
        return IsTree(uri) ? new SafFolder(Resolver, uri) : new SafFile(Resolver, uri);
    }

    public void Release(string id)
    {
        try
        {
            Resolver.ReleasePersistableUriPermission(Uri.Parse(id)!, ReadWrite);
        }
        catch (Java.Lang.SecurityException)
        {
            // Already gone.
        }
    }

    /// <summary>
    /// The folder's document ID in the external storage provider: "&lt;volume&gt;:&lt;path&gt;", where the volume is
    /// "primary" for internal storage and the card's UUID (e.g. 12A8-6020) for an SD card. Null when there's no SD card.
    /// </summary>
    string? DocumentId(EmulatorFolder folder)
    {
        var volume = folder.Area switch
        {
            StorageArea.Shared => "primary",
            StorageArea.SdCard => SdCardUuid(),
            _ => null,
        };
        return volume is null ? null : $"{volume}:{folder.Path}";
    }

    /// <summary>The UUID of the first mounted removable volume (the SD card), or null if there's none.</summary>
    string? SdCardUuid()
    {
        var storage = (StorageManager)activity.GetSystemService(Context.StorageService)!;
        return storage.StorageVolumes
            .FirstOrDefault(v => v.IsRemovable && v.Uuid is not null && v.State == global::Android.OS.Environment.MediaMounted)
            ?.Uuid;
    }

    static bool IsTree(Uri uri)
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
