using System.Text.Json;
using System.Text.Json.Serialization;
using PKHeX.Core;

namespace Rhome.FixtureScrubber;

/// <summary>What a fixture holds, written next to it so tests can assert on it (fixtures/README.md).</summary>
public sealed record FixtureManifest(
    string File,
    string Game,
    int Generation,
    FixtureTrainer Trainer,
    IReadOnlyList<FixtureSlot> Party,
    IReadOnlyList<FixtureSlot> Boxes)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static FixtureManifest Create(string file, SaveFile sav)
    {
        var party = sav.PartyData.Select((pk, i) => Slot(sav, pk, null, i + 1)).ToList();
        var boxes = sav.BoxData
            .Select((pk, i) => (pk, i))
            .Where(t => t.pk.Species != 0)
            .Select(t => Slot(sav, t.pk, t.i / sav.BoxSlotCount + 1, t.i % sav.BoxSlotCount + 1))
            .ToList();

        return new FixtureManifest(
            file,
            sav is SAV3FRLG ? "FRLG" : sav.Version.ToString(), // PKHeX guesses FireRed; the save can't tell.
            sav.Generation,
            new FixtureTrainer(sav.OT, sav.TID16, sav.SID16, sav.Gender),
            party,
            boxes);
    }

    static FixtureSlot Slot(SaveFile sav, PKM pk, int? box, int slot)
    {
        // The OT is only listed when it isn't this save's trainer (traded, or moved from another game).
        var sameOt = pk.OriginalTrainerName == sav.OT && pk.ID32 == sav.ID32;
        return new FixtureSlot(
            box,
            slot,
            ((Species)pk.Species).ToString(),
            pk.CurrentLevel,
            pk.IsNicknamed ? pk.Nickname : null,
            pk.IsEgg ? true : null,
            pk.IsShiny ? true : null,
            sameOt ? null : new FixtureOt(pk.OriginalTrainerName, pk.TID16, pk.SID16));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";
}

public sealed record FixtureTrainer(string Name, ushort Tid, ushort Sid, byte Gender);

/// <summary>A Pokémon in the party (no box) or a box. Box and slot count from 1, as the game shows them.</summary>
public sealed record FixtureSlot(
    int? Box, int Slot, string Species, byte Level, string? Nickname, bool? Egg, bool? Shiny, FixtureOt? Ot);

public sealed record FixtureOt(string Name, ushort Tid, ushort Sid);
