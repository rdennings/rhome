using System.Text.Json.Serialization;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Where a Pokémon sits in a save, counted from 1 the way the games show it.
/// </summary>
/// <param name="Box">The box number, or null for the party.</param>
/// <param name="Slot">The slot in the party (1–6) or in the box (1–30).</param>
public readonly record struct SaveSlot(int? Box, int Slot)
{
    [JsonIgnore]
    public bool IsParty => Box is null;

    public override string ToString() => Box is { } box ? $"box {box} slot {Slot}" : $"party slot {Slot}";
}

/// <summary>A Pokémon and the slot it was found in.</summary>
public sealed record SlotContents(SaveSlot Where, PKM Pokemon);

/// <summary>
/// Read-only view of what a save holds: the party, and every occupied box slot.
/// The slot numbers here are the addresses later commands (import, withdraw) use.
/// </summary>
public static class SaveContents
{
    /// <summary>The party, in order.</summary>
    public static IReadOnlyList<SlotContents> Party(SaveFile save) =>
        save.PartyData.Select((pk, i) => new SlotContents(new SaveSlot(null, i + 1), pk)).ToList();

    /// <summary>Every occupied box slot, box by box. Empty slots are left out.</summary>
    public static IReadOnlyList<SlotContents> Boxes(SaveFile save)
    {
        var perBox = save.BoxSlotCount;
        return save.BoxData
            .Select((pk, i) => new SlotContents(new SaveSlot(i / perBox + 1, i % perBox + 1), pk))
            .Where(s => s.Pokemon.Species != 0)
            .ToList();
    }

    /// <summary>The name the player gave a box (1-based), or the game's default name for it.</summary>
    public static string BoxName(SaveFile save, int box) =>
        save is IBoxDetailName named ? named.GetBoxName(box - 1) : BoxDetailNameExtensions.GetDefaultBoxName(box - 1);

    /// <summary>True when the Pokémon's original trainer is the save's own trainer (name and full ID).</summary>
    public static bool IsFromThisTrainer(SaveFile save, PKM pk) =>
        pk.OriginalTrainerName == save.OT && pk.ID32 == save.ID32;
}
