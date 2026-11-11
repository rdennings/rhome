using PKHeX.Core;

namespace Rhome.Core;

/// <summary>One staged operation in a <see cref="Changeset"/>.</summary>
public abstract record Change;

/// <summary>Take the Pokémon in a save slot into the bank (<see cref="Transfers.Import"/>).</summary>
/// <param name="NewId">The ID the Pokémon gets if it becomes a new bank record. Give one when later changes in the same
/// changeset may move or withdraw it: every preview runs the import again, and would otherwise make a new ID each
/// time.</param>
public sealed record ImportChange(Guid SaveId, SaveSlot From, BankSlot? Into = null, ImportAs How = default, Guid? NewId = null) : Change;

/// <summary>Send a bank Pokémon into a save (<see cref="Transfers.Withdraw"/>).</summary>
public sealed record WithdrawChange(Guid PokemonId, Guid SaveId, SaveSlot? To = null, MovesetChoice Moves = MovesetChoice.Newest) : Change;

/// <summary>Move a Pokémon (or a checked-out placeholder) to another bank slot, swapping with whatever is there.</summary>
public sealed record MoveInBankChange(Guid PokemonId, BankSlot To) : Change;

/// <summary>Add an empty bank box at the end.</summary>
/// <param name="Id">The new box's ID (<see cref="BoxGroup.NextId"/> of the bank as the staged changes leave it). It's
/// part of the change so later changes can put Pokémon in the box, and undoing another change never renumbers it.</param>
public sealed record AddBoxChange(int Id, string Name) : Change;

/// <summary>Rename a bank box.</summary>
public sealed record RenameBoxChange(int BoxId, string Name) : Change;

/// <summary>Move a bank box to another place in the order. The Pokémon in it go along: they follow the box's ID.</summary>
/// <param name="Position">Where it goes, from 1 (first).</param>
public sealed record MoveBoxChange(int BoxId, int Position) : Change;

/// <summary>Delete a bank box. Only an empty one: no Pokémon, and no placeholder of one checked out into a game.</summary>
public sealed record DeleteBoxChange(int BoxId) : Change;

/// <summary>
/// Pending changes (PLAN.md §1 lesson 2, §2 "Safety"): operations staged by the user and applied all at once
/// on Save. Nothing touches the bank or a save until then. It also remembers the bytes of each save as the user
/// saw them, so a save that changed in the meantime (the game was played) is refused instead of overwritten.
/// </summary>
public sealed class Changeset
{
    readonly Dictionary<Guid, string> _seen = [];
    readonly List<Change> _changes = [];

    public IReadOnlyList<Change> Changes => _changes;

    public bool IsEmpty => _changes.Count == 0;

    /// <summary>Records the save's bytes as the user is seeing them. Call it when a save is opened.</summary>
    public void Opened(Guid saveId, ReadOnlySpan<byte> contents) => _seen[saveId] = SaveHash.Of(contents);

    /// <summary>The hash of the save as the user saw it, or null when it was never opened.</summary>
    public string? SeenHash(Guid saveId) => _seen.GetValueOrDefault(saveId);

    /// <summary>Stages a change. Every save it touches must have been <see cref="Opened"/> first.</summary>
    public void Add(Change change)
    {
        foreach (var save in SavesOf(change))
            if (!_seen.ContainsKey(save))
                throw new InvalidOperationException($"Open save {save} before staging changes to it.");
        _changes.Add(change);
    }

    public bool Remove(Change change) => _changes.Remove(change);

    public void Clear() => _changes.Clear();

    /// <summary>Every save the staged changes touch.</summary>
    public IReadOnlyList<Guid> Saves => _changes.SelectMany(SavesOf).Distinct().ToList();

    static IEnumerable<Guid> SavesOf(Change change) => change switch
    {
        ImportChange i => [i.SaveId],
        WithdrawChange w => [w.SaveId],
        _ => [],
    };
}

/// <summary>What one change does, for the pending-changes summary.</summary>
/// <param name="Description">One line, e.g. "Withdraw Kirlia into Emerald, box 1 slot 2".</param>
public sealed record ChangeOutcome(Change Change, string Description, IReadOnlyList<ConversionNote> Notes, IReadOnlyList<string> Bookkeeping);

