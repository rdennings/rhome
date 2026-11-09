using System.Security.Cryptography;

namespace Rhome.Core;

/// <summary>
/// One game save, as the platform layer reaches it (PLAN.md §4). The core never sees where it is.
/// </summary>
public interface ISaveLocation
{
    /// <summary>The save's current bytes.</summary>
    byte[] Read();

    /// <summary>
    /// Overwrites the save's bytes in place: the same file, the same length. Never deletes and recreates it
    /// (PLAN.md §1 lesson 2: on Android the emulator then loses access and the game hangs).
    /// </summary>
    void WriteInPlace(ReadOnlySpan<byte> contents);
}

/// <summary>A file in a <see cref="ISaveFolder"/>.</summary>
/// <param name="Id">What the platform uses to find the file again, also after a restart: a full path on a plain
/// file system, a document ID on Android. Opaque to the core.</param>
/// <param name="RelativePath">The path below the folder, with <c>/</c> between folders, for showing to the user.</param>
public sealed record FolderEntry(string Id, string RelativePath, long Size, DateTimeOffset LastModified)
{
    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}

/// <summary>
/// A folder of saves rhome may use (PLAN.md §4): a plain directory on desktop, a folder the user granted through
/// the system picker on Android. Files in it are opened as <see cref="ISaveLocation"/>s, so they're written in
/// place the same way everywhere.
/// </summary>
public interface ISaveFolder
{
    /// <summary>What to call the folder in the app, e.g. its path.</summary>
    string DisplayName { get; }

    /// <summary>Whether the folder can be reached right now (an SD card can be taken out, a grant revoked).</summary>
    bool IsAvailable();

    /// <summary>Every file in the folder, down to <paramref name="maxDepth"/> levels of subfolders.</summary>
    IReadOnlyList<FolderEntry> List(int maxDepth);

    /// <summary>The save with this <see cref="FolderEntry.Id"/>. Reading or writing it fails if it's gone.</summary>
    ISaveLocation Open(string id);
}

/// <summary>Where copies are kept before anything is written (PLAN.md §2 "Safety").</summary>
public interface IBackups
{
    /// <summary>Keeps a copy of a save as it was before a write. The platform keeps the last few per save.</summary>
    void KeepSave(Guid saveId, ReadOnlySpan<byte> contents, DateTimeOffset when);

    /// <summary>Keeps a copy of the bank as it was before a change is applied. The platform keeps the last few.</summary>
    void KeepBank(ReadOnlySpan<byte> contents, DateTimeOffset when);
}

/// <summary>The fingerprint of a save's bytes rhome records to notice changes made behind its back.</summary>
public static class SaveHash
{
    /// <summary>SHA-256 of the bytes, in lowercase hex (the form <see cref="BankSave.LastSeenHash"/> holds).</summary>
    public static string Of(ReadOnlySpan<byte> contents) => Convert.ToHexStringLower(SHA256.HashData(contents));
}
