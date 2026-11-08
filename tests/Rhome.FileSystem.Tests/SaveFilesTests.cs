using PKHeX.Core;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.FileSystem.Tests;

/// <summary>In-place save writes, backups, and a changeset applied to real files (issue #21).</summary>
public sealed class SaveFilesTests : IDisposable
{
    static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(-5));

    readonly string folder = Directory.CreateTempSubdirectory("rhome-test-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    string Copy(string fixture, string name)
    {
        var path = Path.Combine(folder, "saves", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture + ".sav"), path);
        return path;
    }

    [Fact]
    public void A_save_is_written_into_the_same_file()
    {
        var path = Copy("emerald", "emerald.srm");
        var save = new SaveOnDisk(path);
        var changed = save.Read();
        changed[0x100] ^= 0xFF;

        // A handle opened before the write (like an emulator holding the file) sees the new bytes. If the file
        // had been replaced by a new one, this handle would still be reading the old file.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        save.WriteInPlace(changed);
        var seen = new byte[changed.Length];
        held.ReadExactly(seen);

        Assert.Equal(changed, seen);
        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void A_save_is_never_resized_or_recreated()
    {
        var path = Copy("emerald", "emerald.srm");
        var save = new SaveOnDisk(path);
        var original = save.Read();

        Assert.Throws<IOException>(() => save.WriteInPlace(original[..^16]));
        Assert.Equal(original, File.ReadAllBytes(path));

        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => save.WriteInPlace(original));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Backups_keep_the_newest_few_in_order()
    {
        var backups = new BackupFolder(Path.Combine(folder, "backups"), keep: 3);
        var id = Guid.NewGuid();

        for (byte i = 1; i <= 5; i++)
            backups.KeepSave(id, [i], Monday.AddMinutes(i));
        backups.KeepSave(id, [6], Monday.AddMinutes(5)); // same time as the last one
        backups.KeepBank("{}"u8, Monday);

        var kept = backups.SaveBackups(id);
        Assert.Equal([[4], [5], [6]], kept.Select(File.ReadAllBytes));
        Assert.Single(backups.BankBackups());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupFolder(folder, keep: 0));
    }

    [Fact]
    public void A_changeset_moves_a_Pokemon_between_two_save_files_on_disk()
    {
        var platinum = new SaveOnDisk(Copy("platinum-us", "Platinum.sav"));
        var emerald = new SaveOnDisk(Copy("emerald", "Emerald.srm"));
        var store = new JsonBankStore(new FileOnDisk(Path.Combine(folder, "bank", "bank.json")));
        var backups = new BackupFolder(Path.Combine(folder, "backups"));
        var bank = new Bank();
        var ids = new Dictionary<Guid, SaveOnDisk>();
        foreach (var (save, name) in new[] { (platinum, "Platinum"), (emerald, "Emerald") })
        {
            var detected = SaveDetector.Detect(save.Read())!;
            var record = new BankSave(Guid.NewGuid(), detected.Game,
                new TrainerInfo(detected.Trainer, detected.Save.TID16, detected.Save.SID16), name, null);
            bank.Saves.Add(record);
            ids[record.Id] = save;
        }
        store.Save(bank);
        var (platinumId, emeraldId) = (bank.Saves[0].Id, bank.Saves[1].Id);
        var runner = new ChangesetRunner(store, id => ids.GetValueOrDefault(id), backups);
        var originalEmerald = emerald.Read();

        var import = new Changeset();
        import.Opened(platinumId, platinum.Read());
        import.Add(new ImportChange(platinumId, new SaveSlot(1, 3))); // Gastly
        Assert.True(runner.Apply(import, Monday).Written);
        var gastly = Assert.Single(store.Load().Pokemon);

        var withdraw = new Changeset();
        withdraw.Opened(emeraldId, emerald.Read());
        withdraw.Add(new WithdrawChange(gastly.Id, emeraldId));
        var result = runner.Apply(withdraw, Monday.AddMinutes(5));

        Assert.True(result.Written, result.Refusal);
        var inEmerald = SaveDetector.Detect(emerald.Read())!.Save;
        Assert.True(inEmerald.ChecksumsValid);
        Assert.Equal((ushort)Species.Gastly, inEmerald.GetBoxSlotAtIndex(1).Species);
        Assert.Equal(0, SaveDetector.Detect(platinum.Read())!.Save.GetBoxSlotAtIndex(2).Species);
        Assert.False(store.Load().Pokemon[0].IsInBank);
        Assert.Equal(originalEmerald, File.ReadAllBytes(Assert.Single(backups.SaveBackups(emeraldId))));
        Assert.Equal(2, backups.BankBackups().Count);
        Assert.Equal(["Emerald.srm", "Platinum.sav"], Directory.GetFiles(Path.Combine(folder, "saves")).Select(Path.GetFileName).Order());
    }
}
