using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>
/// The changes the user has staged and not saved yet (PLAN.md §2 "Pending changes → Save"). Each one is previewed
/// as it's staged, so a change that can't be done is refused straight away and the screens can show the bank as the
/// changes leave it. Nothing is written until <see cref="Save"/>.
/// </summary>
/// <remarks>
/// Bank changes only for now (issue #44). Moves between a save and the bank join with the split view (#45), which
/// also gives the runner a way to find each save.
/// </remarks>
public sealed class PendingChanges
{
    readonly IBankStore store;
    readonly ChangesetRunner runner;
    Changeset changeset = new();
    IReadOnlyList<ChangeOutcome> outcomes = [];

    public PendingChanges(AppStorage storage)
    {
        store = storage.Bank;
        runner = new ChangesetRunner(store, _ => null, storage.Backups);
        try
        {
            Bank = store.Load();
        }
        catch (Exception ex) when (ex is BankFormatException or IOException or UnauthorizedAccessException)
        {
            // Never write over a bank rhome can't read: it may hold the only copy of someone's Pokémon.
            Bank = new Bank();
            LoadProblem = $"bank.json couldn't be read, so the bank can't be changed until it's fixed or restored from a backup: {ex.Message}";
        }
    }

    /// <summary>Why the stored bank couldn't be read. Nothing can be staged then.</summary>
    public string? LoadProblem { get; }

    /// <summary>The bank as the pending changes leave it.</summary>
    public Bank Bank { get; private set; }

    /// <summary>What each pending change does, in order.</summary>
    public IReadOnlyList<ChangeOutcome> Outcomes => outcomes;

    public bool IsEmpty => changeset.IsEmpty;

    /// <summary>Stages a change. Returns why it can't be done, or null when it was staged.</summary>
    public string? Stage(Change change, DateTimeOffset now)
    {
        if (LoadProblem is not null)
            return LoadProblem;
        changeset.Add(change);
        var preview = runner.Preview(changeset, now);
        if (!preview.Succeeded)
        {
            changeset.Remove(change);
            return preview.Refusal;
        }
        Show(preview);
        return null;
    }

    /// <summary>Takes back the last change staged.</summary>
    public void UndoLast(DateTimeOffset now)
    {
        if (changeset.Changes.Count == 0)
            return;
        changeset.Remove(changeset.Changes[^1]);
        Refresh(now);
    }

    /// <summary>Drops every pending change.</summary>
    public void Discard()
    {
        changeset = new Changeset();
        outcomes = [];
        Bank = store.Load();
    }

    /// <summary>
    /// Writes the pending changes, all or nothing, after backing up the bank. On success the list is empty again; on a
    /// refusal the changes stay, so nothing the user did is lost.
    /// </summary>
    /// <exception cref="ChangesetWriteException">Writing failed; the bank is as it was.</exception>
    public ChangesetResult Save(DateTimeOffset now)
    {
        var result = runner.Apply(changeset, now);
        if (result.Written)
        {
            changeset = new Changeset();
            outcomes = [];
            Bank = result.Bank!;
        }
        return result;
    }

    void Refresh(DateTimeOffset now)
    {
        if (changeset.IsEmpty)
        {
            Discard();
            return;
        }
        var preview = runner.Preview(changeset, now);
        if (preview.Succeeded)
            Show(preview);
    }

    void Show(ChangesetResult preview)
    {
        Bank = preview.Bank!;
        outcomes = preview.Outcomes;
    }
}
