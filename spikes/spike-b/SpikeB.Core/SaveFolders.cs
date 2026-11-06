namespace SpikeB.Core;

/// <summary>A file found in a save folder. <see cref="Id"/> is whatever the platform uses to find it again.</summary>
public sealed record SaveEntry(string Id, string Name, string RelativePath, long Size, DateTimeOffset LastModified);

/// <summary>
/// A folder of saves that the app has been allowed to use. On Android this is a folder the user
/// granted through the system picker; on desktop it's a plain directory.
/// </summary>
public interface ISaveFolder
{
    /// <summary>Every file under the folder, down to <paramref name="maxDepth"/> levels of subfolders.</summary>
    IReadOnlyList<SaveEntry> List(int maxDepth);

    /// <summary>Whether the folder itself can be reached right now (an SD card can be taken out).</summary>
    bool IsAvailable();

    /// <summary>
    /// Looks up one file by the <see cref="SaveEntry.Id"/> an earlier listing gave it, without listing the
    /// folder. Null if it's gone. <paramref name="relativePath"/> is kept as is (null: use the file name).
    /// </summary>
    SaveEntry? Find(string id, string? relativePath);

    byte[] ReadAll(SaveEntry entry);

    /// <summary>
    /// Opens the existing file for reading and writing <b>without</b> truncating or recreating it,
    /// so the emulator keeps owning the same file (see the in-place lesson in prototypes/README.md).
    /// </summary>
    Stream OpenInPlace(SaveEntry entry);
}

/// <summary>A plain directory (desktop, Linux, tests).</summary>
public sealed class LocalSaveFolder(string root) : ISaveFolder
{
    public IReadOnlyList<SaveEntry> List(int maxDepth)
    {
        var result = new List<SaveEntry>();
        Walk(new DirectoryInfo(root), "", maxDepth, result);
        return result;
    }

    static void Walk(DirectoryInfo dir, string prefix, int depthLeft, List<SaveEntry> result)
    {
        foreach (var file in dir.EnumerateFiles())
            result.Add(new SaveEntry(file.FullName, file.Name, prefix + file.Name, file.Length, file.LastWriteTimeUtc));
        if (depthLeft <= 0)
            return;
        foreach (var sub in dir.EnumerateDirectories())
            Walk(sub, prefix + sub.Name + "/", depthLeft - 1, result);
    }

    public bool IsAvailable() => Directory.Exists(root);

    public SaveEntry? Find(string id, string? relativePath)
    {
        var file = new FileInfo(id);
        return file.Exists
            ? new SaveEntry(file.FullName, file.Name, relativePath ?? file.Name, file.Length, file.LastWriteTimeUtc)
            : null;
    }

    public byte[] ReadAll(SaveEntry entry) => File.ReadAllBytes(entry.Id);

    public Stream OpenInPlace(SaveEntry entry) =>
        new FileStream(entry.Id, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
}
