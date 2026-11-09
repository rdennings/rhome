using Rhome.Core;

namespace Rhome.FileSystem;

/// <summary>A folder of saves on a normal file system. Files are opened as <see cref="SaveOnDisk"/>.</summary>
public sealed class FolderOnDisk(string path) : ISaveFolder
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public string DisplayName => Path;

    public bool IsAvailable() => Directory.Exists(Path);

    public IReadOnlyList<FolderEntry> List(int maxDepth)
    {
        var result = new List<FolderEntry>();
        Walk(new DirectoryInfo(Path), "", maxDepth, result);
        return result;
    }

    static void Walk(DirectoryInfo folder, string prefix, int depthLeft, List<FolderEntry> result)
    {
        foreach (var file in folder.EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
            result.Add(new FolderEntry(file.FullName, prefix + file.Name, file.Length, file.LastWriteTimeUtc));
        if (depthLeft <= 0)
            return;
        foreach (var sub in folder.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
            Walk(sub, prefix + sub.Name + "/", depthLeft - 1, result);
    }

    /// <summary>The save at <paramref name="id"/>, which must be a file inside this folder.</summary>
    public ISaveLocation Open(string id)
    {
        var full = System.IO.Path.GetFullPath(id);
        var inside = System.IO.Path.GetRelativePath(Path, full);
        if (inside == "." || inside.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(inside))
            throw new ArgumentException($"{id} isn't in {Path}.", nameof(id));
        return new SaveOnDisk(full);
    }
}
