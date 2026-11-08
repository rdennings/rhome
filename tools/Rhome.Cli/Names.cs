using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Cli;

/// <summary>English names for what the CLI prints, from PKHeX's own string tables.</summary>
static class Names
{
    static readonly GameStrings Strings = GameInfo.GetStrings("en");

    /// <summary>The game, as precisely as the save tells. Paired games that share a layout only say which pair.</summary>
    public static string Game(GameVersion game) => EnglishNames.Game(game);

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
