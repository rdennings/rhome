using System.Security.Cryptography;
using PKHeX.Core;
using Rhome.Cli;
using Rhome.Core;

namespace Rhome.Cli.Tests;

public sealed class ShowSaveTests : IDisposable
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    readonly string _temp = Directory.CreateTempSubdirectory("rhome-cli-tests").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    static (int Code, string Output, string Error) Run(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    static string[] Lines(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Shows_the_game_trainer_party_and_boxes()
    {
        var (code, output, error) = Run("save", "show", Path.Combine(Fixtures, "platinum-us.sav"));

        Assert.Equal(Cli.Ok, code);
        Assert.Empty(error);
        var lines = Lines(output);
        Assert.Equal("platinum-us.sav", lines[0]);
        Assert.StartsWith("Platinum · Gen 4 · trainer TEST ♀ (ID 33731) · ", lines[1]);
        Assert.Contains("Party (6)", lines);
        Assert.Contains("2  Monferno       Lv  34 ♂  \"Tamale\"", lines);
        Assert.Contains("6  Buneary        Lv  30 ♀  holds Soothe Bell", lines);
        Assert.Contains("Box 1 · BOX 1 (5)", lines);
        Assert.Contains("6  Eevee          Lv  20 ♂", lines);
        Assert.Equal("17 of 18 boxes are empty.", lines[^1]);
    }

    [Fact]
    public void Shows_the_original_trainer_of_a_Pokemon_from_another_game()
    {
        var (_, output, _) = Run("save", "show", Path.Combine(Fixtures, "emerald.sav"));

        Assert.Contains("1  Kirlia         Lv  20 ♀  OT TEST (ID 33731)", Lines(output));
    }

    [Theory]
    [InlineData("leafgreen", "FireRed/LeafGreen · Gen 3")]
    [InlineData("sapphire", "Ruby/Sapphire · Gen 3")]
    [InlineData("diamond", "Diamond/Pearl · Gen 4")]
    [InlineData("soulsilver", "SoulSilver · Gen 4")]
    [InlineData("white2", "White 2 · Gen 5")]
    public void Names_every_Tier_1_game(string fixture, string expected)
    {
        var (code, output, _) = Run("save", "show", Path.Combine(Fixtures, fixture + ".sav"));

        Assert.Equal(Cli.Ok, code);
        Assert.StartsWith(expected, Lines(output)[1]);
    }

    [Fact]
    public void Never_changes_the_file()
    {
        var path = Path.Combine(_temp, "white.sav");
        File.Copy(Path.Combine(Fixtures, "white.sav"), path);
        var (hash, written) = (SHA256.HashData(File.ReadAllBytes(path)), File.GetLastWriteTimeUtc(path));

        Run("save", "show", path);

        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Refuses_a_missing_file()
    {
        var (code, output, error) = Run("save", "show", Path.Combine(_temp, "nope.sav"));

        Assert.Equal(Cli.Failed, code);
        Assert.Empty(output);
        Assert.Equal("nope.sav: no such file", error.Trim());
    }

    [Fact]
    public void Refuses_a_file_that_is_not_a_save()
    {
        var path = Path.Combine(_temp, "notes.sav");
        File.WriteAllBytes(path, new byte[0x80000]); // a DS save's size, but all zeros

        var (code, _, error) = Run("save", "show", path);

        Assert.Equal(Cli.Failed, code);
        Assert.Equal("notes.sav: not a Pokémon save rhome recognizes", error.Trim());
    }

    [Fact]
    public void Says_when_a_game_is_not_supported_yet()
    {
        // PKHeX's blank X/Y save isn't recognized from bytes, so this starts after the reading step.
        var save = BlankSaveFile.Get(GameVersion.X, "TEST", LanguageID.English);
        var detected = new DetectedSave(GameVersion.X, 6, "TEST", SaveDetector.IsSupported(save), save);
        var (output, error) = (new StringWriter(), new StringWriter());

        var code = ShowSave.Print("x.sav", detected, output, error);

        Assert.Equal(Cli.Failed, code);
        Assert.Empty(output.ToString());
        Assert.Equal("x.sav: a Gen 6 save (X). rhome only supports the GBA and DS games so far.", error.ToString().Trim());
    }

    [Theory]
    [InlineData]
    [InlineData("save")]
    [InlineData("save", "show")]
    [InlineData("bogus")]
    public void A_bad_command_line_exits_with_2(params string[] args) =>
        Assert.Equal(Cli.Usage, Run(args).Code);
}
