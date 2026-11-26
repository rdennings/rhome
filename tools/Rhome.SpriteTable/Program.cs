using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;

// Usage: dotnet run --project tools/Rhome.SpriteTable -- <sprites-commit> <pokeapi-commit> [out.json]
//
// Writes the table rhome uses to find a Pokémon's sprite in PokeAPI/sprites (issue #48): which file each species and
// form uses, and which shiny and female files exist, all at one pinned commit of that repo. Numbers and names only:
// no image is downloaded or committed. Bumping the sprites commit means running this again with the new one.
//
// Form files are matched by name: PKHeX's English form names (Gen 9 numbering, a superset of Gen 3–5's) against
// PokeAPI's form identifiers (data/v2/csv/pokemon_forms.csv). A form with no file of its own is listed at the end and
// left out; rhome then shows the species' plain sprite.
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: Rhome.SpriteTable <sprites-commit> <pokeapi-commit> [out.json]");
    return 2;
}

const string Folder = "sprites/pokemon/versions/generation-v/black-white";
const int MaxSpecies = 1025;
var (spritesCommit, pokeapiCommit) = (args[0], args[1]);
var outPath = args.Length == 3 ? args[2] : "src/Rhome.App/Sprites/sprites.json";

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("rhome-sprite-table", "1"));

// Every file in the folder, relative to it, e.g. "201-b.png", "shiny/female/3.png".
var tree = JsonNode.Parse(await http.GetStringAsync(
    $"https://api.github.com/repos/PokeAPI/sprites/git/trees/{spritesCommit}:{Folder}?recursive=1"))!;
if (tree["truncated"]?.GetValue<bool>() == true)
    throw new InvalidOperationException("GitHub cut the file list short.");
var files = tree["tree"]!.AsArray().Where(n => (string)n!["type"]! == "blob").Select(n => (string)n!["path"]!).ToHashSet();
Console.WriteLine($"{files.Count} files in {Folder} at {spritesCommit}");

var csv = $"https://raw.githubusercontent.com/PokeAPI/pokeapi/{pokeapiCommit}/data/v2/csv/";
var speciesOfPokemon = Rows(await http.GetStringAsync(csv + "pokemon.csv"))
    .ToDictionary(r => int.Parse(r["id"], CultureInfo.InvariantCulture), r => int.Parse(r["species_id"], CultureInfo.InvariantCulture));
var apiForms = Rows(await http.GetStringAsync(csv + "pokemon_forms.csv"))
    .Where(r => r["form_identifier"] != "")
    .Select(r =>
    {
        var pokemon = int.Parse(r["pokemon_id"], CultureInfo.InvariantCulture);
        return new ApiForm(speciesOfPokemon[pokemon], pokemon, r["form_identifier"], r["is_default"] == "1");
    })
    .ToLookup(f => f.Species);

var strings = GameInfo.GetStrings("en");
var forms = new SortedDictionary<int, SortedDictionary<int, string>>();
var unmatched = new List<string>();
for (ushort species = 1; species <= MaxSpecies; species++)
{
    if (!files.Contains($"{species}.png"))
        throw new InvalidOperationException($"No sprite for species {species}.");
    // Gen 9's form numbers, plus forms only older games have (Gen 4's spiky-eared Pichu). Gen 4 numbers Arceus' forms
    // differently (it has a ??? form); rhome's sprite lookup makes up for that.
    var names = new SortedDictionary<int, string>();
    foreach (var context in new[] { EntityContext.Gen9, EntityContext.Gen5, EntityContext.Gen4, EntityContext.Gen3 })
    {
        var list = FormConverter.GetFormList(species, strings.Types, strings.forms, GameInfo.GenderSymbolASCII, context);
        for (var form = 1; form < list.Length; form++)
            names.TryAdd(form, list[form]);
    }
    foreach (var (form, name) in names)
    {
        var stem = Match(species, name);
        if (stem is null)
        {
            unmatched.Add($"{species} {strings.specieslist[species]} form {form} \"{name}\"");
            continue;
        }
        if (!forms.TryGetValue(species, out var bySpecies))
            forms[species] = bySpecies = [];
        bySpecies[form] = stem;
    }
}

