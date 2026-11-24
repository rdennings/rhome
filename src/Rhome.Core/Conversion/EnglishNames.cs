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

    public static string Nature(Nature nature) => Strings.natures[(int)nature];

    /// <summary>The line the summary screen shows about its best IV (Gen 4 and later), e.g. "It hates to lose!".</summary>
    public static string Characteristic(int characteristic) => Strings.characteristics[characteristic];

    /// <summary>A ribbon by its PKHeX property name, e.g. "RibbonChampionSinnoh" → "Sinnoh Champion".</summary>
    public static string Ribbon(string property) =>
        Strings.Ribbons.GetNameSafe(property, out var name) ? name : property;

    /// <summary>
    /// Where a Pokémon was met, or where it was received as an egg when <paramref name="egg"/> is true, read the way
    /// the Pokémon's own format and origin generation number locations.
    /// </summary>
    /// <remarks>
    /// The DS games renumber where a Pokémon from an older generation was met (Pal Park, Poké Transfer), so from Gen 4 on
    /// the location is read from the format's own list rather than the origin game's.
    /// </remarks>
    public static string Location(PKM pk, bool egg) =>
        GameInfo.GetLocationName(egg, egg ? pk.EggLocation : pk.MetLocation, pk.Format,
            pk.Format >= 4 ? pk.Format : pk.Generation, pk.Version);

    /// <summary>The item a Pokémon holds, or null when it holds none. Gen 3 numbers its items its own way.</summary>
    public static string? HeldItem(PKM pk) =>
        pk.HeldItem == 0 ? null
            : Item(pk.Format == 3 ? ItemConverter.GetItemForFormat(pk.HeldItem, EntityContext.Gen3, EntityContext.Gen4) : pk.HeldItem);

    /// <summary>Any game PKHeX knows, for where a Pokémon came from (which may be outside the MVP's games).</summary>
    public static string OriginGame(GameVersion game)
    {
        var name = Game(game);
        return name == game.ToString() ? Strings.gamelist[(int)game] : name;
    }

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
