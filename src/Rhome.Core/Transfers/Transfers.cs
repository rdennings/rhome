using System.Diagnostics.CodeAnalysis;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>The outcome of an import or withdraw: what happened, or why nothing did.</summary>
public sealed class TransferResult
{
    TransferResult(BankPokemon? pokemon, SaveSlot? saveSlot, PKM? written, string? refusal,
        IReadOnlyList<ConversionNote> notes, IReadOnlyList<string> bookkeeping, bool returned = false,
        IReadOnlyList<BankPokemon>? candidates = null)
    {
        Pokemon = pokemon;
        SaveSlot = saveSlot;
        Written = written;
        Refusal = refusal;
        Notes = notes;
        Bookkeeping = bookkeeping;
        Returned = returned;
        Candidates = candidates ?? [];
    }

    /// <summary>
    /// The bank record that was created or returned to (import), or checked out (withdraw).
    /// </summary>
    public BankPokemon? Pokemon { get; }

    /// <summary>The save slot it came from (import) or went into (withdraw).</summary>
    public SaveSlot? SaveSlot { get; }

    /// <summary>The Pokémon as written into the save (withdraw only).</summary>
    public PKM? Written { get; }

    /// <summary>Why nothing happened, or null when it did.</summary>
    public string? Refusal { get; }

    /// <summary>Everything that was dropped or replaced on the way, for the pending-changes summary.</summary>
    public IReadOnlyList<ConversionNote> Notes { get; }

    /// <summary>What the arrival bookkeeping changed in the save (withdraw only), e.g. the Pokédex.</summary>
    public IReadOnlyList<string> Bookkeeping { get; }

    /// <summary>True when an import recognized a checked-out Pokémon coming back, and merged it into its record.</summary>
    public bool Returned { get; }

    /// <summary>
    /// When an import was refused because the Pokémon might be one of these bank records: the UI asks which (or
    /// whether it's a new one) and imports again with <see cref="ImportAs"/>.
    /// </summary>
    public IReadOnlyList<BankPokemon> Candidates { get; }

    [MemberNotNullWhen(true, nameof(Pokemon), nameof(SaveSlot))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool Succeeded => Refusal is null;

    internal static TransferResult Done(BankPokemon pokemon, SaveSlot slot, PKM? written,
        IReadOnlyList<ConversionNote> notes, IReadOnlyList<string> bookkeeping, bool returned = false) =>
        new(pokemon, slot, written, null, notes, bookkeeping, returned);

    internal static TransferResult Refused(string reason) => new(null, null, null, reason, [], []);

    internal static TransferResult Ask(IReadOnlyList<BankPokemon> candidates) =>
        new(null, null, null,
            "This might be a Pokémon that's already in the bank. Choose which one it is, or import it as a new one.",
            [], [], candidates: candidates);
}

/// <summary>How an import treats a Pokémon the bank may already know.</summary>
/// <param name="ReturningTo">The record it is: the user's answer when the import listed candidates.</param>
/// <param name="AsNew">A new record, whatever the bank knows.</param>
public readonly record struct ImportAs(Guid? ReturningTo, bool AsNew)
{
    /// <summary>Merge into the record it unmistakably is; ask when it might be one; otherwise a new record.</summary>
    public static ImportAs Auto => default;

    public static ImportAs New => new(null, true);

    public static ImportAs Return(Guid record) => new(record, false);
}

/// <summary>Which moves a Pokémon takes into a game it has been in before (PLAN.md §5).</summary>
public enum MovesetChoice
{
    /// <summary>The moves it knows now, from the game it was last in. The default.</summary>
    Newest,