var stems = Enumerable.Range(1, MaxSpecies).Select(s => s.ToString(CultureInfo.InvariantCulture))
    .Concat(forms.Values.SelectMany(f => f.Values)).Distinct().ToList();
var table = new JsonObject
{
    ["source"] = "https://github.com/PokeAPI/sprites",
    ["commit"] = spritesCommit,
    ["folder"] = Folder,
    ["formsFrom"] = $"https://github.com/PokeAPI/pokeapi/blob/{pokeapiCommit}/data/v2/csv/pokemon_forms.csv",
    ["maxSpecies"] = MaxSpecies,
    ["egg"] = files.Contains("egg.png") ? "egg" : null,
    ["forms"] = new JsonObject(forms.Select(s => KeyValuePair.Create(s.Key.ToString(CultureInfo.InvariantCulture),
        (JsonNode?)new JsonObject(s.Value.Select(f => KeyValuePair.Create(f.Key.ToString(CultureInfo.InvariantCulture), (JsonNode?)f.Value)))))),
    ["noShiny"] = List(stems.Where(s => !files.Contains($"shiny/{s}.png"))),
    ["female"] = List(stems.Where(s => files.Contains($"female/{s}.png"))),
    ["femaleShiny"] = List(stems.Where(s => files.Contains($"shiny/female/{s}.png"))),
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
await File.WriteAllTextAsync(outPath, table.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

Console.WriteLine($"{forms.Values.Sum(f => f.Count)} forms with a file of their own; written to {outPath}");
Console.WriteLine($"{unmatched.Count} forms without one (they show the plain sprite):");
foreach (var line in unmatched)
    Console.WriteLine("  " + line);
return 0;

// The file for a PKHeX form name: the PokeAPI form whose identifier's words all appear in the name (or the other way
// round: "Blue" for "blue-striped"), the most shared words winning. Its file is "<species>-<form>.png" for a cosmetic
// form, or "<pokemon id>.png" for a form PokeAPI counts as a Pokémon of its own (Megas, regional forms, Deoxys' formes, …).
string? Match(ushort species, string name)
{
    var words = Words(name);
    if (species == (ushort)Species.Genesect)
        words = [.. words.Select(w => GenesectDrives.GetValueOrDefault(w, w))];
    var best = apiForms[species]
        .Select(f => (Form: f, Words: f.Identifier.Split('-')))
        .Where(f => f.Words.All(words.Contains) || words.All(f.Words.Contains))
        .OrderByDescending(f => f.Words.Count(words.Contains))
        .ThenBy(f => f.Words.Length)
        .ThenByDescending(f => f.Form.IsDefault)
        .Select(f => f.Form)
        .FirstOrDefault();
    if (best is null)
        return null;
    var cosmetic = $"{species}-{best.Identifier}";
    if (files.Contains(cosmetic + ".png"))
        return cosmetic;
    var own = best.Pokemon.ToString(CultureInfo.InvariantCulture);
    return best.Pokemon != species && best.IsDefault && files.Contains(own + ".png") ? own : null;
}

static HashSet<string> Words(string name)
{
    var text = name.ToLowerInvariant().Replace("!", " exclamation ").Replace("?", " question ").Replace("é", "e");
    var words = text.Split(text.Where(c => !char.IsAsciiLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    foreach (var (from, to) in new[] { ("alolan", "alola"), ("galarian", "galar"), ("hisuian", "hisui"), ("paldean", "paldea"), ("f", "female"), ("m", "male") })
    {
        if (words.Contains(from))
            words.Add(to);
    }
    return words;
}

static JsonArray List(IEnumerable<string> stems) => new([.. stems.Order(StringComparer.Ordinal).Select(s => (JsonNode?)s)]);

static List<Dictionary<string, string>> Rows(string text)
{
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var header = lines[0].Split(',');
    return [.. lines.Skip(1).Select(l => header.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second))];
}

static partial class Program
{
    // PKHeX names Genesect's forms by the type of Techno Blast; PokeAPI by the drive.
    static readonly Dictionary<string, string> GenesectDrives = new()
    {
        ["water"] = "douse", ["electric"] = "shock", ["fire"] = "burn", ["ice"] = "chill",
    };
}

sealed record ApiForm(int Species, int Pokemon, string Identifier, bool IsDefault);
