using System.Security.Cryptography;
using System.Text;
using Rhome.Core;

namespace Rhome.App;

/// <summary>
/// Test writes for checking save access on a device (issue #40), until the real transfer screens exist (#45,
/// #46). Each one backs the save up, writes it in place and reads it back, like a changeset does.
/// </summary>
public static class WriteTest
{
    /// <summary>
    /// Writes the save back with its money +1 (or unchanged), which is easy to check in game on the trainer card.
    /// Returns a line for the log.
    /// </summary>
    /// <exception cref="InvalidOperationException">The save isn't one rhome can write, or didn't read back right.
    /// Nothing was written unless the message says so.</exception>
    public static string Run(ISaveLocation save, IBackups backups, Guid saveId, bool moneyPlusOne, DateTimeOffset now)
    {
        var original = save.Read();
        var detected = SaveDetector.Detect(original)
            ?? throw new InvalidOperationException("not a Pokémon save rhome recognizes; nothing was written");
        if (!detected.IsSupported || !detected.Save.ChecksumsValid)
            throw new InvalidOperationException($"{detected.Game} save that rhome can't write (unsupported or failing its checksums); nothing was written");

        var money = detected.Save.Money;
        byte[] bytes;
        if (moneyPlusOne)
        {
            detected.Save.Money = money + 1;
            bytes = detected.Save.Write().ToArray();
        }
        else
        {
            bytes = original;
        }

        var check = SaveDetector.Detect(bytes);
        if (bytes.Length != original.Length || check is null || check.Game != detected.Game || !check.Save.ChecksumsValid
            || check.Save.Money != (moneyPlusOne ? money + 1 : money))
            throw new InvalidOperationException("the changed save didn't read back right; nothing was written");

        backups.KeepSave(saveId, original, now);
        save.WriteInPlace(bytes);

        var after = save.Read();
        if (!after.AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("WRITTEN, but the file reads back different bytes. Restore it from its backup.");

        var afterMoney = SaveDetector.Detect(after)!.Save.Money;
        return $"{detected.Game} ({detected.Trainer}): money {money} → {afterMoney}, " +
               $"SHA-256 {Short(original)} → {Short(after)}, {after.Length} bytes";
    }

    /// <summary>A stable ID for a file's backups, from its platform ID, until the bank keeps track of saves.</summary>
    public static Guid BackupId(string fileId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(fileId)).AsSpan(0, 16));

    static string Short(byte[] bytes) => SaveHash.Of(bytes)[..12];
}
