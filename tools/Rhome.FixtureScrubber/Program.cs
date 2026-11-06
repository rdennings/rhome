using PKHeX.Core;
using Rhome.FixtureScrubber;

// Usage: dotnet run --project tools/Rhome.FixtureScrubber -- <out-dir> <name>=<save> [<name>=<save> ...]
// Scrub every save of one player in a single run, so their trainer IDs map the same way in all of them.
if (args.Length < 2 || args.Skip(1).Any(a => !a.Contains('=')))
{
    Console.Error.WriteLine("usage: Rhome.FixtureScrubber <out-dir> <name>=<save> [<name>=<save> ...]");
    return 2;
}

var outDir = args[0];
Directory.CreateDirectory(outDir);
var scrubber = new Scrubber();
var failed = false;

foreach (var arg in args.Skip(1))
{
    var split = arg.IndexOf('=');
    var name = arg[..split];
    var path = arg[(split + 1)..];

    Console.WriteLine($"== {name}");
    var result = scrubber.Scrub(File.ReadAllBytes(path));
    foreach (var line in result.Report)
        Console.WriteLine($"   {line}");

    if (!result.Ok)
    {
        foreach (var problem in result.Problems)
            Console.WriteLine($"   PROBLEM: {problem}");
        Console.WriteLine("   not written");
        failed = true;
        continue;
    }

    var file = name + ".sav";
    File.WriteAllBytes(Path.Combine(outDir, file), result.Data);
    SaveUtil.TryGetSaveFile(result.Data.ToArray(), out var sav);
    File.WriteAllText(Path.Combine(outDir, name + ".json"), FixtureManifest.Create(file, sav!).ToJson());
    Console.WriteLine($"   wrote {file} and {name}.json");
}

return failed ? 1 : 0;
