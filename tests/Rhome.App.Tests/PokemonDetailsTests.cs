using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Tests;

/// <summary>What the Pokémon detail view shows (issue #47), for game slots and bank Pokémon, on the test saves.</summary>
public sealed class PokemonDetailsTests
{
    static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    readonly Bank bank = new();
    readonly SaveFile platinum = Load("platinum-us");
    readonly SaveFile emerald = Load("emerald");
    readonly Guid platinumId;
    readonly Guid emeraldId;

    public PokemonDetailsTests()
    {
        platinumId = Know(platinum, "Platinum (TEST)");
        emeraldId = Know(emerald, "Emerald (TEST)");
    }

    static SaveFile Load(string name) =>
        SaveDetector.Detect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav")))!.Save;

    Guid Know(SaveFile save, string name)
    {
        var record = new BankSave(Guid.NewGuid(), SaveDetector.GameOf(save), new TrainerInfo(save.OT, save.TID16, save.SID16), name, null);
        bank.Saves.Add(record);
        return record.Id;
    }

    string Name(Guid id) => bank.Saves.Single(s => s.Id == id).DisplayName;

    PokemonDetails InSave(SaveFile save, Guid id, SaveSlot slot) =>
        PokemonDetails.ForSave(bank, save, id, slot, Name(id), Name)!;

    static string Fact(IEnumerable<Fact> facts, string label) => facts.Single(f => f.Label == label).Value;

    /// <summary>Puts <paramref name="pk"/> in the save's first empty box slot.</summary>
    static SaveSlot Place(SaveFile save, PKM pk)
    {
        var index = Enumerable.Range(0, save.SlotCount).First(i => save.GetBoxSlotAtIndex(i).Species == 0);
        save.SetBoxSlotAtIndex(pk, index, EntityImportSettings.None);
        return new SaveSlot(index / save.BoxSlotCount + 1, index % save.BoxSlotCount + 1);
    }

    [Fact]
    public void A_Pokemon_in_a_game_shows_its_stats_moves_and_origin()
    {
        var details = InSave(platinum, platinumId, new SaveSlot(null, 1));

        Assert.Equal("Kirlia", details.Title);
        Assert.StartsWith("Lv20", details.Subtitle);
        Assert.Equal("In Platinum (TEST), party slot 1.", details.Where);
        Assert.Equal("Hardy (no effect)", Fact(details.Facts, "Nature"));
        Assert.Equal("Trace", Fact(details.Facts, "Ability"));
        Assert.Equal("None", Fact(details.Facts, "Held item"));
        Assert.Equal("It hates to lose!", Fact(details.Facts, "Characteristic"));

        // The summary screen's order, with stats worked out from base stats, IVs, EVs, nature and level.
        Assert.Equal(["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"], details.Stats.Select(s => s.Name));
        Assert.Equal([46, 20, 22, 32, 31, 29], details.Stats.Select(s => s.Value));
        Assert.All(details.Stats, s => Assert.Equal(0, s.Nature));

        var pk = platinum.GetPartySlotAtIndex(0);
        Assert.Equal(Enumerable.Range(0, 4).Count(i => pk.GetMove(i) != 0), details.Moves.Count);
        Assert.Equal(EnglishNames.Move(pk.Move1), details.Moves[0].Name);
        Assert.Equal(25, details.Moves[0].MaxPP);

        Assert.Equal($"TEST ♀ · ID {pk.TID16:D5}", Fact(details.Origin, "Trainer"));
        Assert.Equal("Platinum", Fact(details.Origin, "Origin game"));
        Assert.Equal("Route 208, Oct 27, 2026, at Lv17", Fact(details.Origin, "Met"));
        Assert.Equal("Poké Ball", Fact(details.Origin, "Ball"));

        // Not a bank Pokémon: there's no other data to show.
        Assert.Empty(details.Visits);
        Assert.Empty(details.Kept);
    }

    [Fact]
    public void A_Gen_3_Pokemon_shows_its_nature_effect_and_item_with_no_dates()
    {
        var treecko = (PK3)emerald.GetPartySlotAtIndex(0);
        treecko.HeldItem = 200; // Leftovers, in Gen 3's own numbering
        var slot = Place(emerald, treecko);

        var details = InSave(emerald, emeraldId, slot);

        Assert.Equal("Adamant (+Attack, −Sp. Atk)", Fact(details.Facts, "Nature"));
        Assert.Equal(1, details.Stats.Single(s => s.Name == "Attack").Nature);
        Assert.Equal(-1, details.Stats.Single(s => s.Name == "Sp. Atk").Nature);
        Assert.Equal("Leftovers", Fact(details.Facts, "Held item"));
        Assert.DoesNotContain(details.Facts, f => f.Label == "Characteristic"); // Gen 4 and later
        Assert.Equal("Route 101, at Lv5", Fact(details.Origin, "Met")); // Gen 3 records no date
        Assert.StartsWith("In Emerald (TEST), ", details.Where);
    }

