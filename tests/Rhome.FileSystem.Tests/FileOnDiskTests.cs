using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.FileSystem.Tests;

public sealed class FileOnDiskTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("rhome-test-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void Replace_creates_the_file_and_its_folder()
    {
        var file = new FileOnDisk(Path.Combine(folder, "nested", "bank.json"));

        Assert.False(file.Exists);
        file.Replace("hello"u8);

        Assert.True(file.Exists);
        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(file.Path));
    }

    [Fact]
    public void Replace_swaps_in_the_new_contents_and_leaves_no_temp_file()
    {
        var file = new FileOnDisk(Path.Combine(folder, "bank.json"));
        file.Replace("a much longer old version"u8);

        file.Replace("new"u8);

        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(file.Path));
        Assert.Equal([file.Path], Directory.GetFiles(folder));
    }

    [Fact]
    public void A_failed_replace_cleans_up_its_temp_file()
    {
        var target = Path.Combine(folder, "bank.json");
        Directory.CreateDirectory(target); // a folder where the file should be: the final rename fails

        Assert.ThrowsAny<IOException>(() => new FileOnDisk(target).Replace("new"u8));

        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void A_bank_survives_a_trip_through_the_disk()
    {
        var bank = new Bank();
        bank.Boxes.Boxes[0].Name = "Starters";
        var path = Path.Combine(folder, "bank.json");

        new JsonBankStore(new FileOnDisk(path)).Save(bank);
        var loaded = new JsonBankStore(new FileOnDisk(path)).Load();

        Assert.Equal("Starters", loaded.Boxes.Boxes[0].Name);
        Assert.Equal(BankJson.ToBytes(bank), BankJson.ToBytes(loaded));
    }
}