/// <summary>The outcome of previewing or applying a changeset.</summary>
public sealed class ChangesetResult
{
    internal ChangesetResult(IReadOnlyList<ChangeOutcome> outcomes, string? refusal, Change? failed,
        IReadOnlyList<BankPokemon> candidates, bool written, Bank? bank = null,
        IReadOnlyDictionary<Guid, SaveFile>? saves = null)
    {
        Saves = saves ?? new Dictionary<Guid, SaveFile>();
        Outcomes = outcomes;
        Refusal = refusal;
        Failed = failed;
        Candidates = candidates;
        Written = written;
        Bank = bank;
    }

    /// <summary>
    /// The bank as the changes leave it: what the screens show while changes are pending, and the bank written on
    /// apply. Null for a refusal.
    /// </summary>
    public Bank? Bank { get; }

    /// <summary>
    /// Each save the changes touch, as they leave it, by <see cref="BankSave.Id"/>: what the split view shows while
    /// changes are pending. Empty for a refusal.
    /// </summary>
    public IReadOnlyDictionary<Guid, SaveFile> Saves { get; }

    /// <summary>What each change does (or did), in order. For a refusal, the changes before the one that failed.</summary>
    public IReadOnlyList<ChangeOutcome> Outcomes { get; }

    /// <summary>Why nothing was written, or null.</summary>
    public string? Refusal { get; }

    /// <summary>The change that couldn't be done, or null when the refusal is about a save as a whole.</summary>
    public Change? Failed { get; }

    /// <summary>When an import needs the user to say which bank Pokémon it is (see <see cref="TransferResult.Candidates"/>).</summary>
    public IReadOnlyList<BankPokemon> Candidates { get; }

    /// <summary>True once the saves and the bank were written.</summary>
    public bool Written { get; }

    public bool Succeeded => Refusal is null;

    /// <summary>Every note and bookkeeping line, flattened, for a quick list.</summary>
    public IEnumerable<string> Lines =>
        Outcomes.SelectMany(o => new[] { o.Description }
            .Concat(o.Notes.Select(n => "  " + n.Message))
            .Concat(o.Bookkeeping.Select(b => "  " + b)));
}

/// <summary>
/// The changeset was valid and everything was built, but writing a file failed. The saves already written were
/// put back; the bank is as it was. Backups taken before the write are kept.
/// </summary>
public sealed class ChangesetWriteException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Previews and applies changesets against the stored bank and the saves (PLAN.md §2 "Safety", §6.6).
/// </summary>
/// <remarks>
/// Applying is all or nothing. Every save is read again and refused if it changed since it was opened, or isn't
/// a valid save rhome can work with (unknown, cut short, failing its checksums). Then every change runs in
/// memory, and every result is checked: each new save must read back as the same game with valid checksums and
/// the same size, and the bank must pass its own rules. Only then are the backups made (each save, and the bank),
/// the saves overwritten in place, and the bank written last. If a write fails, the saves written so far are put
/// back.
/// </remarks>
public sealed class ChangesetRunner(IBankStore bankStore, Func<Guid, ISaveLocation?> saves, IBackups backups)
{
    /// <summary>Runs the changes in memory and says what they would do. Nothing is written.</summary>
    public ChangesetResult Preview(Changeset changeset, DateTimeOffset now) => Run(changeset, now, commit: false);

    /// <summary>Applies the changes, all or nothing.</summary>
    /// <exception cref="ChangesetWriteException">Writing a file failed; what was written was put back.</exception>
    public ChangesetResult Apply(Changeset changeset, DateTimeOffset now) => Run(changeset, now, commit: true);

    sealed record OpenSave(Guid Id, ISaveLocation Location, byte[] Original, SaveFile Save);

