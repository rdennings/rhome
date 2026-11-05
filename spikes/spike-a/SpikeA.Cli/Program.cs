// save-report: print a save's trainer, party and boxes via PKHeX.Core.
// Usage: save-report <save file>...
using SpikeA.Report;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: save-report <save file>...");
    return 2;
}

int failures = 0;
foreach (var path in args)
{
    var result = SaveReport.Build(File.ReadAllBytes(path), path);
    Console.Write(result.Text);
    Console.Error.WriteLine($"[{Path.GetFileName(path)}] {result.Game}, loaded in {result.LoadTime.TotalMilliseconds:F1} ms");
    if (!result.Ok)
        failures++;
}
return failures == 0 ? 0 : 1;
