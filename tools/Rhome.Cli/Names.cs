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
    public static string Species(PKM pk) => Species(pk.Species, pk.Form, pk.Context);

    /// <summary>Species name and form, for a bank Pokémon's summary (forms named as in Gen 5, which knows them all).</summary>
    public static string Species(ushort species, byte form, EntityContext context = EntityContext.Gen5)
    {
        var name = Strings.specieslist[species];
        if (form == 0)
            return name;
        var formName = ShowdownParsing.GetStringFromForm(form, Strings, species, context);
        return string.IsNullOrEmpty(formName) ? name : $"{name}-{formName}";
    }

    /// <summary>The moves in order, e.g. "Confusion / Double Team / Teleport".</summary>
    public static string Moves(Rhome.Core.Moveset moves) =>
        string.Join(" / ", new[] { moves.Move1, moves.Move2, moves.Move3, moves.Move4 }.Where(m => m != 0).Select(m => Strings.movelist[m]));

    public static string Item(int item) => Strings.itemlist[item];

    public static string Gender(int gender) => gender switch { 0 => "♂", 1 => "♀", _ => "" };
}
