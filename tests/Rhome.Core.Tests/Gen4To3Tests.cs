using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>The Gen 4 → Gen 3 downgrade (issue #16).</summary>
public class Gen4To3Tests
{
    static PK4 PlatinumKirlia() => (PK4)Fixtures.Party("platinum-us", 1);

    static PK3 Converted(PK4 pk4, GameVersion target = GameVersion.E)
    {
        var result = Gen4To3.Convert(pk4, target);
        Assert.True(result.Succeeded, result.Refusal);
        return result.Pokemon;
    }

    static string Refusal(PK4 pk4)
    {
        var result = Gen4To3.Convert(pk4, GameVersion.E);
        Assert.False(result.Succeeded);
        Assert.Null(result.Pokemon);
        Assert.Empty(result.Notes);
        return result.Refusal;
    }

    static ConversionNote Note(ConversionResult<PK3> result, ChangedField field) =>
        Assert.Single(result.Notes, n => n.Field == field);

    [Fact]
    public void The_Platinum_Kirlia_converts_to_the_same_bytes_the_prototype_wrote_into_Emerald()
    {
        // fixtures/emerald.sav holds the Kirlia prototypes/transfer_pt_to_em.py moved from this Platinum
        // save, and it was checked in game on the Thor. Matching it byte for byte keeps that result.
        var verified = (PK3)Fixtures.Box("emerald", 1, 1);

        var ours = Converted(PlatinumKirlia());

        Assert.Equal(StoredBytes(verified), StoredBytes(ours));
    }

    [Fact]
    public void The_Platinum_Kirlia_keeps_what_Gen_3_can_hold_field_by_field()
    {
        var pk4 = PlatinumKirlia();

        var result = Gen4To3.Convert(pk4, GameVersion.E);
        var pk3 = result.Pokemon!;

        Assert.Equal(pk4.Species, pk3.Species);
        Assert.Equal(pk4.PID, pk3.PID);
        Assert.Equal(pk4.ID32, pk3.ID32);
        Assert.Equal(pk4.OriginalTrainerName, pk3.OriginalTrainerName);
        Assert.Equal(pk4.OriginalTrainerGender, pk3.OriginalTrainerGender);
        Assert.Equal(pk4.Language, pk3.Language);
        Assert.Equal("KIRLIA", pk3.Nickname);
        Assert.False(pk3.IsNicknamed);
        Assert.Equal(pk4.EXP, pk3.EXP);
        Assert.Equal(pk4.CurrentLevel, pk3.CurrentLevel);
        Assert.Equal(pk4.Nature, pk3.Nature);
        Assert.Equal(pk4.Gender, pk3.Gender);
        Assert.Equal(pk4.IsShiny, pk3.IsShiny);
        Assert.Equal(pk4.Ability, pk3.Ability);
        Assert.Equal(pk4.OriginalTrainerFriendship, pk3.OriginalTrainerFriendship);
        Assert.Equal(pk4.Ball, pk3.Ball);
        Assert.Equal(pk4.MetLevel, pk3.MetLevel);
        Assert.Equal(GameVersion.E, pk3.Version);
        Assert.Equal(Gen4To3.FatefulEncounterLocation, pk3.MetLocation);

        int[] ivs4 = new int[6], ivs3 = new int[6], evs4 = new int[6], evs3 = new int[6];
        pk4.GetIVs(ivs4);
        pk3.GetIVs(ivs3);
        pk4.GetEVs(evs4);
        pk3.GetEVs(evs3);
        Assert.Equal(ivs4, ivs3);
        Assert.Equal(evs4, evs3);

        // Kirlia knows Lucky Chant, a Gen 4 move. It's dropped and the moves after it close the gap.
        var gen3Moves = Enumerable.Range(0, 4).Select(pk4.GetMove).Where(m => m is > 0 and <= Gen4To3.MaxMove).ToArray();
        Assert.Contains(Enumerable.Range(0, 4).Select(pk4.GetMove), m => m == (ushort)Move.LuckyChant);
        Assert.Equal(gen3Moves, Enumerable.Range(0, 4).Select(pk3.GetMove).Where(m => m != 0));
        Assert.Equal((ushort)0, pk3.Move4);
        Assert.Contains("Lucky Chant", Note(result, ChangedField.Moves).Message);

        Assert.True(pk3.ChecksumValid);
    }

    [Fact]
    public void Gen_4_species_are_refused_and_the_rest_of_the_Platinum_save_converts()
    {
        var save = Fixtures.Load("platinum-us");
        var all = SaveContents.Party(save).Concat(SaveContents.Boxes(save)).Select(s => (PK4)s.Pokemon);

        var outcomes = all.ToLookup(pk => Gen4To3.Convert(pk, GameVersion.E).Succeeded, pk => ((Species)pk.Species).ToString());

        Assert.Equal(["Kirlia", "Gastly", "Togepi", "Eevee"], outcomes[true]);
        Assert.Equal(["Monferno", "Luxray", "Budew", "Gible", "Buneary", "Buneary", "Bidoof"], outcomes[false]);
        Assert.Equal("Luxray doesn't exist in the Gen 3 games.", Refusal((PK4)Fixtures.Party("platinum-us", 3)));
    }

    [Theory]
    [InlineData("emerald")]
    [InlineData("sapphire")]
    [InlineData("leafgreen")]
    public void Nothing_the_game_sets_itself_is_left_unset(string game)
    {
        // PLAN.md §6.4: compare with a Pokémon the game made itself, and flag fields that are zero on ours
        // but set on theirs. What's left out describes the individual Pokémon, not the format.
        var native = Fixtures.Party(game, 1);
        HashSet<string> individual =
        [
            "IsNicknamed",                  // the native one was given a nickname
            "Nature", "StatAlignment",      // Kirlia is Hardy, which is nature 0
            "Move4", "Move4_PP",            // Kirlia has three Gen 3 moves left
            "HeldItem", "SpriteItem",       // neither holds anything; kept in case a native one does
            "PotentialRating",              // judged from the IVs
            "FRLG",                         // PKHeX's "is from FireRed/LeafGreen", true for the LeafGreen one
        ];

        var unset = PkmFields.UnsetOnOursButSetByTheGame(Converted(PlatinumKirlia()), native)
            .Where(field => !individual.Contains(field.Split(' ')[0]));

        Assert.Empty(unset);
    }

    [Fact]
    public void The_converted_Pokemon_survives_being_written_into_an_Emerald_save()
    {
        var save = (SAV3)Fixtures.Load("emerald");
        var pk3 = Converted(PlatinumKirlia());

        save.SetBoxSlotAtIndex(pk3, 1);
        save.SetPartySlotAtIndex(pk3, 1);
        var reread = SaveDetector.Detect(save.Write().ToArray())!.Save;

        Assert.True(reread.ChecksumsValid, reread.ChecksumInfo);
        Assert.Equal(StoredBytes(pk3), StoredBytes(reread.GetBoxSlotAtIndex(1)));
        var party = (PK3)reread.GetPartySlotAtIndex(1);
        Assert.Equal(StoredBytes(pk3), StoredBytes(party));
        Assert.Equal(-1, party.HeldMailID);
        Assert.Equal(pk3.Stat_HPMax, party.Stat_HPMax);
    }

    [Fact]
    public void A_Gen_3_Pokemon_sent_to_Gen_4_and_back_keeps_its_data()
    {
        // The Pal Park direction is PKHeX's; ours is the way back. What Pal Park itself loses (the Gen 3 met
        // location and level) and resets (friendship to 70) the bank restores from its Gen 3 snapshot.
        var original = (PK3)Fixtures.Party("emerald", 1);

        var back = Converted(original.ConvertToPK4());

        Assert.Equal(
            [
                "CurrentFriendship: 70 ≠ 104", "MetLevel: 11 ≠ 5", "MetLocation: 255 ≠ 16",
                "OriginalTrainerFriendship: 70 ≠ 104",
            ],
            PkmFields.Differences(back, original).Where(d => !d.StartsWith("Stat_", StringComparison.Ordinal)));
        Assert.Equal(original.Version, back.Version);
    }

    [Fact]
    public void A_hatched_level_1_Pokemon_shows_as_level_1_and_met_at_level_1_in_Gen_3()
    {
        // The Platinum Togepi hatched at level 1: 0 EXP and met level 0 in Gen 4. Copied as they were, Emerald
        // showed it as level 0 (its EXP table starts level 1 at 1 EXP) and as met at level 5 (Gen 3's hatch level).
        var pk4 = (PK4)Fixtures.Box("platinum-us", 1, 4);
        Assert.Equal((ushort)Species.Togepi, pk4.Species);
        Assert.Equal((0u, (byte)0, 1), (pk4.EXP, pk4.MetLevel, (int)pk4.CurrentLevel));

        var pk3 = Converted(pk4);

        Assert.Equal(1u, pk3.EXP);
        Assert.Equal(1, pk3.MetLevel);
        Assert.Equal(1, pk3.CurrentLevel);
        Assert.True(pk3.ChecksumValid);
    }

    [Fact]
    public void A_held_item_Gen_3_has_is_kept_and_one_it_lacks_is_dropped()
    {
        var pk4 = PlatinumKirlia();
        pk4.HeldItem = (int)ItemConverter.GetItemFuture3(200); // Leftovers in Gen 3's numbering
        Assert.Equal(200, Converted(pk4).HeldItem);

        pk4.HeldItem = 112; // Griseous Orb
        var result = Gen4To3.Convert(pk4, GameVersion.E);
        Assert.Equal(0, result.Pokemon!.HeldItem);
        Assert.Equal("Dropped the held item Griseous Orb (not in Gen 3).", Note(result, ChangedField.HeldItem).Message);
    }

    [Theory]
    [InlineData(137, "Greet Mail (not in Gen 3)")]
    [InlineData(208, "Enigma Berry (Gen 3's Enigma Berry needs e-Reader data)")]
    public void Items_that_would_misbehave_in_Gen_3_are_dropped(int item, string why)
    {
        var pk4 = PlatinumKirlia();
        pk4.HeldItem = item;

        var result = Gen4To3.Convert(pk4, GameVersion.E);

        Assert.Equal(0, result.Pokemon!.HeldItem);
        Assert.Equal($"Dropped the held item {why}.", Note(result, ChangedField.HeldItem).Message);
    }

    [Fact]
    public void A_ball_Gen_3_lacks_becomes_a_Poke_Ball()
    {
        var pk4 = PlatinumKirlia();
        pk4.Ball = (byte)Ball.Dusk;

        var result = Gen4To3.Convert(pk4, GameVersion.E);

        Assert.Equal((byte)Ball.Poke, result.Pokemon!.Ball);
        Assert.Equal("The Dusk Ball becomes a Poké Ball (not in Gen 3).", Note(result, ChangedField.Ball).Message);
    }

    [Fact]
    public void Hoenn_ribbons_are_kept_and_Sinnoh_ribbons_dropped()
    {
        var pk4 = PlatinumKirlia();
        pk4.RibbonG3Cool = pk4.RibbonG3CoolSuper = pk4.RibbonG3CoolHyper = true;
        pk4.RibbonG3Smart = true;
        pk4.RibbonChampionG3 = pk4.RibbonEarth = true;
        pk4.RibbonChampionSinnoh = pk4.RibbonFootprint = true;

        var result = Gen4To3.Convert(pk4, GameVersion.E);
        var pk3 = result.Pokemon!;

        Assert.Equal(3, pk3.RibbonCountG3Cool);
        Assert.Equal(1, pk3.RibbonCountG3Smart);
        Assert.Equal(0, pk3.RibbonCountG3Beauty);
        Assert.True(pk3.RibbonChampionG3);
        Assert.True(pk3.RibbonEarth);
        Assert.Equal("Dropped 2 Sinnoh ribbons (not in Gen 3).", Note(result, ChangedField.Ribbons).Message);
    }

    [Fact]
    public void An_ability_the_species_lacks_in_Gen_3_is_replaced_by_the_one_its_PID_picks()
    {
        var pk4 = PlatinumKirlia(); // Synchronize or Trace
        pk4.Ability = (int)Ability.Download;

        var result = Gen4To3.Convert(pk4, GameVersion.E);

        var expected = PersonalTable.E[pk4.Species].GetAbility((pk4.PID & 1) == 1);
        Assert.Equal(expected, result.Pokemon!.Ability);
        Assert.Contains("Download", Note(result, ChangedField.Ability).Message);
    }

    [Fact]
    public void The_second_ability_stays_the_second_ability()
    {
        var pk4 = PlatinumKirlia();
        var personal = PersonalTable.E[pk4.Species];
        pk4.Ability = personal.Ability2;

        var pk3 = Converted(pk4);

        Assert.True(pk3.AbilityBit);
        Assert.Equal(personal.Ability2, pk3.Ability);
    }

    [Fact]
    public void A_nickname_with_characters_Gen_3_lacks_gets_question_marks_and_a_note()
    {
        var pk4 = PlatinumKirlia();
        pk4.IsNicknamed = true;
        pk4.Nickname = "Kir♪lia";

        var result = Gen4To3.Convert(pk4, GameVersion.E);

        Assert.Equal("Kir?lia", result.Pokemon!.Nickname);
        Assert.True(result.Pokemon.IsNicknamed);
        Assert.Contains("'♪'", Note(result, ChangedField.Nickname).Message);
    }

    [Fact]
    public void A_nickname_Gen_3_can_show_is_kept_as_it_is()
    {
        var pk4 = PlatinumKirlia();
        pk4.IsNicknamed = true;
        pk4.Nickname = "Tamale";

        var result = Gen4To3.Convert(pk4, GameVersion.E);

        Assert.Equal("Tamale", result.Pokemon!.Nickname);
        Assert.DoesNotContain(result.Notes, n => n.Field is ChangedField.Nickname or ChangedField.TrainerName);
    }

    [Fact]
    public void PP_above_the_Gen_3_maximum_is_lowered()
    {
        var pk4 = PlatinumKirlia();
        pk4.Move1_PP = 99;

        var pk3 = Converted(pk4);

        Assert.Equal(pk3.GetMovePP(pk3.Move1, pk3.Move1_PPUps), pk3.Move1_PP);
    }

    [Fact]
    public void Mew_and_Deoxys_get_the_flag_that_makes_them_obey_in_Gen_3()
    {
        var mew = PlatinumKirlia();
        mew.Species = (ushort)Species.Mew;
        mew.Gender = 2;
        mew.FatefulEncounter = false;

        Assert.True(Converted(mew).FatefulEncounter);
    }

    [Fact]
    public void A_Deoxys_in_another_form_is_converted_with_a_note()
    {
        var deoxys = PlatinumKirlia();
        deoxys.Species = (ushort)Species.Deoxys;
        deoxys.Gender = 2;
        deoxys.Form = 1;

        var result = Gen4To3.Convert(deoxys, GameVersion.E);

        Assert.Equal(ChangeKind.Replaced, Note(result, ChangedField.Form).Kind);
    }

    [Fact]
    public void A_Pokemon_born_in_Gen_4_takes_the_target_game_as_its_origin()
    {
        Assert.Equal(GameVersion.FR, Converted(PlatinumKirlia(), GameVersion.FRLG).Version);
        Assert.Equal(GameVersion.R, Converted(PlatinumKirlia(), GameVersion.RS).Version);
        Assert.Equal(GameVersion.LG, Converted(PlatinumKirlia(), GameVersion.LG).Version);
        Assert.Throws<ArgumentException>(() => Gen4To3.Convert(PlatinumKirlia(), GameVersion.Pt));
    }

    [Fact]
    public void Eggs_are_refused()
    {
        var egg = (PK4)Fixtures.Box("platinum-us", 1, 4); // Togepi
        egg.IsEgg = true;

        Assert.Contains("Egg", Refusal(egg));
    }

    [Fact]
    public void An_Unown_whose_letter_would_change_is_refused()
    {
        var unown = PlatinumKirlia();
        unown.Species = (ushort)Species.Unown;
        unown.Gender = 2;
        unown.Form = (byte)((EntityPID.GetUnownForm3(unown.PID) + 1) % 26);

        Assert.Contains("letter", Refusal(unown));

        unown.Form = EntityPID.GetUnownForm3(unown.PID);
        Assert.Equal(unown.Form, Converted(unown).Form);
    }

    [Fact]
    public void The_Spiky_eared_Pichu_is_refused()
    {
        var pichu = PlatinumKirlia();
        pichu.Species = (ushort)Species.Pichu;
        pichu.Form = 1;
        pichu.Gender = EntityGender.GetFromPID(pichu.Species, pichu.PID);

        Assert.Contains("Spiky-eared", Refusal(pichu));
    }

    [Fact]
    public void A_Korean_Pokemon_is_refused()
    {
        var pk4 = PlatinumKirlia();
        pk4.Language = (int)LanguageID.Korean;

        Assert.Contains("Korean", Refusal(pk4));
    }

    [Fact]
    public void A_Pokemon_whose_gender_would_change_is_refused()
    {
        var pk4 = PlatinumKirlia();
        pk4.Gender = (byte)(pk4.Gender == 0 ? 1 : 0);

        Assert.Contains("gender", Refusal(pk4));
    }

    [Fact]
    public void A_Pokemon_with_only_Gen_4_moves_is_refused()
    {
        var pk4 = PlatinumKirlia();
        pk4.SetMoves([(ushort)Move.LuckyChant, (ushort)Move.AuraSphere]);

        Assert.Contains("moves", Refusal(pk4));
    }

    static byte[] StoredBytes(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }
}
