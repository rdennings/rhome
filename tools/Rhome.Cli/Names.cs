using PKHeX.Core;

namespace Rhome.Cli;

/// <summary>English names for what the CLI prints, from PKHeX's own string tables.</summary>
static class Names
{
    static readonly GameStrings Strings = GameInfo.GetStrings("en");

    /// <summary>
    /// The game, as precisely as the save tells. Paired games that share a layout only say which pair.
    /// </summary>
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

    /// <summary>Species name, with the form when it isn't the plain one (Unown-F, Wormadam-Sandy, …).</summary>
    public static string Species(PKM pk)
    {
        var name = Strings.specieslist[pk.Species];
        if (pk.Form == 0)
            return name;
        var form = ShowdownParsing.GetStringFromForm(pk.Form, Strings, pk.Species, pk.Context);
        return string.IsNullOrEmpty(form) ? name : $"{name}-{form}";
    }

    public static string Item(int item) => Strings.itemlist[item];

    public static string Gender(int gender) => gender switch { 0 => "♂", 1 => "♀", _ => "" };
}