    /// <summary>The moves it had the last time it was in a game of this format.</summary>
    LastInThisGame,
}

/// <summary>The two movesets a withdraw can offer. <see cref="LastInThisGame"/> is null when there's no choice to make.</summary>
public sealed record MovesetOptions(Moveset Newest, Moveset? LastInThisGame);

/// <summary>
/// Moving Pokémon between a save and the bank (PLAN.md §2 "Transfers", §5). Both work on a save and a bank in
/// memory; writing them to storage, all at once and with backups, is the changeset's job (issue #21). A refused
/// transfer changes neither.
/// </summary>
public static class Transfers
{
    /// <summary>
    /// Takes the Pokémon in <paramref name="from"/> out of the save and into the bank. The save slot is emptied;
    /// a party closes the gap, as in the games.
    /// </summary>
    /// <remarks>
    /// A Pokémon coming back from a game it was withdrawn into is merged into its record (<see cref="Identity"/>):
    /// its snapshot in this format is replaced by the one it comes back with, so its progress follows the newest
    /// game, and it returns to its placeholder slot. Anything else becomes a new record with a snapshot, in
    /// <paramref name="into"/> or the bank's first free slot. When it only might be a known Pokémon, nothing
    /// happens and the result lists the candidates; <paramref name="how"/> carries the user's answer.
    /// </remarks>
    /// <param name="saveId">The bank's ID for this save (<see cref="BankSave.Id"/>); it must be in <see cref="Bank.Saves"/>.</param>
    public static TransferResult Import(Bank bank, SaveFile save, Guid saveId, SaveSlot from, DateTimeOffset now,
        BankSlot? into = null, ImportAs how = default)
    {
        if (bank.Saves.All(s => s.Id != saveId))
            throw new ArgumentException($"The bank doesn't know save {saveId}.", nameof(saveId));
        if (!SaveDetector.IsSupported(save))
            return TransferResult.Refused("rhome only works with the GBA and DS games so far.");

        if (!TryIndex(save, from, out var index, out var problem))
            return TransferResult.Refused(problem);
        var pk = from.IsParty ? save.GetPartySlotAtIndex(index) : save.GetBoxSlotAtIndex(index);
        if (pk.Species == 0)
            return TransferResult.Refused($"There's no Pokémon in {from}.");
        if (!pk.ChecksumValid)
            return TransferResult.Refused($"The Pokémon in {from} fails its checksum, so its data can't be trusted.");
        if (from.IsParty && !SaveContents.Party(save).Any(s => s.Where != from && !s.Pokemon.IsEgg))
            return TransferResult.Refused("That's the last Pokémon in the party that isn't an egg, and the game needs one.");

        BankPokemon? returning = null;
        if (!how.AsNew)
        {
            var match = Identity.Find(bank, pk, saveId);
            if (how.ReturningTo is { } chosen)
            {
                returning = bank.Pokemon.Find(p => p.Id == chosen)
                    ?? throw new ArgumentException($"The bank has no Pokémon {chosen}.", nameof(how));
                if (returning != match.Match && !match.Candidates.Contains(returning))
                    return TransferResult.Refused("That bank Pokémon doesn't share this one's personality value and trainer IDs, so it can't be the same Pokémon.");
                if (returning.IsInBank)
                    return TransferResult.Refused("That one is in the bank already, not out in a game.");
            }
            else if (match.Match is not null)
                returning = match.Match;
            else if (match.Candidates.Count > 0)
                return TransferResult.Ask(match.Candidates);
        }

        if (returning is not null)
        {
            Empty(save, from, index);
            returning.Keep(Snapshot.Of(pk, saveId, now));
            returning.Summary = PokemonSummary.Of(pk);
            returning.CheckedOut = null;
            return TransferResult.Done(returning, from, null, [], [], returned: true);
        }

        var slot = into ?? bank.FirstFreeSlot();
        if (slot is not { } target)
            return TransferResult.Refused("Every bank box is full.");
        if (bank.Boxes.Box(target.Box) is null || target.Slot is < 1 or > BankBox.SlotCount)
            return TransferResult.Refused($"{target} doesn't exist.");
        if (bank.At(target) is not null)
            return TransferResult.Refused($"{target} is taken.");

        var record = BankPokemon.From(pk, target, saveId, now);
        bank.Pokemon.Add(record);
        Empty(save, from, index);
        return TransferResult.Done(record, from, null, [], []);
    }