    [Fact]
    public void Ribbons_are_listed_by_name_and_Gen_3_contest_ribbons_by_rank()
    {
        var kirlia = (PK4)platinum.GetPartySlotAtIndex(0);
        kirlia.RibbonChampionSinnoh = true;
        kirlia.RibbonEffort = true;
        var treecko = (PK3)emerald.GetPartySlotAtIndex(0);
        treecko.RibbonCountG3Cool = 4;

        Assert.Equal(["Sinnoh Champion", "Effort"], InSave(platinum, platinumId, Place(platinum, kirlia)).Ribbons.Order().Reverse());
        Assert.Equal(["Cool (Master Rank)"], InSave(emerald, emeraldId, Place(emerald, treecko)).Ribbons);
    }

    /// <summary>
    /// Platinum's Kirlia, holding a Razor Claw and knowing Psycho Cut (neither exists in Gen 3), goes to the bank and
    /// then out to Emerald.
    /// </summary>
    BankPokemon WithdrawnToEmerald(out SaveSlot inEmerald)
    {
        var kirlia = (PK4)platinum.GetPartySlotAtIndex(0);
        kirlia.HeldItem = 326; // Razor Claw
        kirlia.Move4 = (ushort)Move.PsychoCut;
        kirlia.Move4_PP = 20;
        kirlia.RefreshChecksum();
        var imported = Transfers.Import(bank, platinum, platinumId, Place(platinum, kirlia), Monday);
        Assert.True(imported.Succeeded, imported.Refusal);

        var withdrawn = Transfers.Withdraw(bank, imported.Pokemon!.Id, emerald, emeraldId, Tuesday);
        Assert.True(withdrawn.Succeeded, withdrawn.Refusal);
        inEmerald = withdrawn.SaveSlot!.Value;
        return withdrawn.Pokemon!;
    }

    [Fact]
    public void A_bank_Pokemon_out_in_a_game_shows_the_games_it_visited_and_what_the_bank_keeps()
    {
        var record = WithdrawnToEmerald(out var slot);

        var details = InSave(emerald, emeraldId, slot);

        Assert.Contains("It's out from the bank, where Box 1 slot 1 is kept for it.", details.Where);
        Assert.Equal("Emerald", details.InGame);
        Assert.Equal("None", Fact(details.Facts, "Held item"));
        Assert.DoesNotContain(details.Moves, m => m.Name == "Psycho Cut");

        Assert.Equal([(3, "Emerald (TEST)", true), (4, "Platinum (TEST)", false)],
            details.Visits.Select(v => (v.Generation, v.Game, v.IsLatest)));
        Assert.Equal(Monday, details.Visits[1].LastSeen);
        Assert.Equal(20, details.Visits[1].Level);

        // Its origin as Platinum recorded it, then how Emerald, which can't name a Gen 4 game, records it.
        Assert.Equal("Platinum", Fact(details.Origin, "Origin game"));
        Assert.Equal("Route 208, Oct 27, 2026, at Lv17", Fact(details.Origin, "Met"));
        Assert.Equal("from Emerald · (Fateful Encounter), at Lv17", Fact(details.Origin, "In Emerald"));

        // What its Gen 4 data holds that Emerald can't: the same notes the withdraw gave.
        var kept = Assert.Single(details.Kept);
        Assert.Equal((4, "Platinum (TEST)"), (kept.Generation, kept.Game));
        Assert.Contains(kept.Lines, l => l.Contains("Psycho Cut"));
        Assert.Contains(kept.Lines, l => l.Contains("Razor Claw"));

        // The bank's own view says where it is, and that it shows the Pokémon as it left.
        var fromBank = PokemonDetails.ForBank(bank, record, Name);
        Assert.Equal($"Out in Emerald (TEST), {slot}. Its place in the bank, Box 1 slot 1, is kept for it.", fromBank.Where);
        Assert.Equal("Shown as it was when it left the bank on Nov 10, 2026. It may have changed in the game since.", fromBank.Note);
        Assert.Equal(details.Kept.Single().Lines, fromBank.Kept.Single().Lines);
    }

    [Fact]
    public void A_Pokemon_in_the_bank_shows_its_newest_data()
    {
        var kirlia = platinum.GetPartySlotAtIndex(0);
        var imported = Transfers.Import(bank, platinum, platinumId, Place(platinum, kirlia), Monday);

        var details = PokemonDetails.ForBank(bank, imported.Pokemon!, Name);

        Assert.Equal("Kirlia", details.Title);
        Assert.Equal("In the bank, Box 1 slot 1.", details.Where);
        Assert.Null(details.Note);
        Assert.Equal("Platinum", details.InGame);
        Assert.Equal([(4, "Platinum (TEST)", true)], details.Visits.Select(v => (v.Generation, v.Game, v.IsLatest)));
        Assert.DoesNotContain(details.Origin, f => f.Label.StartsWith("In ", StringComparison.Ordinal)); // nothing differs
        Assert.Empty(details.Kept);
    }

    [Fact]
    public void An_empty_slot_has_no_details()
    {
        var index = Enumerable.Range(0, platinum.SlotCount).First(i => platinum.GetBoxSlotAtIndex(i).Species == 0);
        Assert.Null(PokemonDetails.ForSave(bank, platinum, platinumId,
            new SaveSlot(index / platinum.BoxSlotCount + 1, index % platinum.BoxSlotCount + 1), "Platinum", Name));
        Assert.Null(PokemonDetails.ForSave(bank, platinum, platinumId, new SaveSlot(null, platinum.PartyCount + 1), "Platinum", Name));
    }
}
