using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App.Tests;

/// <summary>The test writes on the save access screen (issue #40), on real files.</summary>
public sealed class WriteTestTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    (ISaveLocation Save, string Path, AppStorage Storage) Setup(string fixture)
    {
        var saves = Directory.CreateDirectory(Path.Combine(root, "saves")).FullName;
        var path = Path.Combine(saves, fixture + ".sav");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture + ".sav"), path);
        return (new FolderOnDisk(saves).Open(path), path, new AppStorage(Path.Combine(root, "app")));
    }

    [Theory]
    [InlineData("emerald")]
    [InlineData("platinum-us")]
    [InlineData("white")]
    public void Money_plus_one_is_written_in_place_after_a_backup(string fixture)
    {
        var (save, path, storage) = Setup(fixture);
        var original = File.ReadAllBytes(path);
        var money = SaveDetector.Detect(original)!.Save.Money;
        var id = WriteTest.BackupId(path);

        WriteTest.Run(save, storage.Backups, id, moneyPlusOne: true, Now);

        var after = File.ReadAllBytes(path);
        Assert.Equal(original.Length, after.Length);
        Assert.Equal(money + 1, SaveDetector.Detect(after)!.Save.Money);
        Assert.True(SaveDetector.Detect(after)!.Save.ChecksumsValid);
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(storage.Backups.SaveBackups(id))));
    }

    [Fact]
    public void Writing_back_unchanged_keeps_the_same_bytes()
    {
        var (save, path, storage) = Setup("emerald");
        var original = File.ReadAllBytes(path);

        WriteTest.Run(save, storage.Backups, Guid.NewGuid(), moneyPlusOne: false, Now);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_file_that_isnt_a_save_is_left_alone()
    {
        var (_, _, storage) = Setup("emerald");
        var other = Path.Combine(root, "saves", "notes.sav");
        File.WriteAllBytes(other, new byte[0x20000]);
        var id = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WriteTest.Run(new SaveOnDisk(other), storage.Backups, id, moneyPlusOne: true, Now));

        Assert.Contains("nothing was written", ex.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[0x20000], File.ReadAllBytes(other));
        Assert.Empty(storage.Backups.SaveBackups(id));
    }

    [Fact]
    public void The_backup_id_is_stable_per_file()
    {
        Assert.Equal(WriteTest.BackupId("primary:a/b.sav"), WriteTest.BackupId("primary:a/b.sav"));
        Assert.NotEqual(WriteTest.BackupId("primary:a/b.sav"), WriteTest.BackupId("primary:a/c.sav"));
    }
}
