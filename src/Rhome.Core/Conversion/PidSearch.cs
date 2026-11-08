using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// Finding a new personality value (PID) for a Gen 5 Pokémon going down to Gen 4 or 3. Gen 5 stores the
/// nature on its own; the older games work it out from the PID (PID mod 25). So a Gen 5 Pokémon whose
/// nature doesn't match its PID gets a new one there, chosen to keep everything else the PID decides
/// (Rodney, 2026-11-08: "new PID, same nature"). The bank keeps the original and restores it in Gen 5.
/// </summary>
static class PidSearch
{
    /// <summary>
    /// The PID closest to <paramref name="original"/> (searching upward from its high half) that gives the
    /// same nature, gender, shininess and Wurmple evolution, the ability slot <paramref name="abilitySlot"/>
    /// when the species has two, and for Unown the same letter in Gen 3. Deterministic, so converting the same
    /// Pokémon twice gives the same bytes.
    /// </summary>
    public static uint Find(PKM pk, Nature nature, int abilitySlot, bool hasTwoAbilities)
    {
        var original = pk.PID;
        var trainer = (uint)(pk.TID16 ^ pk.SID16);
        var shiny = ShinyXor(original, trainer) < 8;
        var wurmple = WurmpleBranch(original);

        for (uint step = 0; step <= ushort.MaxValue; step++)
        {
            var high = (ushort)((original >> 16) + step);
            if (WurmpleBranch((uint)high << 16) != wurmple)
                continue;

            if (shiny)
            {
                // Shiny when high ^ low ^ TID ^ SID < 8, so only eight low halves can work.
                var basis = high ^ trainer;
                for (uint k = 0; k < 8; k++)
                    if (Fits(((uint)high << 16) | ((basis ^ k) & 0xFFFF)))
                        return ((uint)high << 16) | ((basis ^ k) & 0xFFFF);
            }
            else
            {
                for (uint low = 0; low <= ushort.MaxValue; low++)
                    if (Fits(((uint)high << 16) | low))
                        return ((uint)high << 16) | low;
            }
        }
        throw new InvalidOperationException($"No PID keeps {pk.Species}'s nature, gender and shininess together.");

        bool Fits(uint pid) =>
            pid % 25 == (uint)nature
            && ShinyXor(pid, trainer) < 8 == shiny
            && EntityGender.GetFromPID(pk.Species, pid) == pk.Gender
            && (!hasTwoAbilities || (pid & 1) == abilitySlot)
            && (pk.Species != (ushort)Species.Unown || EntityPID.GetUnownForm3(pid) == pk.Form);
    }

    static uint ShinyXor(uint pid, uint trainer) => (pid >> 16) ^ (pid & 0xFFFF) ^ trainer;

    /// <summary>Wurmple evolves into Silcoon or Cascoon by the PID's high half (Gen 3 to 5).</summary>
    static bool WurmpleBranch(uint pid) => (pid >> 16) % 10 < 5;
}
