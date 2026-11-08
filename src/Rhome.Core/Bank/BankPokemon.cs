using System.Text.Json.Serialization;
using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// One Pokémon in the bank (PLAN.md §5). It keeps one snapshot per format it has lived in, so nothing has
/// to fit into a single superset struct, plus a summary for the UI and a fingerprint for matching it
/// when it comes back from a game.
/// </summary>
public sealed class BankPokemon
{
    /// <summary>The bank's own ID for this Pokémon. Never changes, wherever the Pokémon goes.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The bank slot it occupies. A checked-out Pokémon keeps its slot as a placeholder, so it comes back
    /// to the same place.
    /// </summary>
    public required BankSlot Slot { get; set; }

    /// <summary>Where the Pokémon is while it's out in a game, or null while it's in the bank.</summary>
    public CheckOut? CheckedOut { get; set; }

    [JsonIgnore]
    public bool IsInBank => CheckedOut is null;

    public required PokemonFingerprint Fingerprint { get; set; }

    public required PokemonSummary Summary { get; set; }

    /// <summary>The Pokémon's data in every format it has lived in, at most one per format.</summary>
    public List<Snapshot> Snapshots { get; init; } = [];

    /// <summary>The snapshot in a given format (e.g. "PK3"), or null when it has never lived in that format.</summary>
    public Snapshot? SnapshotIn(string format) => Snapshots.Find(s => s.Format == format);

    /// <summary>The most recently captured snapshot: the Pokémon as it is now.</summary>
    [JsonIgnore]
    public Snapshot Newest => Snapshots.MaxBy(s => s.CapturedAt)
        ?? throw new InvalidOperationException($"Pokémon {Id} has no snapshot.");

    /// <summary>Stores a snapshot, replacing the one in the same format if there is one.</summary>
    public void Keep(Snapshot snapshot)
    {
        Snapshots.RemoveAll(s => s.Format == snapshot.Format);
        Snapshots.Add(snapshot);
    }

    /// <summary>
    /// A new bank record for a Pokémon read from a save: fingerprint, summary and one snapshot. Placing it
    /// and recording where it came from is the import step's job (issue #18).
    /// </summary>
    public static BankPokemon From(PKM pk, BankSlot slot, Guid fromSave, DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        Slot = slot,
        Fingerprint = PokemonFingerprint.Of(pk),
        Summary = PokemonSummary.Of(pk),
        Snapshots = [Snapshot.Of(pk, fromSave, capturedAt)],
    };
}

/// <summary>A checked-out Pokémon's whereabouts: which save, and which slot in it.</summary>
public sealed record CheckOut(Guid SaveId, SaveSlot Slot);

/// <summary>
/// What identifies a Pokémon across games (PLAN.md §5), used to match one coming back from a game to
/// its bank record (issue #20).
/// </summary>
/// <param name="EncryptionConstant">Gen 6 and later only; null for the GBA and DS games.</param>
public sealed record PokemonFingerprint(
    uint PID,
    [property: JsonPropertyName("tid")] ushort TID16,
    [property: JsonPropertyName("sid")] ushort SID16,
    string OriginalTrainerName,
    GameVersion OriginGame,
    uint? EncryptionConstant)
{
    public static PokemonFingerprint Of(PKM pk) => new(
        pk.PID,
        pk.TID16,
        pk.SID16,
        pk.OriginalTrainerName,
        pk.Version,
        pk.Format >= 6 ? pk.EncryptionConstant : null);
}

/// <summary>What the UI shows about a Pokémon without opening a snapshot. Refreshed from the newest snapshot.</summary>
public sealed record PokemonSummary(
    ushort Species,
    byte Form,
    string Nickname,
    bool IsNicknamed,
    byte Level,
    bool IsShiny,
    byte Ball,
    byte Gender,
    Moveset Moves)
{
    public static PokemonSummary Of(PKM pk) => new(
        pk.Species,
        pk.Form,
        pk.Nickname,
        pk.IsNicknamed,
        pk.CurrentLevel,
        pk.IsShiny,
        pk.Ball,
        pk.Gender,
        Moveset.Of(pk));
}

/// <summary>
/// A Pokémon's four move slots, in order, as move numbers (0 = empty). Move numbers are the same in
/// every game, so two movesets from different games can be compared directly; the withdraw step needs
/// that to tell whether the moves it had last time in a game differ from its newest ones (PLAN.md §5).
/// </summary>
public readonly record struct Moveset(ushort Move1, ushort Move2, ushort Move3, ushort Move4)
{
    public static Moveset Of(PKM pk) => new(pk.Move1, pk.Move2, pk.Move3, pk.Move4);
}
