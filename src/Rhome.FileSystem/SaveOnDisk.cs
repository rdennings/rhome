using Rhome.Core;

namespace Rhome.FileSystem;

/// <summary>
/// A game save on a normal file system. Writes go into the existing file, at the same length: the file is never
/// deleted, renamed or recreated, so an emulator that has it open, or owns it on Android, keeps it
/// (PLAN.md §1 lesson 2).
/// </summary>
public sealed class SaveOnDisk(string path) : ISaveLocation
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public byte[] Read() => File.ReadAllBytes(Path);

    public void WriteInPlace(ReadOnlySpan<byte> contents)
    {
        // FileMode.Open: fail if the file is gone rather than create a new one. Others may keep reading and
        // writing (an emulator often holds its save open); rhome checked the bytes just before.
        using var stream = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (stream.Length != contents.Length)
            throw new IOException($"{Path} is {stream.Length} bytes, not {contents.Length}; refusing to change its size.");
        stream.Position = 0;
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }
}
