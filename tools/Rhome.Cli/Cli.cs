using System.CommandLine;

namespace Rhome.Cli;

/// <summary>
/// The commands. Each one writes to the writers it's given, so tests can run it in-process.
/// Exit codes: 0 done, 1 the command couldn't do its job (no such file, not a save …), 2 bad command line.
/// </summary>
static class Cli
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var file = new Argument<FileInfo>("file") { Description = "The save file (.sav, .srm, .dsv …)" };
        var show = new Command("show", "List a save's game, trainer, party and boxes. Reads only; the file is never changed.") { file };
        show.SetAction(parsed => ShowSave.Run(parsed.GetValue(file)!, output, error));

        var save = new Command("save", "Look at an emulator save file") { show };
        var root = new RootCommand("rhome: a bank for the Pokémon in your emulator saves") { save };

        // System.CommandLine prints the problem and the help either way; only the exit code is ours.
        var parsed = root.Parse(args);
        var code = parsed.Invoke(new InvocationConfiguration { Output = output, Error = error });
        return parsed.Errors.Count > 0 ? Usage : code;
    }
}
