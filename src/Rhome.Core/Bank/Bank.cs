namespace Rhome.Core;

/// <summary>
/// Everything the bank holds (PLAN.md §5). A personal bank is a few MB at most, so it is loaded fully
/// into memory and written back whole. The JSON form of this type is also the export file.
/// </summary>
/// <remarks>
/// Saves are listed by game and trainer only. Where a save lives on this device is kept outside the
/// bank (by the platform layer, keyed by <see cref="BankSave.Id"/>), because a file path means nothing
/// on another device and must never travel with the bank when sync arrives.
/// </remarks>
public sealed class Bank
{
    /// <summary>The bank's boxes. The MVP has exactly one group.</summary>
    public BoxGroup Boxes { get; init; } = BoxGroup.CreateDefault();

    /// <summary>Every save the bank has seen.</summary>
    public List<BankSave> Saves { get; init; } = [];

    /// <summary>Every Pokémon in the bank, including those checked out into a save.</summary>
    public List<BankPokemon> Pokemon { get; init; } = [];

    /// <summary>The Pokémon parked in a bank slot, or null when the slot is empty.</summary>
    public BankPokemon? At(BankSlot slot) => Pokemon.Find(p => p.Slot == slot);

    /// <summary>The first slot no Pokémon holds, box by box in display order, or null when every box is full.</summary>
    public BankSlot? FirstFreeSlot()
    {
        var taken = Pokemon.Select(p => p.Slot).ToHashSet();
        foreach (var box in Boxes.Boxes)
        for (var slot = 1; slot <= BankBox.SlotCount; slot++)
        {
            var candidate = new BankSlot(box.Id, slot);
            if (!taken.Contains(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Checks the rules the types alone can't: unique IDs, slots in boxes that exist and aren't shared, one
    /// snapshot per format, and check-outs that point at a known save. Returns every problem found.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (Boxes.Boxes.Count == 0)
            problems.Add("the bank has no boxes");
        foreach (var id in Duplicates(Boxes.Boxes.Select(b => b.Id)))
            problems.Add($"box ID {id} is used by more than one box");

        foreach (var id in Duplicates(Saves.Select(s => s.Id)))
            problems.Add($"save {id} is listed more than once");
        foreach (var id in Duplicates(Pokemon.Select(p => p.Id)))
            problems.Add($"Pokémon {id} is listed more than once");

        var saveIds = Saves.Select(s => s.Id).ToHashSet();
        var boxIds = Boxes.Boxes.Select(b => b.Id).ToHashSet();
        foreach (var pokemon in Pokemon)
        {
            var (box, slot) = (pokemon.Slot.Box, pokemon.Slot.Slot);
            if (!boxIds.Contains(box) || slot < 1 || slot > BankBox.SlotCount)
                problems.Add($"Pokémon {pokemon.Id} is in {pokemon.Slot}, which doesn't exist");

            if (pokemon.Snapshots.Count == 0)
                problems.Add($"Pokémon {pokemon.Id} has no snapshot");
            foreach (var format in Duplicates(pokemon.Snapshots.Select(s => s.Format)))
                problems.Add($"Pokémon {pokemon.Id} has more than one {format} snapshot");

            if (pokemon.CheckedOut is { } checkedOut && !saveIds.Contains(checkedOut.SaveId))
                problems.Add($"Pokémon {pokemon.Id} is checked out to save {checkedOut.SaveId}, which the bank doesn't know");
        }

        foreach (var slot in Duplicates(Pokemon.Select(p => p.Slot)))
            problems.Add($"more than one Pokémon is in {slot}");

        return problems;
    }

    static IEnumerable<T> Duplicates<T>(IEnumerable<T> items) =>
        items.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key);
}
