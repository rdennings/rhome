using System.Text.Json;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using SpikeB.App;
using SpikeB.Core;
using Uri = Android.Net.Uri;

namespace SpikeB.Android;

/// <summary>
/// Emulator save folders through the Storage Access Framework. The user picks the folder once in
/// the system picker (opened right at the emulator's folder); we keep a persistable grant to it.
/// </summary>
sealed class SafFolderAccess(MainActivity activity, string dataDir) : IFolderAccess
{
    const string ExternalStorage = "com.android.externalstorage.documents";

    ContentResolver Resolver => activity.ContentResolver!;
    string GrantsPath => Path.Combine(dataDir, "grants.json");

    public bool IsInstalled(EmulatorProfile emulator)
    {
        try
        {
            activity.PackageManager!.GetPackageInfo(emulator.PackageName, PackageManager.PackageInfoFlags.Of(0L));
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public ISaveFolder? GetFolder(EmulatorProfile emulator, out string status)
    {
        if (!LoadGrants().TryGetValue(emulator.Key, out var uriText))
        {
            status = "no access yet";
            return null;
        }
        var tree = Uri.Parse(uriText)!;
        bool held = Resolver.PersistedUriPermissions.Any(p =>
            p.Uri!.Equals(tree) && p.IsReadPermission && p.IsWritePermission);
        if (!held)
        {
            status = "access was lost (permission no longer held); pick the folder again";
            return null;
        }
        status = $"access OK: {Describe(tree)}";
        return new SafSaveFolder(Resolver, tree);
    }

    public async Task<string> RequestAccessAsync(EmulatorProfile emulator)
    {
        // Document IDs are "<volume>:<path>": "primary" for internal storage, the volume's UUID
        // (e.g. 12A8-6020) for an SD card.
        string? volume = emulator.OnSdCard ? SdCardUuid() : "primary";
        string? expected = volume is null ? null : $"{volume}:{emulator.StoragePath}";
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        if (expected is not null)
            intent.PutExtra(DocumentsContract.ExtraInitialUri, DocumentsContract.BuildDocumentUri(ExternalStorage, expected));
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

        var (result, data) = await activity.StartForResultAsync(intent);
        if (result != global::Android.App.Result.Ok || data?.Data is not { } tree)
            return "picker cancelled, nothing granted";

        try
        {
            Resolver.TakePersistableUriPermission(tree, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Java.Lang.SecurityException ex)
        {
            return $"picked {Describe(tree)} but couldn't keep access: {ex.Message}";
        }

        var grants = LoadGrants();
        grants[emulator.Key] = tree.ToString()!;
        File.WriteAllText(GrantsPath, JsonSerializer.Serialize(grants));

        var picked = DocumentsContract.GetTreeDocumentId(tree);
        return picked == expected
            ? $"granted {Describe(tree)} (the expected folder)"
            : $"granted {Describe(tree)}, NOT the expected {expected ?? emulator.Where}";
    }

    /// <summary>The UUID of the first mounted removable volume (the SD card), or null if there's none.</summary>
    string? SdCardUuid()
    {
        var storage = (global::Android.OS.Storage.StorageManager)activity.GetSystemService(Context.StorageService)!;
        return storage.StorageVolumes
            .FirstOrDefault(v => v.IsRemovable && v.Uuid is not null && v.State == global::Android.OS.Environment.MediaMounted)
            ?.Uuid;
    }

    static string Describe(Uri tree) => $"{tree.Authority} · {DocumentsContract.GetTreeDocumentId(tree)}";

    Dictionary<string, string> LoadGrants()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(GrantsPath)) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }
}

/// <summary>A granted document tree. Files are opened through file descriptors so .NET streams work on them.</summary>
sealed class SafSaveFolder(ContentResolver resolver, Uri tree) : ISaveFolder
{
    static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    ];

    public IReadOnlyList<SaveEntry> List(int maxDepth)
    {
        var result = new List<SaveEntry>();
        Walk(DocumentsContract.GetTreeDocumentId(tree)!, "", maxDepth, result);
        return result;
    }

    void Walk(string parentId, string prefix, int depthLeft, List<SaveEntry> result)
    {
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parentId)!;
        var dirs = new List<(string Id, string Name)>();
        using (var c = resolver.Query(children, Columns, null, null, null))
        {
            if (c is null)
                return;
            while (c.MoveToNext())
            {
                string id = c.GetString(0)!, name = c.GetString(1) ?? "", mime = c.GetString(2) ?? "";
                if (mime == DocumentsContract.Document.MimeTypeDir)
                {
                    if (depthLeft > 0)
                        dirs.Add((id, name));
                    continue;
                }
                long size = c.IsNull(3) ? 0 : c.GetLong(3);
                long modified = c.IsNull(4) ? 0 : c.GetLong(4);
                result.Add(new SaveEntry(id, name, prefix + name, size, DateTimeOffset.FromUnixTimeMilliseconds(modified)));
            }
        }
        foreach (var (id, name) in dirs)
            Walk(id, prefix + name + "/", depthLeft - 1, result);
    }

    public bool IsAvailable() => Query(DocumentsContract.GetTreeDocumentId(tree)!) is not null;

    public SaveEntry? Find(string id, string? relativePath)
    {
        var row = Query(id);
        if (row is not { } r || r.Mime == DocumentsContract.Document.MimeTypeDir)
            return null;
        return new SaveEntry(id, r.Name, relativePath ?? r.Name, r.Size, DateTimeOffset.FromUnixTimeMilliseconds(r.Modified));
    }

    /// <summary>One document's row, or null if it doesn't exist. A lost grant still throws (SecurityException).</summary>
    (string Name, string Mime, long Size, long Modified)? Query(string id)
    {
        try
        {
            using var c = resolver.Query(DocumentsContract.BuildDocumentUriUsingTree(tree, id)!, Columns, null, null, null);
            if (c is null || !c.MoveToFirst())
                return null;
            return (c.GetString(1) ?? "", c.GetString(2) ?? "", c.IsNull(3) ? 0 : c.GetLong(3), c.IsNull(4) ? 0 : c.GetLong(4));
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // The external storage provider reports a missing file this way.
            return null;
        }
    }

    Uri DocumentUri(SaveEntry entry) => DocumentsContract.BuildDocumentUriUsingTree(tree, entry.Id)!;

    public byte[] ReadAll(SaveEntry entry)
    {
        using var stream = Open(entry, "r", FileAccess.Read);
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    // "rw" opens the existing file without truncating it ("w" and "rwt" may truncate first).
    public Stream OpenInPlace(SaveEntry entry) => Open(entry, "rw", FileAccess.ReadWrite);

    FileStream Open(SaveEntry entry, string mode, FileAccess access)
    {
        using var pfd = resolver.OpenFileDescriptor(DocumentUri(entry), mode)
            ?? throw new IOException($"provider returned no file for {entry.RelativePath}");
        var handle = new SafeFileHandle(pfd.DetachFd(), ownsHandle: true);
        return new FileStream(handle, access, bufferSize: 0);
    }
}
