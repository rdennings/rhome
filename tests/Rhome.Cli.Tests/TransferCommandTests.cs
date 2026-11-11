using PKHeX.Core;
using Rhome.Cli;
using Rhome.Core;

namespace Rhome.Cli.Tests;

/// <summary>Headless transfers from the command line (issue #23), on copies of the fixtures.</summary>
public sealed class TransferCommandTests : IDisposable
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    readonly string _temp = Directory.CreateTempSubdirectory("rhome-cli-tests").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    string Bank => Path.Combine(_temp, "bank", "bank.json");
    string Backups => Path.Combine(_temp, "backups");

    string Copy(string fixture, string name)
    {
        var path = Path.Combine(_temp, name);
        File.Copy(Path.Combine(Fixtures, fixture + ".sav"), path);
        return path;
    }

    (int Code, string Output, string Error) Run(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var code = Cli.Run(["--bank", Bank, "--backups", Backups, .. args], output, error);
        return (code, output.ToString(), error.ToString());
    }

    (int Code, string Output, string Error) Ok(params string[] args)
    {
        var result = Run(args);
        Assert.True(result.Code == Cli.Ok, $"rhome {string.Join(' ', args)} → {result.Code}\n{result.Output}\n{result.Error}");
        return result;
    }

    static SaveFile Read(string path) => SaveDetector.Detect(File.ReadAllBytes(path))!.Save;

    static PKM At(string path, int box, int slot) => Read(path).GetBoxSlotAtIndex((box - 1) * 30 + slot - 1);

    [Fact]
    public void M1_exit_Platinum_to_the_bank_to_Emerald_and_back_to_Platinum()
    {
        var platinum = Copy("platinum-us", "Platinum.sav");
        var emerald = Copy("emerald", "Emerald.srm");
        var kirlia = Read(platinum).GetPartySlotAtIndex(0);

        Ok("import", platinum, "party:1");
        var withdrawn = Ok("withdraw", "1:1", emerald);
        Assert.Contains("Withdraw Kirlia into Emerald (Emerald.srm), box 1 slot 2", withdrawn.Output);
        Assert.Equal((ushort)Species.Kirlia, At(emerald, 1, 2).Species);

        var back = Ok("import", emerald, "box:1:2");
        Assert.Contains("Return Kirlia from Emerald (Emerald.srm), box 1 slot 2, to Box 1 slot 1", back.Output);
        Ok("withdraw", "1:1", platinum, "box:1:5", "--moves", "last");

        var home = At(platinum, 1, 5);
        Assert.Equal(kirlia.PID, home.PID);
        Assert.Equal(Rhome.Core.Moveset.Of(kirlia), Rhome.Core.Moveset.Of(home));
        Assert.Equal(kirlia.MetLocation, home.MetLocation);
        Assert.Equal(kirlia.EXP, home.EXP);
        Assert.Equal(0, At(emerald, 1, 2).Species);
        Assert.Contains("1 Pokémon, 1 of them out in a game.", Ok("bank", "list").Output);
    }

    [Fact]
    public void M1_exit_White_to_the_bank_to_Platinum_and_back_to_White()
    {
        // White stands in for Black (#22). Its Pokémon are all Unova species, which can't exist in Platinum,
        // so the CLI must refuse them; the core tests cover a Gen 5 Pokémon that can make the trip.
        var white = Copy("white", "White.sav");
        var platinum = Copy("platinum-us", "Platinum.sav");

        Ok("import", white, "box:1:1");
        var refused = Run("withdraw", "1:1", platinum);

        Assert.Equal(Cli.Failed, refused.Code);
        Assert.Contains("Patrat doesn't exist in the Gen 4 games.", refused.Error);
        Ok("withdraw", "1:1", white, "box:1:1");
        Assert.Equal(File.ReadAllBytes(Path.Combine(Fixtures, "white.sav")), File.ReadAllBytes(white));
    }

    [Fact]
    public void A_dry_run_shows_the_summary_and_writes_nothing()
    {
        var platinum = Copy("platinum-us", "Platinum.sav");
        var before = File.ReadAllBytes(platinum);

        var (code, output, _) = Run("import", platinum, "box:1:3", "--dry-run");

        Assert.Equal(Cli.Ok, code);
        Assert.Contains("Import Gastly from Platinum (Platinum.sav), box 1 slot 3, to Box 1 slot 1", output);
        Assert.Contains("Dry run: nothing was written.", output);
        Assert.Equal(before, File.ReadAllBytes(platinum));
        Assert.False(File.Exists(Bank));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Every_change_backs_up_the_save_and_the_bank_first()
    {
        var platinum = Copy("platinum-us", "Platinum.sav");
        var original = File.ReadAllBytes(platinum);

        Ok("import", platinum, "box:1:3");

        var saveBackup = Assert.Single(Directory.GetFiles(Path.Combine(Backups, "saves"), "*", SearchOption.AllDirectories));
        Assert.Equal(original, File.ReadAllBytes(saveBackup));
        Assert.Single(Directory.GetFiles(Path.Combine(Backups, "bank")));
    }

    [Fact]
    public void Lists_the_bank_and_moves_within_it()
    {
        var platinum = Copy("platinum-us", "Platinum.sav");
        Ok("import", platinum, "box:1:3");
        Ok("import", platinum, "box:1:6");

        Ok("bank", "move", "1:1", "2:4");
        var list = Ok("bank", "list").Output;

        Assert.Contains("Box 1 · Box 1 (1)", list);
        Assert.Contains("Box 2 · Box 2 (1)", list);
        Assert.Matches(@"\n\s+4  Gastly\s+Lv  13 ♂  id [0-9a-f]{8}", list);
        Assert.Matches(@"\n\s+2  Eevee\s+Lv  20 ♂  id [0-9a-f]{8}", list);
    }

    [Fact]
    public void A_Pokemon_the_bank_might_already_have_is_asked_about()
    {
        // The save is rolled back after an import (an older copy put back), so the Kirlia is in it again.
        var platinum = Copy("platinum-us", "Platinum.sav");
        Ok("import", platinum, "party:1");
        File.Copy(Path.Combine(Fixtures, "platinum-us.sav"), platinum, overwrite: true);

        var asked = Run("import", platinum, "party:1");

        Assert.Equal(Cli.Failed, asked.Code);
        Assert.Contains("might be a Pokémon that's already in the bank", asked.Error);
        Assert.Matches(@"maybe:\s+1  Kirlia\s+Lv  20 ♀  id [0-9a-f]{8}", asked.Error);
        Assert.Contains("--returning <id>", asked.Error);

        var refused = Run("import", platinum, "party:1", "--returning", "1:1");
        Assert.Contains("in the bank already", refused.Error);
        Ok("import", platinum, "party:1", "--new");
        Assert.Contains("2 Pokémon, 0 of them out in a game.", Ok("bank", "list").Output);
    }

    [Fact]
    public void Bad_slots_are_usage_errors()
    {
        var platinum = Copy("platinum-us", "Platinum.sav");

        Assert.Equal(Cli.Usage, Run("import", platinum, "party").Code);
        Assert.Equal(Cli.Usage, Run("import", platinum, "box:0:1").Code);
        Assert.Equal(Cli.Usage, Run("import", platinum, "1:1", "--to", "x").Code);
        Assert.Equal(Cli.Usage, Run("withdraw", "1:1", platinum, "--moves", "oldest").Code);
    }

    [Fact]
    public void Refusals_say_why_and_change_nothing()
    {
        var emerald = Copy("emerald", "Emerald.srm");
        var before = File.ReadAllBytes(emerald);

        var (code, _, error) = Run("import", emerald, "party:1");

        Assert.Equal(Cli.Failed, code);
        Assert.Contains("last Pokémon in the party", error);
        Assert.Equal(before, File.ReadAllBytes(emerald));
    }

    [Fact]
    public void An_unknown_Pokemon_is_reported()
    {
        var emerald = Copy("emerald", "Emerald.srm");

        var (code, _, error) = Run("withdraw", "3:3", emerald);

        Assert.Equal(Cli.Failed, code);
        Assert.Contains("There's no Pokémon in bank box 3 slot 3.", error);
    }
}
