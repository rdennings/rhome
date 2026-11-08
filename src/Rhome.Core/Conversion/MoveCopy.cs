using PKHeX.Core;

namespace Rhome.Core;

/// <summary>Copying the four move slots into an older format, shared by the downgrade converters.</summary>
static class MoveCopy
{
    /// <summary>
    /// Copies every move up to <paramref name="maxMove"/>; the others are dropped with a note, and the moves
    /// after them move up so there are no gaps, as in the games. PP Ups come along, and PP is capped at the
    /// older format's maximum.
    /// </summary>
    public static void Copy(PKM from, PKM to, ushort maxMove, int generation, List<ConversionNote> notes)
    {
        var kept = 0;
        for (var i = 0; i < 4; i++)
        {
            var move = from.GetMove(i);
            if (move == 0)
                continue;
            if (move > maxMove)
            {
                notes.Add(new(ChangeKind.Dropped, ChangedField.Moves,
                    $"Dropped the move {EnglishNames.Move(move)} (not in Gen {generation})."));
                continue;
            }

            var ppUps = PPUps(from, i);
            to.SetMove(kept, move);
            SetPPUps(to, kept, ppUps);
            SetPP(to, kept, Math.Min(PP(from, i), to.GetMovePP(move, ppUps)));
            kept++;
        }
    }

    /// <summary>True when at least one of the Pokémon's moves exists in the older format.</summary>
    public static bool AnyUpTo(PKM pk, ushort maxMove) =>
        Enumerable.Range(0, 4).Select(pk.GetMove).Any(m => m is > 0 && m <= maxMove);

    static int PPUps(PKM pk, int index) => index switch
    {
        0 => pk.Move1_PPUps,
        1 => pk.Move2_PPUps,
        2 => pk.Move3_PPUps,
        _ => pk.Move4_PPUps,
    };

    static void SetPPUps(PKM pk, int index, int value)
    {
        switch (index)
        {
            case 0: pk.Move1_PPUps = value; break;
            case 1: pk.Move2_PPUps = value; break;
            case 2: pk.Move3_PPUps = value; break;
            default: pk.Move4_PPUps = value; break;
        }
    }

    static int PP(PKM pk, int index) => index switch
    {
        0 => pk.Move1_PP,
        1 => pk.Move2_PP,
        2 => pk.Move3_PP,
        _ => pk.Move4_PP,
    };

    static void SetPP(PKM pk, int index, int value)
    {
        switch (index)
        {
            case 0: pk.Move1_PP = value; break;
            case 1: pk.Move2_PP = value; break;
            case 2: pk.Move3_PP = value; break;
            default: pk.Move4_PP = value; break;
        }
    }
}
