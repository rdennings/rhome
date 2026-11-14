using PKHeX.Core;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.App;

/// <summary>The outcome of staging one change.</summary>
/// <param name="Refusal">Why it can't be done, or null when it was staged.</param>
/// <param name="Candidates">When an import might be a Pokémon the bank knows: the ones it might be. The screen asks
/// which, and stages the import again with <see cref="ImportAs"/>.</param>
public sealed record StageResult(string? Refusal, IReadOnlyList<BankPokemon> Candidates)
{
    public static StageResult Staged { get; } = new(null, []);

    public bool Succeeded => Refusal is null;
}

/// <summary>
/// The changes the user has staged and not saved yet (PLAN.md §2 "Pending changes → Save"): moves between saves and
/// the bank, and changes inside the bank. Each one is previewed as it's staged, so a change that can't be done is
/// refused straight away, and the screens show the bank and each save as the changes leave them. Nothing is written
/// until <see cref="Save"/>.
/// </summary>
/// <remarks>
/// A save takes part once it's <see cref="Open"/>ed. The bank knows saves by game and trainer, never by where they
/// are (see <see cref="Bank"/>), so where each one is on this device is kept here, for this session.
/// </remarks>
public sealed class PendingChanges
{
    readonly IBankStore store;
    readonly ChangesetRunner runner;
    readonly Dictionary<Guid, OpenSave> open = [];
    readonly Dictionary<Guid, BankSave> newSaves = [];
    Changeset changeset = new();
    ChangesetResult? preview;

    sealed record OpenSave(ISaveLocation Location, SaveFile Save, string Title);

