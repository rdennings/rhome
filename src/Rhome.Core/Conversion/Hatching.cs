namespace Rhome.Core;

/// <summary>
/// How each generation records a Pokémon hatched from an egg, and the EXP it gives level 1. They differ, so a
/// converter that copies the met level or EXP as they are gets them wrong. Found on the Thor: a Platinum
/// Togepi that hatched at level 1 showed in Emerald as level 0, met at level 5.
/// </summary>
/// <remarks>
/// <code>
/// Gen   hatches at   met level it records   EXP for level 1
///  2       5                 1                    (n/a)
///  3       5                 0                     1
///  4       1                 0                     0
///  5+      1                 1                     0
/// </code>
/// Hatch and met levels are PKHeX's <c>EggStateLegality</c>; the Gen 3 EXP is from Emerald's ROM, whose EXP
/// tables match PKHeX's at every level but 1, for all six growth rates. Gen 3 reads a met level of 0 as
/// "hatched" and shows its own hatch level, 5, even when the met location says otherwise.
/// Any new converter between generations should go through these instead of copying the fields.
/// </remarks>
public static class Hatching
{
    /// <summary>The level an egg hatches at in <paramref name="generation"/>.</summary>
    public static byte HatchLevel(int generation) => generation <= 3 ? (byte)5 : (byte)1;

    /// <summary>The met level <paramref name="generation"/> records for a Pokémon that hatched there.</summary>
    public static byte MetLevelRecorded(int generation) => generation is 3 or 4 ? (byte)0 : (byte)1;

    /// <summary>
    /// The least EXP a Pokémon can have in <paramref name="generation"/>: Gen 3 puts level 1 at 1 EXP, and
    /// computes level 0 for a Pokémon with less.
    /// </summary>
    public static uint MinimumExp(int generation) => generation == 3 ? 1u : 0u;
}
