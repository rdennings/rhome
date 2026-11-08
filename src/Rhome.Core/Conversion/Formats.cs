using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Converting a Pokémon between the MVP's three formats in either direction: forward with PKHeX (the same
/// route as Pal Park and Poké Transfer), backward with rhome's own converters.
/// </summary>
public static class Formats
{
    /// <summary>The generation a Gen 3–5 game's Pokémon are stored in, or 0 for any other game.</summary>
    public static int GenerationOf(GameVersion game) => game switch
    {
        GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG
            or GameVersion.RS or GameVersion.RSE or GameVersion.FRLG => 3,
        GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS
            or GameVersion.DP or GameVersion.DPPt or GameVersion.HGSS => 4,
        GameVersion.B or GameVersion.W or GameVersion.B2 or GameVersion.W2
            or GameVersion.BW or GameVersion.B2W2 => 5,
        _ => 0,
    };

    /// <summary>
    /// <paramref name="pk"/> in the format of <paramref name="target"/>, a Gen 3–5 game. The same format gives
    /// a copy. <paramref name="now"/> is the met date a forward conversion records, as Pal Park and Poké
    /// Transfer do.
    /// </summary>
    public static ConversionResult<PKM> Convert(PKM pk, GameVersion target, DateTimeOffset now)
    {
        var to = GenerationOf(target);
        if (to == 0)
            throw new ArgumentException($"{target} isn't a Gen 3–5 game.", nameof(target));

        // PKHeX hatches an egg on the way forward, and the downgrades refuse eggs, so an egg stays in its own generation.
        if (pk.IsEgg && pk.Format != to)
            return ConversionResult<PKM>.Refused($"Eggs can't move to a Gen {to} game from Gen {pk.Format}. Hatch it first.");

        return (pk, to) switch
        {
            (PK3 pk3, 3) => Same(pk3.Clone()),
            (PK4 pk4, 4) => Same(pk4.Clone()),
            (PK5 pk5, 5) => Same(pk5.Clone()),
            (PK3 pk3, 4) => Forward(pk3, p => ((PK3)p).ConvertToPK4(), now),
            (PK4 pk4, 5) => Forward(pk4, p => ((PK4)p).ConvertToPK5(), now),
            (PK3 pk3, 5) => Forward(pk3, p => ((PK3)p).ConvertToPK4().ConvertToPK5(), now),
            (PK4 pk4, 3) => Widen(Gen4To3.Convert(pk4, target)),
            (PK5 pk5, 4) => Widen(Gen5To4.Convert(pk5, target)),
            (PK5 pk5, 3) => Widen(Downgrade.Gen5ToGen3(pk5, target)),
            _ => throw new NotSupportedException($"rhome can't convert a {pk.GetType().Name} yet."),
        };
    }

    static ConversionResult<PKM> Same(PKM copy) => ConversionResult<PKM>.Converted(copy, []);

    static ConversionResult<PKM> Widen<T>(ConversionResult<T> result) where T : PKM =>
        result.Succeeded
            ? ConversionResult<PKM>.Converted(result.Pokemon, result.Notes)
            : ConversionResult<PKM>.Refused(result.Refusal);

    /// <summary>
    /// PKHeX's forward step, which does what Pal Park and Poké Transfer do, with two differences: friendship
    /// isn't reset to 70 (it's the Pokémon's progress, and follows it), and what PKHeX leaves out (HM moves,
    /// items the newer game can't take) is reported like the downgrades report theirs.
    /// </summary>
    static ConversionResult<PKM> Forward(PKM pk, Func<PKM, PKM> step, DateTimeOffset now)
    {
        var converted = step(pk);
        converted.OriginalTrainerFriendship = pk.OriginalTrainerFriendship;
        converted.MetDate = DateOnly.FromDateTime(now.Date);

        var notes = new List<ConversionNote>();
        var generation = converted.Format;
        for (var i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            if (move != 0 && !converted.HasMove(move))
                notes.Add(new(ChangeKind.Dropped, ChangedField.Moves,
                    $"Dropped the move {EnglishNames.Move(move)} (it can't be brought into Gen {generation}; Pal Park and Poké Transfer refuse HM moves)."));
        }
        if (pk.HeldItem != 0 && converted.HeldItem == 0)
            notes.Add(new(ChangeKind.Dropped, ChangedField.HeldItem,
                $"Dropped the held item {EnglishNames.Item(ItemConverter.GetItemForFormat(pk.HeldItem, pk.Context, EntityContext.Gen4))} (can't be held in Gen {generation})."));

        converted.RefreshChecksum();
        return ConversionResult<PKM>.Converted(converted, notes);
    }
}
