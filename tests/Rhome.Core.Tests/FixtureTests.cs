using System.Text.Json;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>
/// Reads every save in fixtures/ and checks it against the manifest next to it (fixtures/README.md).
/// The manifests were cross-checked against the Python readers in prototypes/ where one exists.
/// </summary>
public class FixtureTests
{
    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "fixtures");

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Folder, "*.json").Order())
            data.Add(Path.GetFileNameWithoutExtension(path));
        return data;
    }

    static Manifest LoadManifest(string name) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Folder, name + ".json")), Json)!;

    static byte[] LoadSave(Manifest manifest) => File.ReadAllBytes(Path.Combine(Folder, manifest.File));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_game_and_trainer_are_recognized(string name)
    {
        var manifest = LoadManifest(name);

        var detected = SaveDetector.Detect(LoadSave(manifest));

        Assert.NotNull(detected);
        Assert.Equal(Enum.Parse<GameVersion>(manifest.Game), detected.Game);
        Assert.Equal(manifest.Generation, detected.Generation);
        Assert.True(detected.IsSupported);
        Assert.Equal(manifest.Trainer.Name, detected.Trainer);
        Assert.Equal(manifest.Trainer.Tid, detected.Save.TID16);
        Assert.Equal(manifest.Trainer.Sid, detected.Save.SID16);
        Assert.Equal(manifest.Trainer.Gender, detected.Save.Gender);
        Assert.True(detected.Save.ChecksumsValid, detected.Save.ChecksumInfo);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_party_matches_the_manifest(string name)
    {
        var manifest = LoadManifest(name);
        var save = SaveDetector.Detect(LoadSave(manifest))!.Save;

        var party = save.PartyData.Select((pk, i) => Describe(save, pk, null, i + 1));

        Assert.Equal(manifest.Party, party);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_boxes_match_the_manifest(string name)
    {
        var manifest = LoadManifest(name);
        var save = SaveDetector.Detect(LoadSave(manifest))!.Save;

        var boxes = save.BoxData
            .Select((pk, i) => (pk, i))
            .Where(t => t.pk.Species != 0)
            .Select(t => Describe(save, t.pk, t.i / save.BoxSlotCount + 1, t.i % save.BoxSlotCount + 1));

        Assert.Equal(manifest.Boxes, boxes);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Writing_without_changes_gives_the_same_bytes(string name)
    {
        // PLAN.md §6, test 2: catches checksum and layout mistakes on the write path.
        var original = LoadSave(LoadManifest(name));
        var save = SaveDetector.Detect(original)!.Save;

        var written = save.Write(BinaryExportSetting.None).ToArray();

        Assert.Equal(original, written);
    }

    [Fact]
    public void The_Kirlia_moved_to_Emerald_keeps_its_Platinum_trainer()
    {
        // It was caught in Platinum and moved by prototypes/transfer_pt_to_em.py, so matching a returning
        // Pokémon to its original (PLAN.md §5) can be tested on real data.
        var platinum = LoadManifest("platinum-us").Trainer;
        var kirlia = Assert.Single(LoadManifest("emerald").Boxes, s => s.Species == "Kirlia");

        Assert.Equal(new Ot(platinum.Name, platinum.Tid, platinum.Sid), kirlia.Ot);
    }

    static Pokemon Describe(SaveFile save, PKM pk, int? box, int slot)
    {
        var sameOt = pk.OriginalTrainerName == save.OT && pk.ID32 == save.ID32;
        return new Pokemon(
            box,
            slot,
            ((Species)pk.Species).ToString(),
            pk.CurrentLevel,
            pk.IsNicknamed ? pk.Nickname : null,
            pk.IsEgg ? true : null,
            pk.IsShiny ? true : null,
            sameOt ? null : new Ot(pk.OriginalTrainerName, pk.TID16, pk.SID16));
    }

    // Mirrors the JSON written by tools/Rhome.FixtureScrubber. Kept separate on purpose: the tests
    // shouldn't trust the tool's own types to say what a fixture holds.
    sealed record Manifest(string File, string Game, int Generation, Trainer Trainer, Pokemon[] Party, Pokemon[] Boxes);

    sealed record Trainer(string Name, ushort Tid, ushort Sid, byte Gender);

    sealed record Pokemon(int? Box, int Slot, string Species, byte Level, string? Nickname, bool? Egg, bool? Shiny, Ot? Ot);

    sealed record Ot(string Name, ushort Tid, ushort Sid);
}