    ChangesetResult Run(Changeset changeset, DateTimeOffset now, bool commit)
    {
        var bank = bankStore.Load();
        var bankBefore = BankJson.ToBytes(bank);

        var open = new Dictionary<Guid, OpenSave>();
        foreach (var id in changeset.Saves)
        {
            var (opened, problem) = Open(bank, changeset, id);
            if (opened is null)
                return Refused(problem, null, [], []);
            open[id] = opened;
        }

        var outcomes = new List<ChangeOutcome>();
        foreach (var change in changeset.Changes)
        {
            var (outcome, problem, candidates) = Do(bank, open, change, now);
            if (problem is not null)
                return Refused(problem, change, outcomes, candidates);
            outcomes.Add(outcome!);
        }

        if (!commit)
            return new ChangesetResult(outcomes, null, null, [], written: false, bank, SavesOf(open));

        // Build every file before touching any, and check each one reads back.
        var newSaves = new List<(OpenSave Save, byte[] Bytes)>();
        foreach (var save in open.Values)
        {
            var bytes = save.Save.Write().ToArray();
            if (Check(save, bytes) is { } problem)
                throw new InvalidOperationException($"rhome built a save it can't read back ({problem}); nothing was written.");
            newSaves.Add((save, bytes));
            var index = bank.Saves.FindIndex(s => s.Id == save.Id);
            bank.Saves[index] = bank.Saves[index] with { LastSeenHash = SaveHash.Of(bytes) };
        }
        BankJson.ToBytes(bank); // throws if the bank breaks its rules, before anything is written

        backups.KeepBank(bankBefore, now);
        foreach (var save in open.Values)
            backups.KeepSave(save.Id, save.Original, now);

        var written = new List<OpenSave>();
        try
        {
            foreach (var (save, bytes) in newSaves)
            {
                save.Location.WriteInPlace(bytes);
                written.Add(save);
            }
            bankStore.Save(bank);
        }
        catch (Exception e)
        {
            var notRestored = new List<string>();
            foreach (var save in written)
            {
                try
                {
                    save.Location.WriteInPlace(save.Original);
                }
                catch (Exception)
                {
                    notRestored.Add(Name(bank, save.Id));
                }
            }
            var message = notRestored.Count == 0
                ? $"Writing failed ({e.Message}). Nothing was changed: the saves written so far were put back."
                : $"Writing failed ({e.Message}), and {string.Join(", ", notRestored)} couldn't be put back. Restore it from its backup.";
            throw new ChangesetWriteException(message, e);
        }

        return new ChangesetResult(outcomes, null, null, [], written: true, bank, SavesOf(open));
    }

    (OpenSave? Save, string Problem) Open(Bank bank, Changeset changeset, Guid id)
    {
        var name = Name(bank, id);
        if (bank.Saves.All(s => s.Id != id))
            return (null, $"The bank doesn't know save {id}.");
        if (saves(id) is not { } location)
            return (null, $"rhome doesn't know where {name} is on this device.");

        var bytes = location.Read();
        if (SaveHash.Of(bytes) != changeset.SeenHash(id))
            return (null, $"{name} changed since it was opened (was the game saved in the meantime?). Open it again; nothing was written.");

        var detected = SaveDetector.Detect(bytes);
        if (detected is null)
            return (null, $"{name} isn't a save rhome recognizes any more: it may be damaged or cut short. Nothing was written.");
        if (!detected.IsSupported)
            return (null, $"{name} is a Gen {detected.Generation} save; rhome only works with the GBA and DS games so far.");
        if (!detected.Save.ChecksumsValid)
            return (null, $"{name} fails its own checksums, so the game may treat it as damaged. rhome won't write to it.");
        return (new OpenSave(id, location, bytes, detected.Save), "");
    }

    /// <summary>Why a save rhome built doesn't read back as the save it should be, or null when it does.</summary>
    static string? Check(OpenSave save, byte[] bytes)
    {
        if (bytes.Length != save.Original.Length)
            return $"its size changed from {save.Original.Length} to {bytes.Length} bytes";
        var reread = SaveDetector.Detect(bytes);
        if (reread is null || reread.Save.GetType() != save.Save.GetType())
            return "it isn't recognized as the same game";
        if (!reread.Save.ChecksumsValid)
            return "its checksums don't match";
        return null;
    }

