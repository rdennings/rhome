using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App.Tests;

/// <summary>Pending bank changes (issue #44): staged and previewed one at a time, written only on Save.</summary>
public sealed class PendingChangesTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    readonly string root = Directory.CreateTempSubdirectory("rhome-test-").FullName;
    readonly AppStorage storage;

    public PendingChangesTests() => storage = new AppStorage(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    static IEnumerable<string> Names(Bank bank) => bank.Boxes.Boxes.Select(b => b.Name);

    [Fact]
    public void Staged_changes_show_in_the_bank_but_arent_written_until_save()
    {
        var pending = new PendingChanges(storage);

        Assert.True(pending.Stage(new RenameBoxChange(1, "Starters"), Now).Succeeded);
        Assert.True(pending.Stage(new MoveBoxChange(1, 3), Now).Succeeded);

        Assert.Equal(["Box 2", "Box 3", "Starters"], Names(pending.Bank).Take(3));
        Assert.Equal(["Rename box Box 1 to Starters", "Move box Starters to place 3"], pending.Outcomes.Select(o => o.Description));
        Assert.False(storage.BankFile.Exists);

        var result = pending.Save(Now);

        Assert.True(result.Written, result.Refusal);
        Assert.True(pending.IsEmpty);
        Assert.Equal(["Box 2", "Box 3", "Starters"], Names(storage.Bank.Load()).Take(3));
    }

    [Fact]
    public void A_change_that_cant_be_done_is_refused_and_not_kept()
    {
        var pending = new PendingChanges(storage);

        Assert.Equal("A box needs a name.", pending.Stage(new RenameBoxChange(1, " "), Now).Refusal);

        Assert.True(pending.IsEmpty);
        Assert.Equal("Box 1", pending.Bank.Boxes.Boxes[0].Name);
    }

    [Fact]
    public void Undo_takes_back_the_last_change_and_discard_all_of_them()
    {
        var pending = new PendingChanges(storage);
        pending.Stage(new AddBoxChange(pending.Bank.Boxes.NextId, "Eleven"), Now);
        pending.Stage(new AddBoxChange(pending.Bank.Boxes.NextId, "Twelve"), Now);

        pending.UndoLast(Now);
        Assert.Equal("Eleven", Names(pending.Bank).Last());
        Assert.Single(pending.Outcomes);

        pending.Discard();
        Assert.True(pending.IsEmpty);
        Assert.Equal(10, pending.Bank.Boxes.Boxes.Count);
    }

    [Fact]
    public void Saving_backs_up_the_bank_first()
    {
        var pending = new PendingChanges(storage);
        pending.Stage(new RenameBoxChange(1, "First"), Now);
        pending.Save(Now);

        pending.Stage(new RenameBoxChange(1, "Second"), Now.AddMinutes(1));
        pending.Save(Now.AddMinutes(1));

        var backups = storage.Backups.BankBackups();
        Assert.Equal(2, backups.Count);
        using var stream = File.OpenRead(backups[^1]);
        Assert.Equal("First", BankJson.Read(stream).Boxes.Boxes[0].Name);
    }

    [Fact]
    public void A_bank_that_cant_be_read_is_never_written_over()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(storage.BankFile.Path, "{ not a bank");

        var pending = new PendingChanges(storage);

        Assert.NotNull(pending.LoadProblem);
        Assert.Equal(pending.LoadProblem, pending.Stage(new RenameBoxChange(1, "x"), Now).Refusal);
        Assert.Equal("{ not a bank", File.ReadAllText(storage.BankFile.Path));
    }
}
