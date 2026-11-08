using Rhome.Core;

namespace Rhome.FileSystem;

/// <summary>
/// A file on a normal file system, e.g. the bank file. <see cref="Replace"/> writes a temp file in the
/// same folder, flushes it to disk, then renames it over the old one. A rename within one folder is a
/// single step on every file system rhome targets, so a crash leaves the old file or the new one.
/// </summary>
public sealed class FileOnDisk(string path) : IStoredFile
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public bool Exists => File.Exists(Path);

    public Stream OpenRead() => File.OpenRead(Path);

    public void Replace(ReadOnlySpan<byte> contents)
    {
        var folder = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(folder);

        var temp = System.IO.Path.Combine(folder, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
