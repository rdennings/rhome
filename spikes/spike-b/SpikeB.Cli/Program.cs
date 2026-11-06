using SpikeB.Core;

// save-scan <emulator> <folder> [<emulator> <folder> ...] [state.json]
// Runs the same scan the Android app does, on plain folders (e.g. ones pulled off the device with adb).
// emulator: melonds | melonds-roms | retroarch | eden

var keys = string.Join('|', EmulatorProfile.All.Select(p => p.Key));
if (args.Length < 2)
    return Usage();

var sources = new List<(EmulatorProfile, ISaveFolder)>();
int i = 0;
for (; i + 1 < args.Length; i += 2)
{
    if (EmulatorProfile.All.FirstOrDefault(p => p.Key == args[i]) is not { } profile)
        return Usage();
    sources.Add((profile, new LocalSaveFolder(args[i + 1])));
}

var statePath = i < args.Length ? args[i] : null;
var state = statePath is null ? new ScanState() : ScanState.Load(statePath);
var result = SaveScanner.Scan(sources, state);
if (statePath is not null)
    state.Save(statePath);

foreach (var s in result.Saves)
{
    Console.WriteLine($"{Mark(s.Status)} {s.Emulator.Key,-12} {s.Game,-24} {s.Trainer,-10} {s.PlayTime,7}  {s.Entry.RelativePath}");
    if (s.Note is not null)
        Console.WriteLine($"    note: {s.Note}");
}
Console.WriteLine($"\n{result.Saves.Count} Pokémon saves · {result.CandidateFiles} save files · {result.FilesListed} files listed " +
                  $"· list {result.ListTime.TotalMilliseconds:F0} ms · read {result.FilesRead} in {result.ReadTime.TotalMilliseconds:F0} ms");
foreach (var e in result.Errors)
    Console.WriteLine($"error: {e}");
return 0;

int Usage()
{
    Console.Error.WriteLine($"usage: save-scan <{keys}> <folder> [<emulator> <folder> ...] [state.json]");
    return 2;
}

static string Mark(SaveStatus s) => s switch { SaveStatus.New => "NEW", SaveStatus.Changed => "UPD", _ => "   " };