    static void Empty(SaveFile save, SaveSlot slot, int index)
    {
        if (slot.IsParty)
            save.DeletePartySlot(index);
        else
            save.SetBoxSlotAtIndex(save.BlankPKM, index, EntityImportSettings.None);
    }

    /// <summary>
    /// Sends a bank Pokémon into the save, converted to the save's format (PLAN.md §5): from its newest snapshot,
    /// forward with PKHeX or backward with rhome's converters, then with the format's own fields restored from
    /// the snapshot it had in that format. It goes into <paramref name="to"/> (the next party slot, or an empty
    /// box slot), or the first empty box slot, and the game's arrival bookkeeping is done (<see cref="Arrival"/>).
    /// Its bank slot stays as a checked-out placeholder, and the written Pokémon is kept as its snapshot in the
    /// save's format.
    /// </summary>
    /// <param name="saveId">The bank's ID for this save; it must be in <see cref="Bank.Saves"/>.</param>
    /// <param name="moves">Which moveset it takes, when it has been in a game of this format before (<see cref="Movesets"/>).</param>
    public static TransferResult Withdraw(Bank bank, Guid pokemonId, SaveFile save, Guid saveId, DateTimeOffset now,
        SaveSlot? to = null, MovesetChoice moves = MovesetChoice.Newest)
    {
        if (bank.Saves.All(s => s.Id != saveId))
            throw new ArgumentException($"The bank doesn't know save {saveId}.", nameof(saveId));
        var record = bank.Pokemon.Find(p => p.Id == pokemonId)
            ?? throw new ArgumentException($"The bank has no Pokémon {pokemonId}.", nameof(pokemonId));
        if (!SaveDetector.IsSupported(save))
            return TransferResult.Refused("rhome only works with the GBA and DS games so far.");
        if (record.CheckedOut is { } away)
            return TransferResult.Refused($"It's already out, in {SaveName(bank, away.SaveId)}.");

        var slot = to ?? FirstEmptyBoxSlot(save);
        if (slot is not { } target)
            return TransferResult.Refused("Every box in the save is full.");
        if (!TryIndex(save, target, out var index, out var problem))
            return TransferResult.Refused(problem);
        if (target.IsParty ? index != save.PartyCount : save.GetBoxSlotAtIndex(index).Species != 0)
            return TransferResult.Refused(target.IsParty
                ? save.PartyCount == 6 ? "The party is full." : $"The next free party slot is {save.PartyCount + 1}."
                : $"{target} isn't empty.");

        var built = Build(bank, record, save, now, moves);
        if (!built.Succeeded)
            return TransferResult.Refused(built.Refusal);
        var pk = built.Pokemon;

        // Structural check (PLAN.md §2): everything the Pokémon refers to exists in this game.
        var errata = save.EvaluateCompatibility(pk);
        if (errata.Count > 0)
            return TransferResult.Refused($"It can't go into {Names(save)}: {string.Join("; ", errata)}");
        if (!pk.ChecksumValid)
            throw new InvalidOperationException("rhome built a Pokémon with a bad checksum; nothing was written.");

        // PKHeX's own write hooks stay off: arrival bookkeeping is done explicitly, so it can say what it did.
        var bookkeeping = Arrival.Adapt(save, pk).ToList();
        if (target.IsParty)
            save.SetPartySlotAtIndex(pk, index, EntityImportSettings.None);
        else
            save.SetBoxSlotAtIndex(pk, index, EntityImportSettings.None);
        bookkeeping.AddRange(Arrival.Record(save, pk));

        record.Keep(Snapshot.Of(pk, saveId, now));
        record.Summary = PokemonSummary.Of(pk);
        record.CheckedOut = new CheckOut(saveId, target);
        return TransferResult.Done(record, target, pk, built.Notes, bookkeeping);
    }

