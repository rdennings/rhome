using SpikeB.Core;

// save-scan <emulator> <folder> [state.json]
// Runs the same scan the Android app does, on a plain folder (e.g. one pulled off the device with adb).
// emulator: melonds | retroarch | eden

if (args.Length < 2 || EmulatorProfile.All.FirstOrDefault(p => p.Key == args[0]) is not { } profile)
{
    Console.Error.WriteLine($"usage: save-scan <{string.Join('|', EmulatorProfile.All.Select(p => p.Key))}> <folder> [state.json]");
    return 2;
}

var statePath = args.Length > 2 ? args[2] : null;
var state = statePath is null ? new ScanState() : ScanState.Load(statePath);
var result = SaveScanner.Scan([(profile, new LocalSaveFolder(args[1]))], state);
if (statePath is not null)
    state.Save(statePath);

foreach (var s in result.Saves)
    Console.WriteLine($"{Mark(s.Status)} {s.Game,-24} {s.Trainer,-10} {s.PlayTime,7}  {s.Entry.RelativePath}");
Console.WriteLine($"\n{result.Saves.Count} Pokémon saves · {result.CandidateFiles} save files · {result.FilesListed} files listed " +
                  $"· list {result.ListTime.TotalMilliseconds:F0} ms · read {result.FilesRead} in {result.ReadTime.TotalMilliseconds:F0} ms");
foreach (var e in result.Errors)
    Console.WriteLine($"error: {e}");
return 0;

static string Mark(SaveStatus s) => s switch { SaveStatus.New => "NEW", SaveStatus.Changed => "UPD", _ => "   " };

