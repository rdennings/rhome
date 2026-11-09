using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.FileSystem.Tests;

/// <summary>A save folder on disk and the app's own storage (issue #40).</summary>
public sealed class FolderOnDiskTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    string Write(string relativePath, int size)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public void Lists_files_down_to_the_depth_asked_for()
    {
        Write("emerald.srm", 3);
        Write("nds/white.sav", 5);
        Write("nds/old/platinum.sav", 7);
        var folder = new FolderOnDisk(root);

        Assert.Equal(["emerald.srm"], folder.List(0).Select(e => e.RelativePath));
        Assert.Equal(["emerald.srm", "nds/white.sav"], folder.List(1).Select(e => e.RelativePath));

        var deepest = folder.List(2).Single(e => e.Name == "platinum.sav");
        Assert.Equal("nds/old/platinum.sav", deepest.RelativePath);
        Assert.Equal(7, deepest.Size);
    }

    [Fact]
    public void A_listed_file_opens_as_a_save_written_in_place()
    {
        Write("white.sav", 4);
        var folder = new FolderOnDisk(root);
        var entry = Assert.Single(folder.List(0));

        var save = folder.Open(entry.Id);
        save.WriteInPlace([1, 2, 3, 4]);

        Assert.Equal([1, 2, 3, 4], save.Read());
        Assert.Equal([1, 2, 3, 4], folder.Open(entry.Id).Read());
    }

    [Fact]
    public void Refuses_a_file_outside_the_folder()
    {
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "elsewhere.sav");
        var folder = new FolderOnDisk(Path.Combine(root, "saves"));

        Assert.Throws<ArgumentException>(() => folder.Open(outside));
        Assert.Throws<ArgumentException>(() => folder.Open(Path.Combine(root, "saves", "..", "x.sav")));
        Assert.Throws<ArgumentException>(() => folder.Open(Path.Combine(root, "saves")));
    }

    [Fact]
    public void A_missing_folder_is_not_available()
    {
        Assert.True(new FolderOnDisk(root).IsAvailable());
        Assert.False(new FolderOnDisk(Path.Combine(root, "sd-card-taken-out")).IsAvailable());
    }

    [Fact]
    public void App_storage_keeps_the_bank_and_backups_under_its_root()
    {
        var storage = new AppStorage(Path.Combine(root, "files"), keepBackups: 2);
        var when = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        storage.Bank.Save(new Bank());
        storage.Backups.KeepSave(Guid.Empty, [1], when);
        storage.Backups.KeepBank([2], when);

        Assert.True(File.Exists(Path.Combine(root, "files", "bank.json")));
        Assert.Single(storage.Backups.SaveBackups(Guid.Empty));
        Assert.Single(storage.Backups.BankBackups());
        Assert.StartsWith(Path.Combine(root, "files", "backups"), storage.Backups.BankBackups()[0], StringComparison.Ordinal);
        Assert.Equal(2, storage.Backups.Keep);
    }
}
