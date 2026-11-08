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
