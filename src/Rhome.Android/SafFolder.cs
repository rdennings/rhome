using Android.Content;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Rhome.Core;
using Uri = Android.Net.Uri;

namespace Rhome.Android;

/// <summary>
/// A folder the user granted through the system picker. Files are found by document ID, which stays the same across
/// restarts, and opened through file descriptors so .NET streams work on them.
/// </summary>
sealed class SafFolder(ContentResolver resolver, Uri tree) : ISaveFolder
{
    static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    ];

    public string DisplayName => Describe(tree);

    /// <summary>
    /// The folder as a person would say it. Document IDs are "&lt;volume&gt;:&lt;path&gt;": "primary" for internal
    /// storage, the card's UUID (e.g. 12A8-6020) for an SD card.
    /// </summary>
    public static string Describe(Uri tree)
    {
        var id = DocumentsContract.GetTreeDocumentId(tree) ?? tree.ToString()!;
        var colon = id.IndexOf(':', StringComparison.Ordinal);
        if (tree.Authority != "com.android.externalstorage.documents" || colon < 0)
            return $"{tree.Authority} · {id}";
        var volume = id[..colon];
        return (volume == "primary" ? "Internal storage" : $"SD card {volume}") + "/" + id[(colon + 1)..];
    }

    public bool IsAvailable()
    {
        try
        {
            using var c = resolver.Query(DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!,
                Columns, null, null, null);
            return c is not null && c.MoveToFirst();
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException or Java.IO.FileNotFoundException)
        {
            // A revoked grant throws SecurityException; a missing folder or removed card, one of the others.
            return false;
        }
    }

    public IReadOnlyList<FolderEntry> List(int maxDepth)
    {
        var result = new List<FolderEntry>();
        Walk(DocumentsContract.GetTreeDocumentId(tree)!, "", maxDepth, result);
        return result;
    }

    void Walk(string parentId, string prefix, int depthLeft, List<FolderEntry> result)
    {
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parentId)!;
        var files = new List<FolderEntry>();
        var folders = new List<(string Id, string Name)>();
        using (var c = resolver.Query(children, Columns, null, null, null))
        {
            while (c is not null && c.MoveToNext())
            {
                string id = c.GetString(0)!, name = c.GetString(1) ?? "", mime = c.GetString(2) ?? "";
                if (mime == DocumentsContract.Document.MimeTypeDir)
                {
                    if (depthLeft > 0)
                        folders.Add((id, name));
                    continue;
                }
                long size = c.IsNull(3) ? 0 : c.GetLong(3);
                long modified = c.IsNull(4) ? 0 : c.GetLong(4);
                files.Add(new FolderEntry(id, prefix + name, size, DateTimeOffset.FromUnixTimeMilliseconds(modified)));
            }
        }
        result.AddRange(files.OrderBy(f => f.RelativePath, StringComparer.Ordinal));
        foreach (var (id, name) in folders.OrderBy(f => f.Name, StringComparer.Ordinal))
            Walk(id, prefix + name + "/", depthLeft - 1, result);
    }

    /// <summary>One query for the document's own row: no listing, so it stays fast in a folder of 2,800 ROMs.</summary>
    public FileStamp? Stat(string id)
    {
        try
        {
            using var c = resolver.Query(DocumentsContract.BuildDocumentUriUsingTree(tree, id)!, Columns, null, null, null);
            if (c is null || !c.MoveToFirst())
                return null;
            return new FileStamp(c.IsNull(3) ? 0 : c.GetLong(3), DateTimeOffset.FromUnixTimeMilliseconds(c.IsNull(4) ? 0 : c.GetLong(4)));
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalArgumentException or Java.IO.FileNotFoundException)
        {
            // A deleted document: the provider can't resolve its ID any more.
            return null;
        }
        catch (Java.Lang.SecurityException ex)
        {
            throw new UnauthorizedAccessException($"rhome no longer has access to {id}: {ex.Message}", ex);
        }
    }

    public ISaveLocation Open(string id) => new SafSave(resolver, DocumentsContract.BuildDocumentUriUsingTree(tree, id)!);
}

/// <summary>
/// One save file the user picked by hand (PLAN.md §2 "Fallback"), as a folder holding just that file, so it's scanned
/// and written like any other.
/// </summary>
sealed class SafFile(ContentResolver resolver, Uri document) : ISaveFolder
{
    static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    ];

    public string DisplayName => Row()?.Name ?? document.LastPathSegment ?? document.ToString()!;

    public bool IsAvailable() => Row() is not null;

    public IReadOnlyList<FolderEntry> List(int maxDepth) =>
        Row() is { } r
            ? [new FolderEntry(document.ToString()!, r.Name, r.Size, DateTimeOffset.FromUnixTimeMilliseconds(r.Modified))]
            : throw new IOException($"{DisplayName} can't be reached.");

    public FileStamp? Stat(string id) =>
        Row() is { } r ? new FileStamp(r.Size, DateTimeOffset.FromUnixTimeMilliseconds(r.Modified)) : null;

    public ISaveLocation Open(string id) => new SafSave(resolver, document);

    (string Name, long Size, long Modified)? Row()
    {
        try
        {
            using var c = resolver.Query(document, Columns, null, null, null);
            if (c is null || !c.MoveToFirst())
                return null;
            return (c.GetString(0) ?? "", c.IsNull(1) ? 0 : c.GetLong(1), c.IsNull(2) ? 0 : c.GetLong(2));
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException or Java.IO.FileNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// One save in a granted folder. Writes open the existing file with mode "rw", which neither truncates nor recreates
/// it ("w" and "rwt" may truncate first), and overwrite it from the start, so the emulator keeps owning the same file
/// (PLAN.md §1 lesson 2). Spike B checked on the Thor that the inode, owner and mode stay the same.
/// </summary>
sealed class SafSave(ContentResolver resolver, Uri document) : ISaveLocation
{
    public byte[] Read()
    {
        using var stream = Open("r", FileAccess.Read);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void WriteInPlace(ReadOnlySpan<byte> contents)
    {
        using var stream = Open("rw", FileAccess.ReadWrite);
        if (stream.Length != contents.Length)
            throw new IOException($"{document.LastPathSegment} is {stream.Length} bytes, not {contents.Length}; refusing to change its size.");
        stream.Position = 0;
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    FileStream Open(string mode, FileAccess access)
    {
        try
        {
            using var pfd = resolver.OpenFileDescriptor(document, mode)
                ?? throw new IOException($"Android returned no file for {document.LastPathSegment}.");
            return new FileStream(new SafeFileHandle(pfd.DetachFd(), ownsHandle: true), access, bufferSize: 0);
        }
        catch (Java.IO.FileNotFoundException ex)
        {
            // Never create the file: a save that's gone stays gone.
            throw new FileNotFoundException($"{document.LastPathSegment} is gone: {ex.Message}", ex);
        }
        catch (Java.Lang.SecurityException ex)
        {
            throw new UnauthorizedAccessException($"rhome no longer has access to {document.LastPathSegment}: {ex.Message}", ex);
        }
    }
}
