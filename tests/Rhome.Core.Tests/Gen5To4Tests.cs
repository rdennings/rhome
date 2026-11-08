using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>The Gen 5 → Gen 4 downgrade, and Gen 5 → 3 through it (issue #17).</summary>
public class Gen5To4Tests
{
    /// <summary>
    /// The White fixture holds only Unova species, which can't go to Gen 4 at all. So most tests use its
    /// Snivy turned into another species: everything else (a Modest nature its PID doesn't give, Unova met
    /// data, White as origin, Gen 5 moves) is what the game wrote.
    /// </summary>
    static PK5 WhiteCaught(Species species = Species.Kirlia)
    {
        var pk5 = (PK5)Fixtures.Party("white", 1);
        pk5.Species = (ushort)species;
        pk5.Gender = EntityGender.GetFromPID(pk5.Species, pk5.PID);
        var personal = PersonalTable.B2W2[pk5.Species];
        pk5.Ability = personal.GetAbilityAtIndex((int)((pk5.PID >> 16) & 1)); // Gen 5's own slot rule
        pk5.Nickname = SpeciesName.GetSpeciesNameGeneration(pk5.Species, pk5.Language, 5);
        pk5.IsNicknamed = false;
        pk5.RefreshChecksum();
        return pk5;
    }

    static PK4 Converted(PK5 pk5, GameVersion target = GameVersion.Pt)
    {
        var result = Gen5To4.Convert(pk5, target);
        Assert.True(result.Succeeded, result.Refusal);
        return result.Pokemon;
    }

    static string Refusal(PK5 pk5, GameVersion target = GameVersion.Pt)
    {
        var result = Gen5To4.Convert(pk5, target);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Notes);
        return result.Refusal;
    }

    static ConversionNote Note<T>(ConversionResult<T> result, ChangedField field) where T : PKM =>
        Assert.Single(result.Notes, n => n.Field == field);

    [Fact]
    public void Every_Pokemon_in_the_White_fixture_is_an_Unova_species_and_refused()
    {
        var save = Fixtures.Load("white");

        var refusals = SaveContents.Party(save).Concat(SaveContents.Boxes(save))
            .Select(s => Refusal((PK5)s.Pokemon)).ToList();

        Assert.Equal(10, refusals.Count);
        Assert.Equal("Snivy doesn't exist in the Gen 4 games.", refusals[0]);
        Assert.All(refusals, r => Assert.EndsWith("doesn't exist in the Gen 4 games.", r));
    }

    [Fact]
    public void A_White_caught_Kirlia_keeps_what_Gen_4_can_hold_field_by_field()
    {
        var pk5 = WhiteCaught();
        Assert.NotEqual(pk5.Nature, (Nature)(pk5.PID % 25)); // the case the new PID is for

        var result = Gen5To4.Convert(pk5, GameVersion.Pt);
        var pk4 = result.Pokemon!;

        Assert.Equal(pk5.Species, pk4.Species);
        Assert.Equal(Nature.Modest, pk4.Nature);
        Assert.Equal(pk5.Gender, pk4.Gender);
        Assert.Equal(pk5.IsShiny, pk4.IsShiny);
        Assert.Equal(pk5.Ability, pk4.Ability);
        Assert.Equal(pk5.ID32, pk4.ID32);
        Assert.Equal(pk5.OriginalTrainerName, pk4.OriginalTrainerName);
        Assert.Equal(pk5.OriginalTrainerGender, pk4.OriginalTrainerGender);
        Assert.Equal(pk5.Language, pk4.Language);
        Assert.Equal("KIRLIA", pk4.Nickname);
        Assert.False(pk4.IsNicknamed);
        Assert.Equal(pk5.EXP, pk4.EXP);
        Assert.Equal(pk5.CurrentLevel, pk4.CurrentLevel);
        Assert.Equal(pk5.OriginalTrainerFriendship, pk4.OriginalTrainerFriendship);
        Assert.Equal(pk5.IV32 & 0x3FFFFFFF, pk4.IV32 & 0x3FFFFFFF);
        Assert.Equal(pk5.Ball, pk4.Ball);
        Assert.Equal(pk5.MetLevel, pk4.MetLevel);
        Assert.Equal(pk5.MetDate, pk4.MetDate);
        Assert.Equal(pk5.MarkingValue, pk4.MarkingValue);
        Assert.Equal(GameVersion.Pt, pk4.Version);
        Assert.Equal(Locations.Faraway4, pk4.MetLocation);

        int[] evs5 = new int[6], evs4 = new int[6];
        pk5.GetEVs(evs5);
        pk4.GetEVs(evs4);
        Assert.Equal(evs5, evs4);

        var kept = Enumerable.Range(0, 4).Select(pk5.GetMove).Where(m => m is > 0 and <= Gen5To4.MaxMove);
        Assert.Equal(kept, Enumerable.Range(0, 4).Select(pk4.GetMove).Where(m => m != 0));

        Assert.NotEqual(pk5.PID, pk4.PID);
        Assert.Contains("Modest", Note(result, ChangedField.PersonalityValue).Message);
        Assert.Contains("as if from Platinum", Note(result, ChangedField.MetData).Message);
        Assert.True(pk4.ChecksumValid);
    }

    [Theory]
    [InlineData(Species.Kirlia, false)]
    [InlineData(Species.Kirlia, true)]
    [InlineData(Species.Bulbasaur, false)]  // seven in eight are male
    [InlineData(Species.Bulbasaur, true)]
    [InlineData(Species.Wurmple, false)]
    [InlineData(Species.Magnemite, false)]  // genderless
    [InlineData(Species.Unown, false)]
    public void The_new_PID_keeps_everything_else_the_PID_decides(Species species, bool shiny)
    {
        var pk5 = WhiteCaught(species);
        if (shiny)
        {
            pk5.SetShiny();
            pk5.Gender = EntityGender.GetFromPID(pk5.Species, pk5.PID);
            pk5.Nature = (Nature)((pk5.PID + 7) % 25); // make sure it still needs a new PID
        }
        if (species == Species.Unown)
            pk5.Form = EntityPID.GetUnownForm3(pk5.PID);
        var personal = PersonalTable.Pt[pk5.Species];

        var pk4 = Converted(pk5);

        Assert.NotEqual(pk5.PID, pk4.PID);
        Assert.Equal(pk5.Nature, pk4.Nature);
        Assert.Equal(pk5.Gender, pk4.Gender);
        Assert.Equal(EntityGender.GetFromPID(pk4.Species, pk4.PID), pk4.Gender);
        Assert.Equal(shiny, pk4.IsShiny);
        Assert.Equal(pk5.Ability, pk4.Ability);
        if (personal.HasSecondAbility)
            Assert.Equal(personal.GetIndexOfAbility(pk4.Ability), (int)(pk4.PID & 1));
        Assert.Equal((pk5.PID >> 16) % 10 < 5, (pk4.PID >> 16) % 10 < 5); // Wurmple's evolution
        if (species == Species.Unown)
            Assert.Equal(pk5.Form, EntityPID.GetUnownForm3(pk4.PID));

        Assert.Equal(pk4.PID, Converted(pk5).PID); // the same Pokémon always gets the same PID
    }

    [Fact]
    public void A_Pokemon_whose_nature_already_matches_its_PID_keeps_its_PID()
    {
        var pk5 = WhiteCaught();
        pk5.Nature = (Nature)(pk5.PID % 25);

        var result = Gen5To4.Convert(pk5, GameVersion.Pt);

        Assert.Equal(pk5.PID, result.Pokemon!.PID);
        Assert.DoesNotContain(result.Notes, n => n.Field == ChangedField.PersonalityValue);
    }

    [Fact]
    public void Spinda_is_told_its_spots_change_with_the_PID()
    {
        var result = Gen5To4.Convert(WhiteCaught(Species.Spinda), GameVersion.Pt);

        Assert.Contains("spot pattern", Note(result, ChangedField.PersonalityValue).Message);
    }

    [Fact]
    public void A_hidden_ability_becomes_a_regular_one_with_a_note()
    {
        var pk5 = WhiteCaught(Species.Eevee);
        pk5.HiddenAbility = true;
        pk5.Ability = (int)Ability.Anticipation;

        var result = Gen5To4.Convert(pk5, GameVersion.Pt);
        var pk4 = result.Pokemon!;

        var personal = PersonalTable.Pt[pk4.Species];
        Assert.Equal(personal.GetAbilityAtIndex((int)(pk5.PID & 1)), pk4.Ability);
        Assert.Equal((int)(pk5.PID & 1), (int)(pk4.PID & 1));
        Assert.Equal(
            $"The hidden ability Anticipation becomes {Rhome.Core.EnglishNames.Ability(pk4.Ability)} (Gen 4 has no hidden abilities).",
            Note(result, ChangedField.Ability).Message);
    }

    [Theory]
    [InlineData("platinum-us")]
    [InlineData("diamond")]
    [InlineData("soulsilver")]
    public void Nothing_the_game_sets_itself_is_left_unset(string game)
    {
        // PLAN.md §6.4, against a Pokémon each Gen 4 game made itself.
        var native = Fixtures.Party(game, 1);
        HashSet<string> individual =
        [
            "IsNicknamed",                         // the Monferno and Cyndaquil were nicknamed
            "HeldItem", "SpriteItem",              // depends on the Pokémon
            "Move4", "Move4_PP",                   // the White Snivy knows three moves
            "EggLocation", "EggLocationDP",        // the Platinum Kirlia hatched from an egg; ours was caught
            "EggDay", "EggMonth", "EggYear",
            "MetLocationExtended", "BallHGSS",     // only set for Pokémon met in Platinum or HeartGold/SoulSilver
            "GroundTile",                          // the kind of ground it was met on in Gen 4; ours wasn't
            "PotentialRating",                     // judged from the IVs
            "EV_HP", "EV_ATK", "EV_DEF",           // EVs come from battles; the Snivy has none in some stats
            "EV_SPA", "EV_SPD", "EV_SPE",
            "WalkingMood",                         // HeartGold/SoulSilver, changes as it follows you
            "ShinyLeaf",                           // HeartGold/SoulSilver, earned in game
            "PtHGSS", "HGSS", "WasCreatedInHGSS",  // PKHeX's "which game is it from"
        ];

        var ours = Converted(WhiteCaught(), native.Version);
        var unset = PkmFields.UnsetOnOursButSetByTheGame(ours, native)
            .Where(field => !individual.Contains(field.Split(' ')[0]));

        Assert.Empty(unset);
    }

    [Fact]
    public void A_Gen_4_Pokemon_sent_to_Gen_5_and_back_keeps_its_data()
    {
        // PKHeX's Poké Transfer step forward, ours back. What Poké Transfer itself loses (the Gen 4 met
        // location and level) and resets (friendship to 70) the bank restores from its Gen 4 snapshot.
        var original = (PK4)Fixtures.Party("platinum-us", 1);

        var back = Converted(original.ConvertToPK5());

        Assert.Equal(
            [
                "CurrentFriendship: 70 ≠ 67", "MetLevel: 20 ≠ 17", "MetLocation: 3002 ≠ 23",
                "MetLocationDP: 3002 ≠ 23", "MetLocationExtended: 3002 ≠ 23", "OriginalTrainerFriendship: 70 ≠ 67",
            ],
            PkmFields.Differences(back, original).Where(d => !d.StartsWith("Stat_", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_Pokemon_hatched_in_Gen_5_gets_Gen_4s_hatched_met_level()
    {
        // Gen 5 records a hatch as met at level 1, Gen 4 at level 0 (Hatching).
        var pk5 = WhiteCaught();
        pk5.EggLocation = Locations.Daycare5;
        pk5.MetLevel = Hatching.MetLevelRecorded(5);

        var pk4 = Converted(pk5);

        Assert.Equal(Locations.Daycare4, pk4.EggLocation);
        Assert.Equal(0, pk4.MetLevel);
    }

    [Fact]
    public void A_Pokemon_hatched_in_Gen_4_gets_back_the_met_level_Poke_Transfer_replaced()
    {
        // The Platinum Togepi hatched at level 1. Poké Transfer records it as met at the level it arrived at.
        var original = (PK4)Fixtures.Box("platinum-us", 1, 4);
        Assert.Equal((ushort)Species.Togepi, original.Species);
        var pk5 = original.ConvertToPK5();
        pk5.EXP = Experience.GetEXP(12, pk5.PersonalInfo.EXPGrowth);
        pk5.MetLevel = 12;

        var back = Converted(pk5);

        Assert.Equal(original.MetLevel, back.MetLevel);
        // Poké Transfer itself turned its egg location (Cynthia's gift egg) into a faraway place. Only the bank's
        // Gen 4 snapshot still has it, and that's what puts it back when the Togepi returns.
        Assert.Equal(Locations.Faraway4, back.EggLocation);
    }

    [Fact]
    public void A_Gen_3_Pokemon_back_from_Gen_5_shows_as_arrived_through_Pal_Park()
    {
        var gen3 = (PK3)Fixtures.Party("emerald", 1);

        var result = Gen5To4.Convert(gen3.ConvertToPK4().ConvertToPK5(), GameVersion.Pt);

        Assert.Equal(GameVersion.E, result.Pokemon!.Version);
        Assert.Equal(Locations.Transfer3, result.Pokemon.MetLocation);
        Assert.Contains("Pal Park", Note(result, ChangedField.MetData).Message);
    }

    [Fact]
    public void The_converted_Pokemon_survives_being_written_into_a_Platinum_save()
    {
        var save = Fixtures.Load("platinum-us");
        var pk4 = Converted(WhiteCaught());

        save.SetBoxSlotAtIndex(pk4, 10);
        var reread = SaveDetector.Detect(save.Write().ToArray())!.Save;

        Assert.True(reread.ChecksumsValid, reread.ChecksumInfo);
        Assert.Equal(StoredBytes(pk4), StoredBytes(reread.GetBoxSlotAtIndex(10)));
    }

    [Fact]
    public void A_held_item_Gen_4_has_is_kept_and_a_Gen_5_one_dropped()
    {
        var pk5 = WhiteCaught();
        pk5.HeldItem = 234; // Leftovers
        Assert.Equal(234, Converted(pk5).HeldItem);

        pk5.HeldItem = 538; // Eviolite
        var result = Gen5To4.Convert(pk5, GameVersion.Pt);
        Assert.Equal(0, result.Pokemon!.HeldItem);
        Assert.Equal("Dropped the held item Eviolite (can't be held in Platinum).", Note(result, ChangedField.HeldItem).Message);
    }

    [Fact]
    public void The_Dream_Ball_becomes_a_Poke_Ball()
    {
        var pk5 = WhiteCaught();
        pk5.Ball = (byte)Ball.Dream;

        var result = Gen5To4.Convert(pk5, GameVersion.Pt);

        Assert.Equal((byte)Ball.Poke, result.Pokemon!.Ball);
        Assert.Equal("The Dream Ball becomes a Poké Ball (not in Platinum).", Note(result, ChangedField.Ball).Message);
    }

    [Fact]
    public void An_Apricorn_Ball_from_HeartGold_comes_back_as_it_was()
    {
        var pk5 = WhiteCaught();
        pk5.Version = GameVersion.HG;
        pk5.Ball = (byte)Ball.Fast;

        var pk4 = Converted(pk5, GameVersion.HG);

        Assert.Equal((byte)Ball.Fast, pk4.Ball);
    }

    [Fact]
    public void Ribbons_carry_over_one_for_one()
    {
        var pk5 = WhiteCaught();
        pk5.RibbonChampionSinnoh = pk5.RibbonG3Cool = pk5.RibbonFootprint = pk5.RibbonClassic = true;

        var pk4 = Converted(pk5);

        Assert.True(pk4.RibbonChampionSinnoh && pk4.RibbonG3Cool && pk4.RibbonFootprint && pk4.RibbonClassic);
        Assert.Equal(4, pk4.RibbonCount);
    }

    [Fact]
    public void Gen_5_only_markers_are_dropped_with_a_note()
    {
        var pk5 = WhiteCaught();
        pk5.NSparkle = true;
        pk5.PokeStarFame = 40;

        var result = Gen5To4.Convert(pk5, GameVersion.Pt);

        Assert.Equal(2, result.Notes.Count(n => n.Field == ChangedField.Other));
        Assert.Contains(result.Notes, n => n.Message.Contains("N's sparkle", StringComparison.Ordinal));
        Assert.Contains(result.Notes, n => n.Message.Contains("Pokéstar", StringComparison.Ordinal));
    }

    [Fact]
    public void Forms_follow_the_target_game()
    {
        var giratina = WhiteCaught(Species.Giratina);
        giratina.Form = 1; // Origin Forme, new in Platinum
        Assert.Contains("Diamond and Pearl", Refusal(giratina, GameVersion.D));
        Assert.Equal(1, Converted(giratina, GameVersion.Pt).Form);

        var pichu = WhiteCaught(Species.Pichu);
        pichu.Form = 1; // Spiky-eared, HeartGold/SoulSilver only
        Assert.Contains("Platinum", Refusal(pichu, GameVersion.Pt));
        Assert.Equal(1, Converted(pichu, GameVersion.HGSS).Form);
    }

    [Fact]
    public void Eggs_and_Pokemon_with_only_Gen_5_moves_are_refused()
    {
        var egg = WhiteCaught();
        egg.IsEgg = true;
        Assert.Contains("Egg", Refusal(egg));

        var gen5Moves = WhiteCaught();
        gen5Moves.SetMoves([(ushort)Move.Hurricane, (ushort)Move.Scald]);
        Assert.Contains("moves", Refusal(gen5Moves));
    }

    [Fact]
    public void Gen_5_to_Gen_3_goes_through_Gen_4_and_keeps_the_nature()
    {
        var pk5 = WhiteCaught();

        var result = Downgrade.Gen5ToGen3(pk5, GameVersion.E);
        var pk3 = result.Pokemon!;

        Assert.Equal(Nature.Modest, pk3.Nature);
        Assert.Equal(pk5.Gender, pk3.Gender);
        Assert.Equal(pk5.Ability, pk3.Ability);
        Assert.Equal(GameVersion.E, pk3.Version);
        Assert.Equal("KIRLIA", pk3.Nickname);
        Assert.Single(result.Notes, n => n.Field == ChangedField.PersonalityValue);
        var metNote = Note(result, ChangedField.MetData);
        Assert.Contains("fateful encounter", metNote.Message);

        var save = Fixtures.Load("emerald");
        save.SetBoxSlotAtIndex(pk3, 5);
        Assert.True(SaveDetector.Detect(save.Write().ToArray())!.Save.ChecksumsValid);
    }

    [Fact]
    public void Gen_5_to_Gen_3_refuses_what_either_step_refuses()
    {
        Assert.False(Downgrade.Gen5ToGen3((PK5)Fixtures.Party("white", 1), GameVersion.E).Succeeded);
        Assert.Equal("Luxray doesn't exist in the Gen 3 games.",
            Downgrade.Gen5ToGen3(WhiteCaught(Species.Luxray), GameVersion.E).Refusal);
    }

    static byte[] StoredBytes(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }
}
