using System.Globalization;
using System.Text.Json;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Sprites;

/// <summary>What a sprite shows: a species and form, shiny or not, male or female, or an egg.</summary>
/// <param name="Format">The generation of the data the form number comes from (Gen 4 numbers Arceus' forms differently).</param>
public sealed record SpriteRequest(ushort Species, byte Form, bool IsShiny, bool IsFemale, bool IsEgg, int Format)
{
    public static SpriteRequest Of(PKM pk) =>
        new(pk.Species, pk.Form, pk.IsShiny, pk.Gender == 1, pk.IsEgg, pk.Format);

    /// <summary>A bank Pokémon, from its newest data: as it is now, or as it was when it left for a game.</summary>
    public static SpriteRequest Of(BankPokemon pokemon) => Of(pokemon.Newest.ToPkm());
}

/// <summary>
/// Which file in PokeAPI/sprites shows a Pokémon (issue #48, PLAN.md risk R5). rhome shows Black and White's sprites
/// for every Pokémon, in every game. They're loaded at runtime from a pinned commit of that repo and never shipped.
/// The table (<c>Sprites/sprites.json</c>) is written by <c>tools/Rhome.SpriteTable</c> and holds only numbers and
/// file names: the file for each form that has one, and which shiny and female files exist. So rhome only ever asks
/// for a file that's there. A form without a file of its own shows the species' plain sprite.
/// </summary>
public sealed class SpriteTable
{
    readonly Dictionary<int, Dictionary<int, string>> forms;
    readonly HashSet<string> noShiny;
    readonly HashSet<string> female;
    readonly HashSet<string> femaleShiny;

    SpriteTable(File file)
    {
        Commit = file.Commit;
        Folder = file.Folder;
        MaxSpecies = file.MaxSpecies;
        Egg = file.Egg;
        forms = file.Forms.ToDictionary(
            s => int.Parse(s.Key, CultureInfo.InvariantCulture),
            s => s.Value.ToDictionary(f => int.Parse(f.Key, CultureInfo.InvariantCulture), f => f.Value));
        noShiny = [.. file.NoShiny];
        female = [.. file.Female];
        femaleShiny = [.. file.FemaleShiny];
    }

    /// <summary>The commit of PokeAPI/sprites every file comes from. A file at a commit never changes.</summary>
    public string Commit { get; }

    /// <summary>The folder in that repo: Black and White's sprites.</summary>
    public string Folder { get; }

    /// <summary>The highest species number with a sprite.</summary>
    public int MaxSpecies { get; }

    string? Egg { get; }

    /// <summary>The table that ships with rhome.</summary>
    public static SpriteTable Default { get; } = Load();

    static SpriteTable Load()
    {
        using var stream = typeof(SpriteTable).Assembly.GetManifestResourceStream("Rhome.App.Sprites.sprites.json")
            ?? throw new InvalidOperationException("sprites.json isn't in the app.");
        return Parse(stream);
    }

    public static SpriteTable Parse(Stream json) =>
        new(JsonSerializer.Deserialize<File>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The sprite table is empty."));

    /// <summary>The file that shows it, relative to <see cref="Folder"/>, e.g. "shiny/female/25.png"; null when there's none.</summary>
    public string? PathFor(SpriteRequest request)
    {
        if (request.IsEgg)
            return Egg is null ? null : Egg + ".png";
        if (request.Species == 0 || request.Species > MaxSpecies)
            return null;

        var stem = forms.GetValueOrDefault(request.Species)?.GetValueOrDefault(FormNow(request))
            ?? request.Species.ToString(CultureInfo.InvariantCulture);
        var shiny = request.IsShiny && !noShiny.Contains(stem);
        var isFemale = request.IsFemale && (shiny ? femaleShiny : female).Contains(stem);
        return (shiny ? "shiny/" : "") + (isFemale ? "female/" : "") + stem + ".png";
    }

    /// <summary>Where to load a file from: jsDelivr, a CDN for GitHub repos, at the pinned commit.</summary>
    public Uri UrlFor(string path) => new($"https://cdn.jsdelivr.net/gh/PokeAPI/sprites@{Commit}/{Folder}/{path}");

    /// <summary>
    /// Every file for species up to <paramref name="maxSpecies"/>: each form, shiny and female, and the egg. What
    /// "Download sprites" fetches, so rhome works offline.
    /// </summary>
    public IReadOnlyList<string> AllPaths(int maxSpecies)
    {
        var paths = new List<string>();
        for (var species = 1; species <= Math.Min(maxSpecies, MaxSpecies); species++)
        {
            var stems = forms.TryGetValue(species, out var byForm) ? byForm.Values : Enumerable.Empty<string>();
            foreach (var stem in stems.Prepend(species.ToString(CultureInfo.InvariantCulture)).Distinct())
            {
                paths.Add(stem + ".png");
                if (!noShiny.Contains(stem))
                    paths.Add($"shiny/{stem}.png");
                if (female.Contains(stem))
                    paths.Add($"female/{stem}.png");
                if (femaleShiny.Contains(stem))
                    paths.Add($"shiny/female/{stem}.png");
            }
        }
        if (Egg is not null)
            paths.Add(Egg + ".png");
        return paths;
    }

    /// <summary>
    /// The form number in the table's numbering (Gen 9's). Gen 4 has an extra Arceus form, ??? (the Curse type), before
    /// Fire: it shows as plain Arceus, and the forms after it move down one.
    /// </summary>
    static int FormNow(SpriteRequest request) => request is { Species: (ushort)Species.Arceus, Format: 4 } && request.Form >= 9
        ? request.Form == 9 ? 0 : request.Form - 1
        : request.Form;

    sealed record File(
        string Commit,
        string Folder,
        int MaxSpecies,
        string? Egg,
        Dictionary<string, Dictionary<string, string>> Forms,
        string[] NoShiny,
        string[] Female,
        string[] FemaleShiny);
}
