using System.Globalization;
using PKHeX.Core;
using Rhome.Core;
using Rhome.FileSystem;

namespace Rhome.Cli;

/// <summary>Where the CLI keeps the bank and its backups (both options on every command).</summary>
sealed record BankPlace(FileInfo Bank, DirectoryInfo Backups, int Keep)
{
    public static FileInfo DefaultBank => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "rhome", "bank.json"));

    public JsonBankStore Store => new(new FileOnDisk(Bank.FullName));
}

/// <summary>
/// The commands that change things: import, withdraw, and moves within the bank. Each one is staged in a
/// changeset (#21), its summary printed, and then applied (or not, with --dry-run). Saves are always backed
/// up first; the changeset runner sees to that.
/// </summary>
static class BankCommands
{
    public static int List(BankPlace place, TextWriter output, TextWriter error)
    {
        var bank = Load(place, error);
        if (bank is null)
            return Cli.Failed;

        output.WriteLine($"Bank: {place.Bank.FullName}");
        if (bank.Pokemon.Count == 0)
        {
            output.WriteLine("The bank is empty.");
            return Cli.Ok;
        }
        foreach (var box in bank.Boxes.Boxes)
        {
            var inBox = bank.Pokemon.Where(p => p.Slot.Box == box.Id).OrderBy(p => p.Slot.Slot).ToList();
            if (inBox.Count == 0)
                continue;
            output.WriteLine();
            output.WriteLine($"Box {box.Id} · {box.Name} ({inBox.Count})");
            foreach (var pokemon in inBox)
                output.WriteLine(Line(bank, pokemon));
        }
        var outCount = bank.Pokemon.Count(p => !p.IsInBank);
        output.WriteLine();
        output.WriteLine($"{bank.Pokemon.Count} Pokémon, {outCount} of them out in a game.");
        return Cli.Ok;
    }

    public static int Import(BankPlace place, FileInfo file, string slotText, string? into, bool asNew, string? returning,
        bool dryRun, TextWriter output, TextWriter error)
    {
        if (!Slots.TryParseSave(slotText, out var slot))
            return Usage(error, $"'{slotText}' isn't a save slot. Use party:N or box:B:S (for example box:1:3).");
        BankSlot? target = null;
        if (into is not null)
        {
            if (!Slots.TryParseBank(into, out var parsed))
                return Usage(error, $"'{into}' isn't a bank slot. Use B:S (for example 2:5).");
            target = parsed;
        }

        var session = Session.Open(place, file, output, error);
        if (session is null)
            return Cli.Failed;

        var how = ImportAs.Auto;
        if (asNew)
            how = ImportAs.New;
        else if (returning is not null)
        {
            if (session.Find(returning, error) is not { } record)
                return Cli.Failed;
            how = ImportAs.Return(record.Id);
        }

        session.Changeset.Add(new ImportChange(session.SaveId, slot, target, how));
        return session.Run(dryRun);
    }

    public static int Withdraw(BankPlace place, string pokemonText, FileInfo file, string? slotText, string moves,
        bool dryRun, TextWriter output, TextWriter error)
    {
        SaveSlot? slot = null;
        if (slotText is not null)
        {
            if (!Slots.TryParseSave(slotText, out var parsed))
                return Usage(error, $"'{slotText}' isn't a save slot. Use party:N or box:B:S (for example box:1:3).");
            slot = parsed;
        }
        var choice = moves switch
        {
            "newest" => MovesetChoice.Newest,
            "last" => MovesetChoice.LastInThisGame,
            _ => (MovesetChoice?)null,
        };
        if (choice is null)
            return Usage(error, $"--moves is 'newest' or 'last', not '{moves}'.");

        var session = Session.Open(place, file, output, error);
        if (session is null)
            return Cli.Failed;
        if (session.Find(pokemonText, error) is not { } record)
            return Cli.Failed;

        if (record.IsInBank)
        {
            var options = Transfers.Movesets(session.Bank, record, session.Save, DateTimeOffset.Now);
            if (options.LastInThisGame is { } last)
                output.WriteLine($"Moves: newest {Names.Moves(options.Newest)}; last time in this game {Names.Moves(last)}. " +
                                 $"Taking the {(choice == MovesetChoice.Newest ? "newest (--moves last for the other)" : "last ones")}.");
        }

        session.Changeset.Add(new WithdrawChange(record.Id, session.SaveId, slot, choice.Value));
        return session.Run(dryRun);
    }

