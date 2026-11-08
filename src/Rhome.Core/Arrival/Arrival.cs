using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// The game's own bookkeeping when a Pokémon arrives, so it looks caught or traded there rather than just
/// appearing in a box (PLAN.md §1 lesson 1, §2 "Arrival bookkeeping"). One module per game family, each with
/// a checklist next to it in <c>CHECKLIST.md</c> saying what a real catch or trade changes and what rhome does
/// about each item.
/// </summary>
public static class Arrival
{
    /// <summary>
    /// What a trade does to the Pokémon itself, before it's written. In Gen 3–5 that's only for eggs: an egg
    /// that arrives in another trainer's game is marked as received in a link trade, as a real trade would.
    /// </summary>
    public static IReadOnlyList<string> Adapt(SaveFile save, PKM pk)
    {
        if (!pk.IsEgg || pk is not (PK4 or PK5))
            return [];
        var (location, date) = (pk.MetLocation, pk.MetDate);
        switch (pk)
        {
            case PK4 pk4:
                pk4.UpdateHandler(save);
                break;
            case PK5 pk5:
                pk5.UpdateHandler(save);
                break;
        }
        pk.RefreshChecksum();
        return (pk.MetLocation, pk.MetDate) == (location, date)
            ? []
            : ["The egg is marked as received in a link trade, as it would be after a real trade."];
    }

    /// <summary>
    /// Records <paramref name="pk"/>'s arrival in <paramref name="save"/>. Call it after writing the Pokémon,
    /// with the Pokémon as written. Returns a line for each thing that changed, for the pending-changes summary.
    /// </summary>
    public static IReadOnlyList<string> Record(SaveFile save, PKM pk)
    {
        var changes = new List<string>();
        if (pk.IsEgg)
            return changes; // the games register a Pokémon when it hatches, not when an egg arrives

        switch (save)
        {
            case SAV3 sav3:
                Gen3(sav3, pk, changes);
                break;
            case SAV4 sav4:
                Gen4(sav4, pk, changes);
                break;
            case SAV5 sav5:
                Gen5(sav5, pk, changes);
                break;
            default:
                throw new NotSupportedException($"rhome has no arrival bookkeeping for {save.GetType().Name} yet.");
        }
        return changes;
    }

    /// <summary>
    /// Ruby, Sapphire, Emerald, FireRed, LeafGreen. The Pokédex keeps "owned" once and "seen" three times
    /// (the game only counts a species as seen when all three agree), plus the first Unown and Spinda seen,
    /// to draw their letter and spots. PKHeX's own Gen 3 code writes all of these; this mirrors it so it can
    /// be called on its own and say what it did.
    /// </summary>
    static void Gen3(SAV3 save, PKM pk, List<string> changes)
    {
        var species = pk.Species;
        var seen = save.GetSeen(species);
        if (!seen)
        {
            if (species == (ushort)Species.Unown)
            {
                save.SmallBlock.DexPIDUnown = pk.PID;
                changes.Add("Pokédex: the first Unown seen, so its letter is the one the Pokédex shows.");
            }
            else if (species == (ushort)Species.Spinda)
            {
                save.SmallBlock.DexPIDSpinda = pk.PID;
                changes.Add("Pokédex: the first Spinda seen, so its spots are the ones the Pokédex shows.");
            }
        }
        NoteOwned(save, pk, changes);
        save.SetSeen(species, true); // all three copies
        save.SetCaught(species, true);
    }

    /// <summary>
    /// Diamond, Pearl, Platinum, HeartGold, SoulSilver. The Pokédex keeps owned, seen, the genders seen, the
    /// forms seen (in the order first seen), the languages seen, and the first Spinda's PID. PKHeX's Zukan4
    /// covers most of it; rhome adds the Spinda and writes the two-form species' forms itself.
    /// </summary>
    static void Gen4(SAV4 save, PKM pk, List<string> changes)
    {
        var wasSeen = save.Dex.GetSeen(pk.Species);
        if (pk.Species == (ushort)Species.Spinda && !wasSeen)
        {
            save.Dex.SpindaPID = pk.PID;
            changes.Add("Pokédex: the first Spinda seen, so its spots are the ones the Pokédex shows.");
        }
        NoteOwned(save, pk, changes);

        var twoForms = TwoFormsOffset(save, pk.Species);
        var formsBefore = twoForms is { } at ? save.Dex.Data[at] : (byte)0;
        save.Dex.SetDex(pk);
        if (twoForms is { } offset)
            save.Dex.Data[offset] = TwoFormsSeen(formsBefore, wasSeen, pk.Form);
    }

    /// <summary>
    /// Where Gen 4 keeps the forms seen of a species with two forms stored one bit each (Shellos, Gastrodon, and
    /// from Platinum on Shaymin and Giratina), inside the Pokédex block. Same places PKHeX uses, checked against
    /// the <c>Pokedex</c> struct in pret/pokeplatinum (<c>include/pokedex.h</c>).
    /// </summary>
    static int? TwoFormsOffset(SAV4 save, ushort species)
    {
        const int formsSeen = 0x108; // after the magic, caught, seen, two gender blocks and the Spinda PID
        var later = formsSeen + (save is SAV4HGSS ? 0x3C : 0x20) + 0x1F4; // after Unown and the languages
        return species switch
        {
            (ushort)Species.Shellos => formsSeen,
            (ushort)Species.Gastrodon => formsSeen + 1,
            (ushort)Species.Shaymin when save is not SAV4DP => later + 4,
            (ushort)Species.Giratina when save is not SAV4DP => later + 5,
            _ => null,
        };
    }

    /// <summary>
    /// The game's own update of a two-form byte (<c>UpdateForms_TwoForms</c> in pret/pokeplatinum
    /// <c>src/pokedex.c</c>): bit 0 is the first form seen and bit 1 the second; the first form seen goes into
    /// both, so the two bits differ only once both forms are seen. PKHeX's Zukan4 writes its 0xFF "no form"
    /// marker into these bits instead, which reads back as no form or both forms, so rhome writes it itself.
    /// </summary>
    static byte TwoFormsSeen(byte before, bool speciesSeen, byte form)
    {
        var (first, second) = (before & 1, (before >> 1) & 1);
        var count = !speciesSeen ? 0 : first == second ? 1 : 2;
        var already = count >= 1 && first == form || count == 2 && second == form;
        if (already || count == 2)
            return before;

        var value = before;
        if (count == 0)
            value = (byte)((value & ~0b11) | (form & 1) | ((form & 1) << 1));
        else
            value = (byte)((value & ~0b10) | ((form & 1) << 1));
        return value;
    }

    /// <summary>
    /// Black, White, Black 2, White 2. The Pokédex keeps owned, seen and displayed per gender and shininess,
    /// forms seen and displayed, the languages seen, and the first Spinda's spot pattern. PKHeX's Zukan5 covers
    /// all of it.
    /// </summary>
    static void Gen5(SAV5 save, PKM pk, List<string> changes)
    {
        if (pk.Species == (ushort)Species.Spinda && !save.Zukan.GetSeen(pk.Species))
            changes.Add("Pokédex: the first Spinda seen, so its spots are the ones the Pokédex shows.");
        NoteOwned(save, pk, changes);
        save.Zukan.SetDex(pk);
    }

    static void NoteOwned(SaveFile save, PKM pk, List<string> changes)
    {
        var name = EnglishNames.Species(pk.Species);
        if (!save.GetCaught(pk.Species))
            changes.Add(save.GetSeen(pk.Species)
                ? $"Pokédex: {name} goes from seen to owned."
                : $"Pokédex: {name} is registered as seen and owned.");
    }
}
