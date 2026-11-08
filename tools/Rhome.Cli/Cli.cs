using System.CommandLine;

namespace Rhome.Cli;

/// <summary>
/// The commands. Each one writes to the writers it's given, so tests can run it in-process.
/// Exit codes: 0 done, 1 the command couldn't do its job (no such file, not a save, a refused change …),
/// 2 bad command line.
/// </summary>
static class Cli
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        // Where the bank lives, on every command.
        var bankOption = new Option<FileInfo>("--bank")
        {
            Description = "The bank file (default: rhome/bank.json in your local app data folder)",
            Recursive = true,
            DefaultValueFactory = _ => BankPlace.DefaultBank,
        };
        var backupsOption = new Option<DirectoryInfo?>("--backups")
        {
            Description = "Where saves and the bank are backed up before every change (default: backups/ next to the bank)",
            Recursive = true,
        };
        var keepOption = new Option<int>("--keep")
        {
            Description = "How many backups to keep per save, and of the bank",
            Recursive = true,
            DefaultValueFactory = _ => 10,
        };
        var dryRun = new Option<bool>("--dry-run") { Description = "Show what would happen, and write nothing", Recursive = true };

        BankPlace Place(ParseResult parsed)
        {
            var bank = parsed.GetValue(bankOption)!;
            var backups = parsed.GetValue(backupsOption) ?? new DirectoryInfo(Path.Combine(bank.DirectoryName!, "backups"));
            return new BankPlace(bank, backups, parsed.GetValue(keepOption));
        }

        // rhome save show <file>
        var file = new Argument<FileInfo>("file") { Description = "The save file (.sav, .srm, .dsv …)" };
        var show = new Command("show", "List a save's game, trainer, party and boxes. Reads only; the file is never changed.") { file };
        show.SetAction(parsed => ShowSave.Run(parsed.GetValue(file)!, output, error));
        var save = new Command("save", "Look at an emulator save file") { show };

        // rhome bank list | move
        var list = new Command("list", "List the Pokémon in the bank, and where the ones out in a game are");
        list.SetAction(parsed => BankCommands.List(Place(parsed), output, error));
        var movePokemon = new Argument<string>("pokemon") { Description = "The Pokémon: its bank slot (B:S) or the start of its ID" };
        var moveTo = new Argument<string>("to") { Description = "The bank slot to move it to (B:S); whatever is there swaps places" };
        var move = new Command("move", "Move a Pokémon to another bank slot") { movePokemon, moveTo };
        move.SetAction(parsed => BankCommands.Move(Place(parsed), parsed.GetValue(movePokemon)!, parsed.GetValue(moveTo)!,
            parsed.GetValue(dryRun), output, error));
        var bankCommand = new Command("bank", "Look at and arrange the bank") { list, move };

        // rhome import <file> <slot>
        var importFile = new Argument<FileInfo>("file") { Description = "The save file to take the Pokémon from" };
        var importSlot = new Argument<string>("slot") { Description = "party:N or box:B:S (as 'rhome save show' numbers them)" };
        var into = new Option<string?>("--to") { Description = "The bank slot to put a new Pokémon in (B:S); default: the first free one" };
        var asNew = new Option<bool>("--new") { Description = "Import it as a new bank Pokémon, even if it looks like one that's out" };
        var returning = new Option<string?>("--returning") { Description = "It's this bank Pokémon coming back (its slot or ID), when rhome asks" };
        var import = new Command("import", "Move a Pokémon from a save into the bank") { importFile, importSlot, into, asNew, returning };
        import.SetAction(parsed => BankCommands.Import(Place(parsed), parsed.GetValue(importFile)!, parsed.GetValue(importSlot)!,
            parsed.GetValue(into), parsed.GetValue(asNew), parsed.GetValue(returning), parsed.GetValue(dryRun), output, error));

        // rhome withdraw <pokemon> <file> [slot]
        var withdrawPokemon = new Argument<string>("pokemon") { Description = "The Pokémon: its bank slot (B:S) or the start of its ID" };
        var withdrawFile = new Argument<FileInfo>("file") { Description = "The save file to put it in" };
        var withdrawSlot = new Argument<string?>("slot") { Description = "party:N or box:B:S; default: the first empty box slot", Arity = ArgumentArity.ZeroOrOne };
        var moves = new Option<string>("--moves")
        {
            Description = "'newest' (default): the moves it knows now; 'last': the ones it had last time in this game",
            DefaultValueFactory = _ => "newest",
        };
        var withdraw = new Command("withdraw", "Move a Pokémon from the bank into a save") { withdrawPokemon, withdrawFile, withdrawSlot, moves };
        withdraw.SetAction(parsed => BankCommands.Withdraw(Place(parsed), parsed.GetValue(withdrawPokemon)!, parsed.GetValue(withdrawFile)!,
            parsed.GetValue(withdrawSlot), parsed.GetValue(moves)!, parsed.GetValue(dryRun), output, error));

        var root = new RootCommand("rhome: a bank for the Pokémon in your emulator saves")
        {
            bankOption, backupsOption, keepOption, dryRun, save, bankCommand, import, withdraw,
        };

        // System.CommandLine prints the problem and the help either way; only the exit code is ours.
        var parsedArgs = root.Parse(args);
        var code = parsedArgs.Invoke(new InvocationConfiguration { Output = output, Error = error });
        return parsedArgs.Errors.Count > 0 ? Usage : code;
    }
}
