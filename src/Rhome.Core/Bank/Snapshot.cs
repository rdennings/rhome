using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// A Pokémon's raw data in one format, exactly as a game stored it (PLAN.md §5). Kept so that sending it
/// back to a game in that format restores everything the format holds, even fields no other format has.
/// </summary>
/// <param name="Format">The PKHeX format name: "PK3", "PK4" or "PK5" for the MVP's games.</param>
/// <param name="Data">The decrypted box data (no party stats; those are recalculated). Base64 in the JSON.</param>
/// <param name="CapturedAt">When rhome read it.</param>
/// <param name="FromSave">The save it was read from, or null when it didn't come from a save.</param>
public sealed record Snapshot(string Format, byte[] Data, DateTimeOffset CapturedAt, Guid? FromSave)
{
    /// <summary>The formats a snapshot can be turned back into a Pokémon from today.</summary>
    public static IReadOnlyList<string> KnownFormats { get; } = ["PK3", "PK4", "PK5"];

    public static Snapshot Of(PKM pk, Guid? fromSave, DateTimeOffset capturedAt)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return new(pk.GetType().Name, data, capturedAt, fromSave);
    }

    /// <summary>The Pokémon this snapshot holds, ready to write into a save of its own format.</summary>
    public PKM ToPkm() => Format switch
    {
        "PK3" => new PK3((byte[])Data.Clone()),
        "PK4" => new PK4((byte[])Data.Clone()),
        "PK5" => new PK5((byte[])Data.Clone()),
        _ => throw new NotSupportedException($"rhome can't read {Format} snapshots yet."),
    };

    // Records compare arrays by reference; compare the bytes instead.
    public bool Equals(Snapshot? other) =>
        other is not null
        && Format == other.Format
        && Data.AsSpan().SequenceEqual(other.Data)
        && CapturedAt == other.CapturedAt
        && FromSave == other.FromSave;

    public override int GetHashCode() => HashCode.Combine(Format, Data.Length, CapturedAt, FromSave);
}