    public static int Move(BankPlace place, string pokemonText, string slotText, bool dryRun, TextWriter output, TextWriter error)
    {
        if (!Slots.TryParseBank(slotText, out var slot))
            return Usage(error, $"'{slotText}' isn't a bank slot. Use B:S (for example 2:5).");
        var session = Session.Open(place, null, output, error);
        if (session is null)
            return Cli.Failed;
        if (session.Find(pokemonText, error) is not { } record)
            return Cli.Failed;

        session.Changeset.Add(new MoveInBankChange(record.Id, slot));
        return session.Run(dryRun);
    }

    static string Line(Bank bank, BankPokemon pokemon)
    {
        var s = pokemon.Summary;
        var parts = new List<string>
        {
            $"{Names.Species(s.Species, s.Form),-14} Lv {s.Level,3} {Names.Gender(s.Gender),1}{(s.IsShiny ? " ★" : "")}",
        };
        if (s.IsNicknamed)
            parts.Add($"\"{s.Nickname}\"");
        if (pokemon.CheckedOut is { } away)
            parts.Add($"out in {bank.Saves.Find(x => x.Id == away.SaveId)?.DisplayName ?? "a save"} ({away.Slot})");
        parts.Add($"id {Short(pokemon.Id)}");
        return $"  {pokemon.Slot.Slot,2}  {string.Join("  ", parts)}";
    }

    public static string Short(Guid id) => id.ToString("N")[..8];

    static Bank? Load(BankPlace place, TextWriter error)
    {
        try
        {
            return place.Store.Load();
        }
        catch (BankFormatException e)
        {
            error.WriteLine($"{place.Bank.FullName}: {e.Message}");
            return null;
        }
    }