    public PendingChanges(AppStorage storage)
    {
        store = new WithNewSaves(storage.Bank, this);
        runner = new ChangesetRunner(store, id => open.GetValueOrDefault(id)?.Location, storage.Backups);
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
    public IReadOnlyList<ChangeOutcome> Outcomes => preview?.Outcomes ?? [];

    public bool IsEmpty => changeset.IsEmpty;

    /// <summary>
    /// Makes a save opened from My saves take part in transfers, and returns the bank's ID for it: the save the bank
    /// knows with this game and trainer, or a new one, stored with the first change that touches it.
    /// </summary>
    /// <exception cref="InvalidOperationException">It can't take part, and the message says why.</exception>
    public Guid Open(OpenedSave opened)
    {
        if (LoadProblem is not null)
            throw new InvalidOperationException(LoadProblem);
        var detected = opened.Save;
        var title = SaveText.Title(opened.Game);
        if (!detected.IsSupported)
            throw new InvalidOperationException($"{title} is a Gen {detected.Generation} save; rhome only moves Pokémon in the GBA and DS games so far.");

        var trainer = new TrainerInfo(detected.Trainer, detected.Save.TID16, detected.Save.SID16);
        var known = Bank.Saves.Concat(newSaves.Values).DistinctBy(s => s.Id).Where(s => s.Game == detected.Game && s.Trainer == trainer).ToList();
        if (known.Count > 1)
            throw new InvalidOperationException($"The bank knows {known.Count} {EnglishNames.Game(detected.Game)} saves of {trainer.Name} " +
                                                "with the same trainer ID, so rhome can't tell which one this is.");
        var record = known.FirstOrDefault();
        if (record is null)
        {
            record = new BankSave(Guid.NewGuid(), detected.Game, trainer, title, null);
            newSaves[record.Id] = record;
        }

        var loaded = opened.Game.Loaded;
        var location = loaded.Where.Access.Open(loaded.Entry.Id);
        if (changeset.Saves.Contains(record.Id) && changeset.SeenHash(record.Id) != SaveHash.Of(opened.Bytes))
            throw new InvalidOperationException($"{title} changed on disk while changes to it were pending (was the game saved?). " +
                                                "Discard the pending changes, then open it again.");

        open[record.Id] = new OpenSave(location, detected.Save, title);
        changeset.Opened(record.Id, opened.Bytes);
        return record.Id;
    }

    /// <summary>A save that was <see cref="Open"/>ed, as the pending changes leave it.</summary>
    public SaveFile Save(Guid saveId) =>
        preview?.Saves.GetValueOrDefault(saveId) ?? open[saveId].Save;

    /// <summary>What to call a save the bank knows or that was opened.</summary>
    public string SaveName(Guid saveId) =>
        open.GetValueOrDefault(saveId)?.Title ?? Bank.Saves.Find(s => s.Id == saveId)?.DisplayName ?? "a game";

    /// <summary>Stages a change, unless it can't be done.</summary>
    public StageResult Stage(Change change, DateTimeOffset now)
    {
        if (LoadProblem is not null)
            return new StageResult(LoadProblem, []);
        changeset.Add(change);
        var result = runner.Preview(changeset, now);
        if (!result.Succeeded)
        {
            changeset.Remove(change);
            return new StageResult(result.Refusal, result.Candidates);
        }
        Show(result);
        return StageResult.Staged;
    }

    /// <summary>
    /// Takes back one staged change, the one at <paramref name="index"/> in <see cref="Outcomes"/>, unless a later
    /// change needs it (a move of a Pokémon imported by it, say): then nothing changes and the refusal says why.
    /// </summary>
    public string? Undo(int index, DateTimeOffset now)
    {
        var change = changeset.Changes[index];
        changeset.RemoveAt(index);
        if (changeset.IsEmpty)
        {
            Discard();
            return null;
        }
        var result = runner.Preview(changeset, now);
        if (!result.Succeeded)
        {
            changeset.Insert(index, change);
            // The refusal is about a save as a whole, or about the later change that needs this one.
            var later = result.Failed is null ? -1 : changeset.Changes.ToList().IndexOf(result.Failed);
            if (later < 0 || later >= Outcomes.Count)
                return result.Refusal;
            return $"Can't undo that on its own: change {later + 1} ({Outcomes[later].Description}) needs it. Undo that one first.";
        }
        Show(result);
        return null;
    }

    /// <summary>
    /// Picks the moveset a staged withdraw takes (<see cref="ChangeOutcome.Movesets"/>): the newest one, or the one it
    /// had last time in this game.
    /// </summary>
    public string? ChooseMoves(int index, MovesetChoice moves, DateTimeOffset now)
    {
        if (changeset.Changes[index] is not WithdrawChange withdraw)
            throw new ArgumentException("Only a withdraw has a moveset to choose.", nameof(index));
        if (withdraw.Moves == moves)
            return null;
        changeset.Replace(index, withdraw with { Moves = moves });
        var result = runner.Preview(changeset, now);
        if (!result.Succeeded)
        {
            changeset.Replace(index, withdraw);
            return result.Refusal;
        }
        Show(result);
        return null;
    }

    /// <summary>Drops every pending change.</summary>
    public void Discard()
    {
        foreach (var change in changeset.Changes.ToList())
            changeset.Remove(change);
        preview = null;
        Bank = store.Load();
    }

    /// <summary>
    /// Writes the pending changes, all or nothing, after backing up the bank and every save they touch. On success
    /// the list is empty again and each open save is read back; on a refusal the changes stay, so nothing is lost.
    /// </summary>
    /// <exception cref="ChangesetWriteException">Writing failed; what was written was put back.</exception>
    public ChangesetResult Save(DateTimeOffset now)
    {
        var result = runner.Apply(changeset, now);
        if (!result.Written)
            return result;

        changeset = new Changeset();
        preview = null;
        Bank = result.Bank!;
        foreach (var id in result.Saves.Keys)
            newSaves.Remove(id);
        foreach (var (id, save) in open.ToList())
        {
            // Read each save back, so the next changes start from what's on disk now.
            var bytes = save.Location.Read();
            changeset.Opened(id, bytes);
            if (SaveDetector.Detect(bytes) is { } detected)
                open[id] = save with { Save = detected.Save };
        }
        return result;
    }

    void Show(ChangesetResult result)
    {
        preview = result;
        Bank = result.Bank!;
    }

    /// <summary>
    /// The stored bank plus the saves opened for the first time that a pending change touches, so the runner knows
    /// them and the bank stores them with the change. A save that was only looked at is never added.
    /// </summary>
    sealed class WithNewSaves(IBankStore inner, PendingChanges owner) : IBankStore
    {
        public Bank Load()
        {
            var bank = inner.Load();
            var touched = owner.changeset.Saves;
            foreach (var save in owner.newSaves.Values)
                if (touched.Contains(save.Id) && bank.Saves.All(s => s.Id != save.Id))
                    bank.Saves.Add(save);
            return bank;
        }

        public void Save(Bank bank) => inner.Save(bank);
    }
}