    /// <summary>The Pokémon a withdraw would write, without writing it: the conversion and restore steps.</summary>
    public static ConversionResult<PKM> Build(Bank bank, BankPokemon record, SaveFile save, DateTimeOffset now,
        MovesetChoice moves = MovesetChoice.Newest)
    {
        var newest = record.Newest;
        var format = save.PKMType.Name;
        if (newest.Format == format)
            return Fit(record.Newest.ToPkm(), save, []);

        var newestPk = newest.ToPkm();
        var converted = Formats.Convert(newestPk, SaveDetector.GameOf(save), now);
        if (!converted.Succeeded)
            return converted;

        var notes = converted.Notes.ToList();
        var pk = converted.Pokemon;
        if (record.SnapshotIn(format) is { } own)
        {
            var ownPk = own.ToPkm();
            var newestGame = bank.Saves.Find(s => s.Id == newest.FromSave)?.Game ?? DefaultGame(newestPk);
            FormatRestore.Apply(pk, newestPk, ownPk, newestGame, now, notes);
            notes.RemoveAll(n => Restored(n, pk));
            ChooseMoves(pk, newestPk, ownPk, moves, save.MaxMoveID, notes);
        }
        KeepFullPP(pk, newestPk);
        return Fit(pk, save, notes);
    }

    /// <summary>
    /// A move whose PP was full stays full. A few moves have less PP in older games, so without this a move
    /// that went to Gen 3 at full PP would come home short.
    /// </summary>
    static void KeepFullPP(PKM pk, PKM newest)
    {
        for (var i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            var j = move == 0 ? -1 : newest.GetMoveIndex(move);
            if (j < 0)
                continue;
            if (newest.GetPP(j) == newest.GetMovePP(move, newest.GetPPUps(j)))
                pk.SetPP(i, pk.GetMovePP(move, pk.GetPPUps(i)));
        }
    }

    /// <summary>
    /// The movesets a withdraw into <paramref name="save"/> can offer: the newest one (after the rule for moves
    /// the game lacks), and the one it had last time in a game of this format when that's different.
    /// </summary>
    public static MovesetOptions Movesets(Bank bank, BankPokemon record, SaveFile save, DateTimeOffset now)
    {
        var built = Build(bank, record, save, now);
        var newest = built.Succeeded ? Moveset.Of(built.Pokemon) : record.Summary.Moves;
        var last = record.Newest.Format == save.PKMType.Name ? null : record.SnapshotIn(save.PKMType.Name);
        var lastMoves = last is null ? (Moveset?)null : Moveset.Of(last.ToPkm());
        return new MovesetOptions(newest, lastMoves == newest ? null : lastMoves);
    }