    static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        return Cli.Usage;
    }

    /// <summary>
    /// One command's work: the bank, the save it touches (if any), and a changeset to run. A save the bank hasn't
    /// seen is added to it as part of the same apply, so a dry run or a refused change leaves the bank file alone.
    /// </summary>
    sealed class Session
    {
        public required BankPlace Place { get; init; }
        public required Bank Bank { get; init; }
        public required IBankStore Store { get; init; }
        public required Changeset Changeset { get; init; }
        public SaveOnDisk? File { get; init; }
        public SaveFile Save { get; init; } = null!;
        public Guid SaveId { get; init; }
        public required TextWriter Output { get; init; }
        public required TextWriter Error { get; init; }

        public static Session? Open(BankPlace place, FileInfo? file, TextWriter output, TextWriter error)
        {
            var bank = Load(place, error);
            if (bank is null)
                return null;
            var store = new WithSaves(place.Store, []);
            var changeset = new Changeset();
            if (file is null)
                return new Session { Place = place, Bank = bank, Store = store, Changeset = changeset, Output = output, Error = error };

            var detected = SaveFiles.Read(file, out var problem);
            if (detected is null)
            {
                error.WriteLine($"{file.Name}: {problem}");
                return null;
            }
            if (!detected.IsSupported)
            {
                error.WriteLine($"{file.Name}: rhome only supports the GBA and DS games so far.");
                return null;
            }

            var trainer = new TrainerInfo(detected.Trainer, detected.Save.TID16, detected.Save.SID16);
            var known = bank.Saves.Where(s => s.Game == detected.Game && s.Trainer == trainer).ToList();
            if (known.Count > 1)
            {
                error.WriteLine($"{file.Name}: the bank knows {known.Count} {Names.Game(detected.Game)} saves of trainer {trainer.Name}; rhome can't tell which this is.");
                return null;
            }
            var record = known.FirstOrDefault()
                         ?? new BankSave(Guid.NewGuid(), detected.Game, trainer, $"{Names.Game(detected.Game)} ({file.Name})", null);
            if (known.Count == 0)
            {
                store = new WithSaves(place.Store, [record]);
                bank.Saves.Add(record);
            }

            var location = new SaveOnDisk(file.FullName);
            changeset.Opened(record.Id, location.Read());
            return new Session
            {
                Place = place, Bank = bank, Store = store, Changeset = changeset, File = location, Save = detected.Save,
                SaveId = record.Id, Output = output, Error = error,
            };
        }

        /// <summary>A bank Pokémon by its bank slot (B:S) or the start of its ID.</summary>
        public BankPokemon? Find(string text, TextWriter error)
        {
            if (Slots.TryParseBank(text, out var slot))
            {
                if (Bank.At(slot) is { } here)
                    return here;
                error.WriteLine($"There's no Pokémon in {slot}.");
                return null;
            }
            var matches = Bank.Pokemon.Where(p => p.Id.ToString("N").StartsWith(text.ToLowerInvariant(), StringComparison.Ordinal)).ToList();
            if (text.Length >= 4 && matches.Count == 1)
                return matches[0];
            error.WriteLine(matches.Count > 1
                ? $"More than one Pokémon's ID starts with '{text}'; give more of it."
                : $"No bank Pokémon is at '{text}' or has an ID starting with it. 'rhome bank list' shows them.");
            return null;
        }

        public int Run(bool dryRun)
        {
            var runner = new ChangesetRunner(Store, id => id == SaveId ? File : null,
                new BackupFolder(Place.Backups.FullName, Place.Keep));
            var now = DateTimeOffset.Now;
            var result = dryRun ? runner.Preview(Changeset, now) : runner.Apply(Changeset, now);

            foreach (var line in result.Lines)
                Output.WriteLine(line);
            if (!result.Succeeded)
            {
                Error.WriteLine(result.Refusal);
                foreach (var candidate in result.Candidates)
                    Error.WriteLine($"  maybe: {Line(Bank, candidate)}");
                if (result.Candidates.Count > 0)
                    Error.WriteLine("Run it again with --returning <id> if it's one of these, or --new if it isn't.");
                return Cli.Failed;
            }
            Output.WriteLine(dryRun
                ? "Dry run: nothing was written."
                : $"Done. The save and the bank were backed up to {Place.Backups.FullName} first.");
            return Cli.Ok;
        }
    }

    /// <summary>The stored bank, plus saves met for the first time in this command, added when it's loaded.</summary>
    sealed class WithSaves(IBankStore inner, IReadOnlyList<BankSave> extra) : IBankStore
    {
        public Bank Load()
        {
            var bank = inner.Load();
            foreach (var save in extra)
                if (bank.Saves.All(s => s.Id != save.Id))
                    bank.Saves.Add(save);
            return bank;
        }

        public void Save(Bank bank) => inner.Save(bank);
    }
}

/// <summary>Parsing slots as the CLI writes them.</summary>
static class Slots
{
    /// <summary><c>party:N</c>, <c>box:B:S</c>, or just <c>B:S</c> for a box slot.</summary>
    public static bool TryParseSave(string text, out SaveSlot slot)
    {
        slot = default;
        var parts = text.ToLowerInvariant().Split(':');
        switch (parts)
        {
            case ["party", var n] when Number(n) is { } i:
                slot = new SaveSlot(null, i);
                return true;
            case ["box", var b, var s] when Number(b) is { } box && Number(s) is { } i:
                slot = new SaveSlot(box, i);
                return true;
            case [var b, var s] when Number(b) is { } box && Number(s) is { } i:
                slot = new SaveSlot(box, i);
                return true;
            default:
                return false;
        }
    }

    /// <summary><c>B:S</c>: a bank box ID and slot.</summary>
    public static bool TryParseBank(string text, out BankSlot slot)
    {
        slot = default;
        var parts = text.Split(':');
        if (parts is not [var b, var s] || Number(b) is not { } box || Number(s) is not { } i)
            return false;
        slot = new BankSlot(box, i);
        return true;
    }

    static int? Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
}
