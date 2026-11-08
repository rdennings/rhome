using PKHeX.Core;

namespace Rhome.Core;

/// <summary>
/// English names for notes, refusals and the CLI, from PKHeX's own string tables. English for now; the app
/// can translate them once it has more than one language.
/// </summary>
public static class EnglishNames
{
    static readonly GameStrings Strings = GameInfo.GetStrings("en");

    public static string Species(ushort species) => Strings.specieslist[species];

    public static string Move(ushort move) => Strings.movelist[move];

    public static string Item(int item) => Strings.itemlist[item];

    public static string Ability(int ability) => Strings.abilitylist[ability];

    public static string Ball(byte ball) => Strings.balllist[ball];

    /// <summary>The game, as precisely as the save tells. Paired games that share a layout only say which pair.</summary>
    public static string Game(GameVersion game) => game switch
    {
        GameVersion.R => "Ruby",
        GameVersion.S => "Sapphire",
        GameVersion.RS => "Ruby/Sapphire",
        GameVersion.E => "Emerald",
        GameVersion.FR => "FireRed",
        GameVersion.LG => "LeafGreen",
        GameVersion.FRLG => "FireRed/LeafGreen",
        GameVersion.D => "Diamond",
        GameVersion.P => "Pearl",
        GameVersion.DP => "Diamond/Pearl",
        GameVersion.Pt => "Platinum",
        GameVersion.HG => "HeartGold",
        GameVersion.SS => "SoulSilver",
        GameVersion.HGSS => "HeartGold/SoulSilver",
        GameVersion.B => "Black",
        GameVersion.W => "White",
        GameVersion.BW => "Black/White",
        GameVersion.B2 => "Black 2",
        GameVersion.W2 => "White 2",
        GameVersion.B2W2 => "Black 2/White 2",
        _ => game.ToString(),
    };
}
