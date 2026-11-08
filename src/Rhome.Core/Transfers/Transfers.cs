using System.Diagnostics.CodeAnalysis;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>The outcome of an import or withdraw: what happened, or why nothing did.</summary>
public sealed class TransferResult
{
    TransferResult(BankPokemon? pokemon, SaveSlot? saveSlot, PKM? written, string? refusal,
        IReadOnlyList<ConversionNote> notes, IReadOnlyList<string> bookkeeping)
    {
        Pokemon = pokemon;
        SaveSlot = saveSlot;
        Written = written;
        Refusal = refusal;
        Notes = notes;
        Bookkeeping = bookkeeping;
    }

    /// <summary>The bank record that was created (import) or checked out (withdraw).</summary>
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

    [MemberNotNullWhen(true, nameof(Pokemon), nameof(SaveSlot))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool Succeeded => Refusal is null;

    internal static TransferResult Done(BankPokemon pokemon, SaveSlot slot, PKM? written,
        IReadOnlyList<ConversionNote> notes, IReadOnlyList<string> bookkeeping) =>
        new(pokemon, slot, written, null, notes, bookkeeping);

    internal static TransferResult Refused(string reason) => new(null, null, null, reason, [], []);
}

/// <summary>
/// Moving Pokémon between a save and the bank (PLAN.md §2 "Transfers", §5). Both work on a save and a bank in
/// memory; writing them to storage, all at once and with backups, is the changeset's job (issue #21). A refused
/// transfer changes neither.
/// </summary>
public static class Transfers
{
    /// <summary>
    /// Takes the Pokémon in <paramref name="from"/> out of the save and into the bank, as a new record with a
    /// snapshot. It goes to <paramref name="into"/>, or the bank's first free slot. The save slot is emptied; a
    /// party closes the gap, as in the games.
    /// </summary>
    /// <param name="saveId">The bank's ID for this save (<see cref="BankSave.Id"/>); it must be in <see cref="Bank.Saves"/>.</param>
    public static TransferResult Import(Bank bank, SaveFile save, Guid saveId, SaveSlot from, DateTimeOffset now, BankSlot? into = null)
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

        var slot = into ?? bank.FirstFreeSlot();
        if (slot is not { } target)
            return TransferResult.Refused("Every bank box is full.");
        if (bank.Boxes.Box(target.Box) is null || target.Slot is < 1 or > BankBox.SlotCount)
            return TransferResult.Refused($"{target} doesn't exist.");
        if (bank.At(target) is not null)
            return TransferResult.Refused($"{target} is taken.");

        var record = BankPokemon.From(pk, target, saveId, now);
        bank.Pokemon.Add(record);
        if (from.IsParty)
            save.DeletePartySlot(index);
        else
            save.SetBoxSlotAtIndex(save.BlankPKM, index, EntityImportSettings.None);
        return TransferResult.Done(record, from, null, [], []);
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
    public static TransferResult Withdraw(Bank bank, Guid pokemonId, SaveFile save, Guid saveId, DateTimeOffset now, SaveSlot? to = null)
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

        var built = Build(bank, record, save, now);
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
    public static ConversionResult<PKM> Build(Bank bank, BankPokemon record, SaveFile save, DateTimeOffset now)
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
            var newestGame = bank.Saves.Find(s => s.Id == newest.FromSave)?.Game ?? DefaultGame(newestPk);
            FormatRestore.Apply(pk, newestPk, own.ToPkm(), newestGame, now, notes);
            notes.RemoveAll(n => Restored(n, pk));
        }
        return Fit(pk, save, notes);
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
