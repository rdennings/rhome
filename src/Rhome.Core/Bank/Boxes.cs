namespace Rhome.Core;

/// <summary>A place in the bank.</summary>
/// <param name="Box">The box's <see cref="BankBox.Id"/>, not its position, so reordering boxes moves no Pokémon.</param>
/// <param name="Slot">The slot in that box, 1–30.</param>
public readonly record struct BankSlot(int Box, int Slot)
{
    public override string ToString() => $"bank box {Box} slot {Slot}";
}

/// <summary>
/// One bank box. Which Pokémon sit in it is recorded on each Pokémon (<see cref="BankPokemon.Slot"/>), by
/// the box's ID, so renaming or reordering a box never touches the Pokémon in it.
/// </summary>
public sealed class BankBox
{
    /// <summary>Every bank box has 30 slots, like the GBA and DS games.</summary>
    public const int SlotCount = 30;

    /// <summary>The longest box name, in characters.</summary>
    public const int MaxNameLength = 30;

    /// <summary>The box's permanent ID within its group. Never changes, wherever the box is moved to.</summary>
    public required int Id { get; init; }

    public required string Name { get; set; }
}

/// <summary>A named set of boxes. The MVP has one; more groups are a later idea.</summary>
public sealed class BoxGroup
{
    /// <summary>
    /// How many boxes a new bank starts with. Ten is plenty while testing; plan to raise it to around 100
    /// before launch (M3).
    /// </summary>
    public const int DefaultBoxCount = 10;

    public string Name { get; set; } = "Bank";

    /// <summary>The boxes in the order the user sees them. Reorder freely: Pokémon follow their box's ID.</summary>
    public List<BankBox> Boxes { get; init; } = [];

    /// <summary>The box with this ID, or null when there's none.</summary>
    public BankBox? Box(int id) => Boxes.Find(b => b.Id == id);

    /// <summary>An ID no box in the group has: one more than the highest.</summary>
    public int NextId => Boxes.Count == 0 ? 1 : Boxes.Max(b => b.Id) + 1;

    /// <summary>Where the box is in the order the user sees, from 1; 0 when there's no box with this ID.</summary>
    public int Position(int id) => Boxes.FindIndex(b => b.Id == id) + 1;

    /// <summary>Adds a box at the end, with an ID no other box in the group has.</summary>
    public BankBox Add(string name)
    {
        var box = new BankBox { Id = NextId, Name = name };
        Boxes.Add(box);
        return box;
    }

    /// <summary>
    /// A box name as rhome keeps it: trimmed, and checked. Returns the name, or why it can't be one (empty, or longer
    /// than <see cref="BankBox.MaxNameLength"/>).
    /// </summary>
    public static (string? Name, string? Problem) CleanName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return (null, "A box needs a name.");
        if (trimmed.Length > BankBox.MaxNameLength)
            return (null, $"A box name can be at most {BankBox.MaxNameLength} characters.");
        return (trimmed, null);
    }

    /// <summary>What to call a slot in the app: the box's name and the slot, e.g. "Starters slot 3".</summary>
    public string Describe(BankSlot slot) => Box(slot.Box) is { } box ? $"{box.Name} slot {slot.Slot}" : slot.ToString();

    /// <summary>A group of <see cref="DefaultBoxCount"/> boxes: ID 1 named "Box 1", ID 2 named "Box 2" and so on.</summary>
    public static BoxGroup CreateDefault()
    {
        var group = new BoxGroup();
        for (var i = 1; i <= DefaultBoxCount; i++)
            group.Add($"Box {i}");
        return group;
    }
}