    /// <summary>
    /// PLAN.md §5: moves follow the newest game by default. A newest move this game doesn't have is replaced by
    /// the move in that slot from the last time it was here, or dropped; either way the summary says so. Or,
    /// when the user picks it, the moveset it had here last time, as it was.
    /// </summary>
    static void ChooseMoves(PKM pk, PKM newest, PKM own, MovesetChoice choice, int maxMove, List<ConversionNote> notes)
    {
        notes.RemoveAll(n => n.Field == ChangedField.Moves);
        var chosen = new List<(ushort Move, int PPUps, int PP)>();
        if (choice == MovesetChoice.LastInThisGame)
        {
            for (var i = 0; i < 4; i++)
                if (own.GetMove(i) is var move and not 0)
                    chosen.Add((move, own.GetPPUps(i), own.GetPP(i)));
        }
        else
        {
            var known = Enumerable.Range(0, 4).Select(newest.GetMove).Where(m => m != 0 && m <= maxMove).ToHashSet();
            for (var i = 0; i < 4; i++)
            {
                var move = newest.GetMove(i);
                if (move == 0)
                    continue;
                if (move <= maxMove)
                {
                    chosen.Add((move, newest.GetPPUps(i), newest.GetPP(i)));
                    continue;
                }

                var before = own.GetMove(i);
                if (before is not 0 && before <= maxMove && !known.Contains(before) && chosen.All(c => c.Move != before))
                {
                    chosen.Add((before, own.GetPPUps(i), own.GetPP(i)));
                    notes.Add(new(ChangeKind.Replaced, ChangedField.Moves,
                        $"The move {EnglishNames.Move(move)} isn't in Gen {pk.Format}, so that slot gets back {EnglishNames.Move(before)}, the move it had there last time."));
                }
                else
                {
                    notes.Add(new(ChangeKind.Dropped, ChangedField.Moves, $"Dropped the move {EnglishNames.Move(move)} (not in Gen {pk.Format})."));
                }
            }
        }

        for (var i = 0; i < 4; i++)
        {
            var (move, ppUps, pp) = i < chosen.Count ? chosen[i] : ((ushort)0, 0, 0);
            pk.SetMove(i, move);
            pk.SetPPUps(i, ppUps);
            pk.SetPP(i, move == 0 ? 0 : Math.Min(pp, pk.GetMovePP(move, ppUps)));
        }
    }

    /// <summary>A note about something the restore step brought back no longer describes the result.</summary>
    static bool Restored(ConversionNote note, PKM pk) => note.Field switch
    {
        ChangedField.MetData or ChangedField.Ribbons or ChangedField.PersonalityValue => true,
        ChangedField.HeldItem => pk.HeldItem != 0,
        _ => false,
    };

    /// <summary>The last adjustments for the exact game: an item that game can't hold is dropped.</summary>
    static ConversionResult<PKM> Fit(PKM pk, SaveFile save, List<ConversionNote> notes)
    {
        var item = (ushort)pk.HeldItem;
        if (item != 0 && !pk.CanHoldItem(save.HeldItems))
        {
            notes.Add(new(ChangeKind.Dropped, ChangedField.HeldItem,
                $"Dropped the held item {EnglishNames.Item(ItemConverter.GetItemForFormat(item, pk.Context, EntityContext.Gen4))} (can't be held in {Names(save)})."));
            pk.HeldItem = 0;
        }
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return ConversionResult<PKM>.Converted(pk, notes);
    }

    static GameVersion DefaultGame(PKM pk) => pk.Format switch
    {
        3 => GameVersion.E,
        4 => GameVersion.Pt,
        _ => GameVersion.W2,
    };

    /// <summary>The 0-based index PKHeX uses for a slot, or why the slot doesn't exist in this save.</summary>
    static bool TryIndex(SaveFile save, SaveSlot slot, out int index, out string problem)
    {
        problem = "";
        if (slot.IsParty)
        {
            index = slot.Slot - 1;
            if (slot.Slot is >= 1 and <= 6)
                return true;
        }
        else
        {
            index = (slot.Box!.Value - 1) * save.BoxSlotCount + slot.Slot - 1;
            if (slot.Box is >= 1 && slot.Box <= save.BoxCount && slot.Slot >= 1 && slot.Slot <= save.BoxSlotCount)
                return true;
        }
        problem = $"This {Names(save)} save has no {slot}.";
        return false;
    }

    static SaveSlot? FirstEmptyBoxSlot(SaveFile save)
    {
        var index = save.NextOpenBoxSlot();
        if (index < 0 || index >= save.SlotCount)
            return null;
        return new SaveSlot(index / save.BoxSlotCount + 1, index % save.BoxSlotCount + 1);
    }

    static string SaveName(Bank bank, Guid id) => bank.Saves.Find(s => s.Id == id)?.DisplayName ?? "another save";

    static string Names(SaveFile save) => EnglishNames.Game(SaveDetector.GameOf(save));
}
