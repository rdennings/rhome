using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// English names for conversion notes, from PKHeX's own string tables. The notes are English for now;
/// the app can translate them once it has more than one language.
/// </summary>
static class EnglishNames
{
    static readonly GameStrings Strings = GameInfo.GetStrings("en");

    public static string Species(ushort species) => Strings.specieslist[species];

    public static string Move(ushort move) => Strings.movelist[move];

    public static string Item(int item) => Strings.itemlist[item];

    public static string Ability(int ability) => Strings.abilitylist[ability];

    public static string Ball(byte ball) => Strings.balllist[ball];
}
