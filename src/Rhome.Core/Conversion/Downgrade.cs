using PKHeX.Core;

namespace Rhome.Core;

/// <summary>Downgrades that take more than one step.</summary>
public static class Downgrade
{
    /// <summary>
    /// Gen 5 → Gen 4 → Gen 3, through Platinum: it has every form and item a Gen 3 Pokémon can have, so the
    /// middle step never refuses or drops something Gen 3 could keep. A refusal at either step refuses it all.
    /// </summary>
    public static ConversionResult<PK3> Gen5ToGen3(PK5 pk5, GameVersion target)
    {
        var gen4 = Gen5To4.Convert(pk5, GameVersion.Pt);
        if (!gen4.Succeeded)
            return ConversionResult<PK3>.Refused(gen4.Refusal);

        var gen3 = Gen4To3.Convert(gen4.Pokemon, target);
        if (!gen3.Succeeded)
            return gen3;

        // The Gen 3 step replaces the met data again, so the middle step's note about it would only confuse.
        return ConversionResult<PK3>.Converted(gen3.Pokemon,
            [.. gen4.Notes.Where(n => n.Field != ChangedField.MetData), .. gen3.Notes]);
    }
}
