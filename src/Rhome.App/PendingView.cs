using Rhome.Core;

namespace Rhome.App;

/// <summary>Which way a pending change moves a Pokémon, for the dot beside it in the pending list.</summary>
public enum PendingDirection
{
    /// <summary>From a game into the bank.</summary>
    IntoBank,

    /// <summary>From the bank out to a game.</summary>
    OutToGame,

    /// <summary>A move or swap inside the bank, or a change to the bank's boxes.</summary>
    InBank,
}

/// <summary>One line of the pending list, e.g. "Kirlia · Lv22" going "Emerald Box 3 → Bank Box 1".</summary>
/// <param name="Notes">How many notes it has (what a withdraw drops, say), or a moveset to pick: shown in Review.</param>
public sealed record PendingLine(string Name, string Route, PendingDirection Direction, int Notes);

/// <summary>
/// What the screens show about the pending changes (issue #49, mockup A): one line per change saying where the
/// Pokémon goes, and which slots a Pokémon is arriving in or leaving, so the box grids can mark them until Save.
/// </summary>
public static class PendingView
{
    public static IReadOnlyList<PendingLine> Lines(PendingChanges pending)
    {
        var bank = pending.Bank;
        return [.. pending.Outcomes.Select(o =>
        {
            var notes = o.Notes.Count + (o.Movesets is null ? 0 : 1);
            switch (o.Change)
            {
                case ImportChange i when Find(bank, Imported(i)) is { } pokemon:
                    return new PendingLine(Name(pokemon), $"{Place(pending, i.SaveId, i.From)} → Bank {BoxName(bank, pokemon.Slot)}",
                        PendingDirection.IntoBank, notes);
                case WithdrawChange w when Find(bank, w.PokemonId) is { } pokemon:
                    var to = pokemon.CheckedOut is { } away ? Place(pending, away.SaveId, away.Slot) : pending.SaveName(w.SaveId);
                    return new PendingLine(Name(pokemon), $"Bank {BoxName(bank, pokemon.Slot)} → {to}", PendingDirection.OutToGame, notes);
                case MoveInBankChange m when Find(bank, m.PokemonId) is { } pokemon:
                    return new PendingLine(Name(pokemon), o.Description.StartsWith("Swap", StringComparison.Ordinal)
                        ? o.Description
                        : $"To Bank {bank.Boxes.Describe(m.To)}", PendingDirection.InBank, notes);
                default:
                    return new PendingLine(o.Description, "", PendingDirection.InBank, notes);
            }
        })];
    }

    /// <summary>The slots of a save that a pending withdraw fills, and the box slots a pending import empties.</summary>
    public static (IReadOnlySet<SaveSlot> Arriving, IReadOnlySet<SaveSlot> Leaving) InSave(PendingChanges pending, Guid saveId)
    {
        var arriving = new HashSet<SaveSlot>();
        var leaving = new HashSet<SaveSlot>();
        foreach (var change in pending.Outcomes.Select(o => o.Change))
        {
            if (change is ImportChange i && i.SaveId == saveId && !i.From.IsParty)
                leaving.Add(i.From); // the party closes up behind a Pokémon that leaves it, so no party slot stays empty
            else if (change is WithdrawChange w && Find(pending.Bank, w.PokemonId)?.CheckedOut is { } away && away.SaveId == saveId)
                arriving.Add(away.Slot);
        }
        leaving.ExceptWith(arriving);
        return (arriving, leaving);
    }

    /// <summary>The bank slots a pending import fills.</summary>
    public static IReadOnlySet<BankSlot> ArrivingInBank(PendingChanges pending) =>
        pending.Outcomes.Select(o => o.Change).OfType<ImportChange>()
            .Select(i => Find(pending.Bank, Imported(i))?.Slot).OfType<BankSlot>().ToHashSet();

    static Guid? Imported(ImportChange import) => import.How.ReturningTo ?? import.NewId;

    static BankPokemon? Find(Bank bank, Guid? id) => id is null ? null : bank.Pokemon.Find(p => p.Id == id);

    static string Name(BankPokemon pokemon) =>
        $"{(pokemon.Summary.IsNicknamed ? pokemon.Summary.Nickname : EnglishNames.Species(pokemon.Summary.Species))} · Lv{pokemon.Summary.Level}";

    static string BoxName(Bank bank, BankSlot slot) => bank.Boxes.Box(slot.Box)?.Name ?? $"box {slot.Box}";

    /// <summary>"Emerald Box 3", or "Emerald party".</summary>
    static string Place(PendingChanges pending, Guid saveId, SaveSlot slot)
    {
        var name = pending.SaveName(saveId);
        if (slot.IsParty)
            return $"{name} party";
        try
        {
            return $"{name} {SaveContents.BoxName(pending.Save(saveId), slot.Box!.Value)}";
        }
        catch (KeyNotFoundException)
        {
            return $"{name} box {slot.Box}"; // a save that isn't open on this screen
        }
    }
}
