using System.Security.Cryptography;
using PKHeX.Core;

namespace SpikeB.Core;

public enum WriteTest
{
    /// <summary>Write the exact bytes back. Proves we're allowed to write, with zero risk to the save.</summary>
    SameBytes,
    /// <summary>Load with PKHeX.Core, add 1 to the trainer's money, write. Proves the game accepts our write.</summary>
    MoneyPlusOne,
}

public sealed record WriteResult(bool Ok, string Message, string? BackupPath);

/// <summary>Backs a save up, overwrites it in place, and reads it back to check.</summary>
public static class InPlaceWriter
{
    public static WriteResult Run(ISaveFolder folder, SaveEntry entry, WriteTest test, string backupDir)
    {
        byte[] original = folder.ReadAll(entry);

        // Back up first, before anything can touch the bytes.
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{entry.Name}");
        File.WriteAllBytes(backup, original);

        byte[] updated;
        string change;
        switch (test)
        {
            case WriteTest.SameBytes:
                updated = original;
                change = "same bytes";
                break;
            case WriteTest.MoneyPlusOne:
                // PKHeX.Core works on the array it's given, so hand it a copy.
                if (!SaveUtil.TryGetSaveFile(original.ToArray(), out var sav, entry.Name))
                    return new WriteResult(false, "PKHeX.Core doesn't recognize this save; nothing written.", null);
                uint before = sav.Money;
                sav.Money = before < sav.MaxMoney ? before + 1 : before - 1;
                updated = sav.Write().ToArray();
                change = $"money {before} → {sav.Money}";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(test));
        }

        if (updated.Length != original.Length)
            return new WriteResult(false, $"New data is {updated.Length} bytes but the save is {original.Length}; nothing written.", null);

        WriteInPlace(folder, entry, updated);

        var after = folder.ReadAll(entry);
        bool ok = after.AsSpan().SequenceEqual(updated);
        return new WriteResult(ok,
            $"{(ok ? "OK" : "READ-BACK MISMATCH")}: {change}; sha1 {Sha1(original)} → {Sha1(after)}",
            backup);
    }

    public static WriteResult Restore(ISaveFolder folder, SaveEntry entry, string backupPath)
    {
        var data = File.ReadAllBytes(backupPath);
        WriteInPlace(folder, entry, data);
        bool ok = folder.ReadAll(entry).AsSpan().SequenceEqual(data);
        return new WriteResult(ok, $"{(ok ? "OK" : "READ-BACK MISMATCH")}: restored {Path.GetFileName(backupPath)}", backupPath);
    }

    /// <summary>
    /// Overwrites from offset 0 and trims to length, on the same open file. Never deletes,
    /// renames or truncates-then-writes, so the file keeps its owner and permissions.
    /// </summary>
    public static void WriteInPlace(ISaveFolder folder, SaveEntry entry, byte[] data)
    {
        using var stream = folder.OpenInPlace(entry);
        stream.Position = 0;
        stream.Write(data);
        if (stream.Length != data.Length)
            stream.SetLength(data.Length);
        if (stream is FileStream fs)
            fs.Flush(flushToDisk: true);
        else
            stream.Flush();
    }

    static string Sha1(byte[] data) => Convert.ToHexStringLower(SHA1.HashData(data))[..12];
}
