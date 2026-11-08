using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Who a Pokémon read from a save is, as far as the bank can tell (PLAN.md §2 "The bank", §5).
/// </summary>
/// <param name="Match">The checked-out record it unmistakably is, or null.</param>
/// <param name="Candidates">Records it might be, for the UI to ask about. Empty when there's a match or no lead at all.</param>
public sealed record IdentityMatch(BankPokemon? Match, IReadOnlyList<BankPokemon> Candidates)
{
    public static IdentityMatch None { get; } = new(null, []);
}

/// <summary>
/// Recognizing a Pokémon that comes back to the bank (issue #20). Gen 3–5 Pokémon have no encryption constant,
/// so the identity is the PID plus the original trainer's IDs and the origin game.
/// </summary>
public static class Identity
{
    /// <summary>
    /// Finds the bank record <paramref name="pk"/>, read from save <paramref name="saveId"/>, belongs to.
    /// </summary>
    /// <remarks>
    /// It's an unmistakable match only when exactly one record is checked out to this very save, and the
    /// snapshot rhome wrote there (in this format) has the same PID, trainer ID, secret ID, OT name and origin
    /// game. Comparing with that snapshot rather than the fingerprint is what recognizes a Gen 5 Pokémon that
    /// got a new PID in Gen 4 or 3. Anything weaker, such as the same PID and IDs on a record that's checked out
    /// elsewhere or sitting in the bank, is only a candidate: the user decides.
    /// </remarks>
    public static IdentityMatch Find(Bank bank, PKM pk, Guid saveId)
    {
        var format = pk.GetType().Name;
        var strong = new List<BankPokemon>();
        var weak = new List<BankPokemon>();

        foreach (var record in bank.Pokemon)
        {
            var written = record.SnapshotIn(format)?.ToPkm();
            if (record.CheckedOut?.SaveId == saveId && written is not null && SameIdentity(written, pk))
                strong.Add(record);
            else if (SharesIds(record, pk))
                weak.Add(record);
        }

        if (strong.Count == 1)
            return new IdentityMatch(strong[0], []);
        return weak.Count + strong.Count == 0 ? IdentityMatch.None : new IdentityMatch(null, [.. strong, .. weak]);
    }

    static bool SameIdentity(PKM a, PKM b) =>
        a.PID == b.PID
        && a.TID16 == b.TID16
        && a.SID16 == b.SID16
        && a.OriginalTrainerName == b.OriginalTrainerName
        && a.Version == b.Version;

    /// <summary>The same PID and trainer IDs as any of the record's snapshots, or as its fingerprint.</summary>
    static bool SharesIds(BankPokemon record, PKM pk)
    {
        var f = record.Fingerprint;
        if (f.PID == pk.PID && f.TID16 == pk.TID16 && f.SID16 == pk.SID16)
            return true;
        return record.Snapshots.Any(s =>
        {
            var old = s.ToPkm();
            return old.PID == pk.PID && old.TID16 == pk.TID16 && old.SID16 == pk.SID16;
        });
    }
}