    static (ChangeOutcome? Outcome, string? Problem, IReadOnlyList<BankPokemon> Candidates) Do(
        Bank bank, Dictionary<Guid, OpenSave> open, Change change, DateTimeOffset now)
    {
        switch (change)
        {
            case ImportChange i:
            {
                var result = Transfers.Import(bank, open[i.SaveId].Save, i.SaveId, i.From, now, i.Into, i.How, i.NewId);
                if (!result.Succeeded)
                    return (null, result.Refusal, result.Candidates);
                var what = $"{(result.Returned ? "Return" : "Import")} {Species(result.Pokemon)} from {Name(bank, i.SaveId)}, {i.From}, " +
                           $"to {bank.Boxes.Describe(result.Pokemon.Slot)}";
                return (new ChangeOutcome(change, what, result.Notes, result.Bookkeeping), null, []);
            }
            case WithdrawChange w:
            {
                if (bank.Pokemon.All(p => p.Id != w.PokemonId))
                    return (null, $"The bank has no Pokémon {w.PokemonId}.", []);
                var result = Transfers.Withdraw(bank, w.PokemonId, open[w.SaveId].Save, w.SaveId, now, w.To, w.Moves);
                if (!result.Succeeded)
                    return (null, result.Refusal, []);
                var what = $"Withdraw {Species(result.Pokemon)} into {Name(bank, w.SaveId)}, {result.SaveSlot}";
                return (new ChangeOutcome(change, what, result.Notes, result.Bookkeeping), null, []);
            }
            case MoveInBankChange m:
            {
                var record = bank.Pokemon.Find(p => p.Id == m.PokemonId);
                if (record is null)
                    return (null, $"The bank has no Pokémon {m.PokemonId}.", []);
                if (bank.Boxes.Box(m.To.Box) is null || m.To.Slot is < 1 or > BankBox.SlotCount)
                    return (null, $"{bank.Boxes.Describe(m.To)} doesn't exist.", []);
                var from = record.Slot;
                var other = bank.At(m.To);
                record.Slot = m.To;
                if (other is not null && other != record)
                    other.Slot = from;
                var what = other is null || other == record
                    ? $"Move {Species(record)} to {bank.Boxes.Describe(m.To)}"
                    : $"Swap {Species(record)} ({bank.Boxes.Describe(from)}) with {Species(other)} ({bank.Boxes.Describe(m.To)})";
                return (new ChangeOutcome(change, what, [], []), null, []);
            }
            case AddBoxChange a:
            {
                var (name, problem) = BoxGroup.CleanName(a.Name);
                if (problem is not null)
                    return (null, problem, []);
                if (a.Id < 1 || bank.Boxes.Box(a.Id) is not null)
                    return (null, $"There's already a box with ID {a.Id}.", []);
                bank.Boxes.Boxes.Add(new BankBox { Id = a.Id, Name = name! });
                return (Done(change, $"Add box {name}"), null, []);
            }
            case RenameBoxChange r:
            {
                if (bank.Boxes.Box(r.BoxId) is not { } box)
                    return (null, NoBox(r.BoxId), []);
                var (name, problem) = BoxGroup.CleanName(r.Name);
                if (problem is not null)
                    return (null, problem, []);
                var old = box.Name;
                box.Name = name!;
                return (Done(change, $"Rename box {old} to {name}"), null, []);
            }
            case MoveBoxChange mb:
            {
                if (bank.Boxes.Box(mb.BoxId) is not { } box)
                    return (null, NoBox(mb.BoxId), []);
                var boxes = bank.Boxes.Boxes;
                if (mb.Position < 1 || mb.Position > boxes.Count)
                    return (null, $"There's no place {mb.Position}: the bank has {boxes.Count} boxes.", []);
                boxes.Remove(box);
                boxes.Insert(mb.Position - 1, box);
                return (Done(change, $"Move box {box.Name} to place {mb.Position}"), null, []);
            }
            case DeleteBoxChange d:
            {
                if (bank.Boxes.Box(d.BoxId) is not { } box)
                    return (null, NoBox(d.BoxId), []);
                if (bank.Boxes.Boxes.Count == 1)
                    return (null, "The bank needs at least one box.", []);
                var inside = bank.Pokemon.Where(p => p.Slot.Box == box.Id).ToList();
                if (inside.Count > 0)
                {
                    var away = inside.Count(p => !p.IsInBank);
                    return (null, $"{box.Name} isn't empty: move its {Plural(inside.Count, "Pokémon")} out first" +
                                  (away > 0 ? $" ({away} of them out in a game, holding their place)." : "."), []);
                }
                bank.Boxes.Boxes.Remove(box);
                return (Done(change, $"Delete box {box.Name}"), null, []);
            }
            default:
                throw new NotSupportedException($"Unknown change {change.GetType().Name}.");
        }
    }

    static string Species(BankPokemon pokemon) => EnglishNames.Species(pokemon.Summary.Species);

    static Dictionary<Guid, SaveFile> SavesOf(Dictionary<Guid, OpenSave> open) => open.ToDictionary(o => o.Key, o => o.Value.Save);

    static ChangeOutcome Done(Change change, string description) => new(change, description, [], []);

    static string NoBox(int id) => $"The bank has no box with ID {id}.";

    static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}";

    static string Name(Bank bank, Guid id) => bank.Saves.Find(s => s.Id == id)?.DisplayName ?? $"save {id}";

    static ChangesetResult Refused(string problem, Change? failed, IReadOnlyList<ChangeOutcome> outcomes, IReadOnlyList<BankPokemon> candidates) =>
        new(outcomes, problem, failed, candidates, written: false);
}
