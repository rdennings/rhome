using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>Identity matching and merge for returning Pokémon (issue #20).</summary>
public class ReturnTests
{
    static readonly DateTimeOffset Wednesday = TestBank.Tuesday.AddDays(1);
    static readonly DateTimeOffset Thursday = Wednesday.AddDays(1);

    /// <summary>Plays with the Pokémon in a save slot: whatever <paramref name="play"/> does, as if in game.</summary>
    static PKM Play(SaveFile save, SaveSlot slot, Action<PKM> play)
    {
        var index = (slot.Box!.Value - 1) * save.BoxSlotCount + slot.Slot - 1;
        var pk = save.GetBoxSlotAtIndex(index);
        play(pk);
        pk.RefreshChecksum();
        save.SetBoxSlotAtIndex(pk, index, EntityImportSettings.None);
        return pk;
    }

    static TransferResult Ok(TransferResult result)
    {
        Assert.True(result.Succeeded, result.Refusal);
        return result;
    }

    /// <summary>The Platinum Kirlia, holding a Griseous Orb and a Sinnoh ribbon that Emerald can't keep.</summary>
    static (TestBank Bank, BankPokemon Kirlia, PK4 Original) KirliaInTheBank()
    {
        var bank = new TestBank("platinum-us", "emerald");
        var kirlia = (PK4)Fixtures.Party("platinum-us", 1);
        kirlia.HeldItem = 112; // Griseous Orb
        kirlia.RibbonChampionSinnoh = true;
        kirlia.RefreshChecksum();
        return (bank, bank.Add(kirlia, "platinum-us"), kirlia);
    }

    [Fact]
    public void A_Pokemon_that_levelled_up_and_learned_a_move_elsewhere_comes_back_with_its_progress()
    {
        var (bank, record, original) = KirliaInTheBank();
        var placeholder = record.Slot;
        var out_ = Ok(bank.Withdraw(record, "emerald"));

        // In Emerald: five levels, a new move in place of the second one, more friendship.
        Play(bank.Save("emerald"), out_.SaveSlot!.Value, pk =>
        {
            pk.EXP = Experience.GetEXP(25, pk.PersonalInfo.EXPGrowth);
            pk.Move2 = (ushort)Move.Psychic;
            pk.HealPPIndex(1);
            pk.OriginalTrainerFriendship = 160;
        });

        var back = Ok(bank.Import("emerald", out_.SaveSlot!.Value, Wednesday));
        Assert.True(back.Returned);
        Assert.Same(record, back.Pokemon);
        Assert.True(record.IsInBank);
        Assert.Equal(placeholder, record.Slot);
        Assert.Single(bank.Bank.Pokemon);
        Assert.Equal(25, record.Summary.Level);
        Assert.Equal("PK3", record.Newest.Format);

        var home = (PK4)Ok(bank.Withdraw(record, "platinum-us", when: Thursday)).Written!;

        // The progress it made in Emerald…
        Assert.Equal(25, home.CurrentLevel);
        Assert.Equal(160, home.OriginalTrainerFriendship);
        Assert.Equal((ushort)Move.Psychic, home.Move2);
        // …and what only Platinum could hold.
        Assert.Equal(112, home.HeldItem);
        Assert.True(home.RibbonChampionSinnoh);
        Assert.Equal(original.MetLocation, home.MetLocation);
        Assert.Equal(original.MetDate, home.MetDate);
        Assert.Equal(original.Version, home.Version);
        Assert.Equal(original.Ball, home.Ball);
        Assert.Equal(original.PID, home.PID);
    }

    [Fact]
    public void The_moveset_it_had_last_time_in_this_game_is_offered_and_can_be_chosen()
    {
        var (bank, record, original) = KirliaInTheBank();
        var out_ = Ok(bank.Withdraw(record, "emerald"));
        Ok(bank.Import("emerald", out_.SaveSlot!.Value, Wednesday));

        // It lost Lucky Chant (a Gen 4 move) in Emerald, so its newest moveset differs from Platinum's.
        var options = Transfers.Movesets(bank.Bank, record, bank.Save("platinum-us"), Thursday);
        Assert.Equal(Moveset.Of(original), options.LastInThisGame);
        Assert.DoesNotContain((ushort)Move.LuckyChant, Moves(options.Newest));

        var home = Ok(bank.Withdraw(record, "platinum-us", when: Thursday, moves: MovesetChoice.LastInThisGame)).Written!;

        Assert.Equal(Moveset.Of(original), Moveset.Of(home));
        Assert.Equal(original.Move1_PPUps, home.Move1_PPUps);
    }

    [Fact]
    public void No_choice_is_offered_when_the_movesets_are_the_same()
    {
        var bank = new TestBank("platinum-us", "white");
        var gastly = (PK4)Fixtures.Box("platinum-us", 1, 3);
        var record = bank.Add(gastly, "platinum-us");
        var out_ = Ok(bank.Withdraw(record, "white"));
        Ok(bank.Import("white", out_.SaveSlot!.Value, Wednesday));

        var options = Transfers.Movesets(bank.Bank, record, bank.Save("platinum-us"), Thursday);

        Assert.Null(options.LastInThisGame);
        Assert.Equal(Moveset.Of(gastly), options.Newest);
    }

    [Fact]
    public void A_move_the_game_lacks_gets_back_the_move_that_slot_had_last_time()
    {
        // White-caught Eevee: to Platinum, back, to White where it learns Round (Gen 5) in slot 2, back, to Platinum.
        var bank = new TestBank("white", "platinum-us");
        var eevee = (PK5)Fixtures.Party("white", 1);
        eevee.Species = (ushort)Species.Eevee;
        eevee.Gender = EntityGender.GetFromPID(eevee.Species, eevee.PID);
        eevee.IsNicknamed = false;
        eevee.Nickname = "Eevee";
        eevee.RefreshAbility(0);
        eevee.SetMoves([(ushort)Move.Tackle, (ushort)Move.TailWhip, (ushort)Move.SandAttack, (ushort)Move.Growl]);
        eevee.HealPP();
        eevee.RefreshChecksum();
        var record = bank.Add(eevee, "white");

        var first = Ok(bank.Withdraw(record, "platinum-us"));
        Ok(bank.Import("platinum-us", first.SaveSlot!.Value, Wednesday));
        var inWhite = Ok(bank.Withdraw(record, "white", when: Wednesday));
        Play(bank.Save("white"), inWhite.SaveSlot!.Value, pk => pk.Move2 = (ushort)Move.Round);
        Ok(bank.Import("white", inWhite.SaveSlot!.Value, Thursday));

        var result = Ok(bank.Withdraw(record, "platinum-us", when: Thursday.AddDays(1)));
        var pk4 = result.Written!;

        Assert.Equal((ushort)Move.TailWhip, pk4.Move2);
        Assert.Equal([(ushort)Move.Tackle, (ushort)Move.TailWhip, (ushort)Move.SandAttack, (ushort)Move.Growl], Moves(Moveset.Of(pk4)));
        var note = Assert.Single(result.Notes, n => n.Field == ChangedField.Moves);
        Assert.Equal(ChangeKind.Replaced, note.Kind);
        Assert.Contains("Round", note.Message);
        Assert.Contains("Tail Whip", note.Message);
    }

    [Fact]
    public void A_Gen_5_Pokemon_with_a_new_PID_is_recognized_when_it_comes_back()
    {
        var bank = new TestBank("white", "platinum-us");
        var kirlia = (PK5)Fixtures.Party("white", 1);
        kirlia.Species = (ushort)Species.Kirlia;
        kirlia.Gender = EntityGender.GetFromPID(kirlia.Species, kirlia.PID);
        kirlia.RefreshAbility(0);
        kirlia.RefreshChecksum();
        var record = bank.Add(kirlia, "white");

        var out_ = Ok(bank.Withdraw(record, "platinum-us"));
        Assert.NotEqual(kirlia.PID, out_.Written!.PID);
        var back = Ok(bank.Import("platinum-us", out_.SaveSlot!.Value, Wednesday));

        Assert.True(back.Returned);
        var home = (PK5)Ok(bank.Withdraw(record, "white", when: Thursday)).Written!;
        Assert.Equal(kirlia.PID, home.PID);
        Assert.Equal(kirlia.Nature, home.Nature);
    }

    [Fact]
    public void A_new_Pokemon_with_no_lead_becomes_a_new_record()
    {
        var bank = new TestBank("platinum-us");

        var result = Ok(bank.Import("platinum-us", new SaveSlot(1, 3)));

        Assert.False(result.Returned);
        Assert.Single(bank.Bank.Pokemon);
    }

    [Fact]
    public void Turning_up_in_another_save_isnt_unmistakable_so_the_user_is_asked()
    {
        // Withdrawn into Emerald, but found in a second Emerald save (say the player traded it between the two).
        var (bank, record, _) = KirliaInTheBank();
        var other = new TestBank("emerald");
        bank.Bank.Saves.Add(other.Bank.Saves[0]);
        var out_ = Ok(bank.Withdraw(record, "emerald"));
        var elsewhere = other.Save("emerald");
        elsewhere.SetBoxSlotAtIndex(out_.Written!, 10, EntityImportSettings.None);
        var before = elsewhere.Write().ToArray();

        var asked = Transfers.Import(bank.Bank, elsewhere, other.Id("emerald"), new SaveSlot(1, 11), Wednesday);

        Assert.False(asked.Succeeded);
        Assert.Equal([record], asked.Candidates);
        Assert.Equal(before, elsewhere.Write().ToArray());
        Assert.Single(bank.Bank.Pokemon);

        var answered = Transfers.Import(bank.Bank, elsewhere, other.Id("emerald"), new SaveSlot(1, 11), Wednesday,
            how: ImportAs.Return(record.Id));
        Assert.True(answered.Returned);
        Assert.True(record.IsInBank);
    }

    [Fact]
    public void The_user_can_import_a_candidate_as_a_new_Pokemon_instead()
    {
        var (bank, record, _) = KirliaInTheBank();
        var out_ = Ok(bank.Withdraw(record, "emerald"));
        var other = new TestBank("emerald");
        bank.Bank.Saves.Add(other.Bank.Saves[0]);
        other.Save("emerald").SetBoxSlotAtIndex(out_.Written!, 10, EntityImportSettings.None);

        var result = Transfers.Import(bank.Bank, other.Save("emerald"), other.Id("emerald"), new SaveSlot(1, 11), Wednesday,
            how: ImportAs.New);

        Assert.True(result.Succeeded, result.Refusal);
        Assert.False(result.Returned);
        Assert.Equal(2, bank.Bank.Pokemon.Count);
        Assert.False(record.IsInBank); // the original is still out in the first Emerald save
    }

    [Fact]
    public void A_copy_of_a_Pokemon_sitting_in_the_bank_is_only_a_candidate()
    {
        var bank = new TestBank("platinum-us");
        var gastly = Fixtures.Box("platinum-us", 1, 3);
        var inBank = bank.Add(gastly, "platinum-us", slot: 5);

        var asked = bank.Import("platinum-us", new SaveSlot(1, 3));

        Assert.Equal([inBank], asked.Candidates);
        Assert.Equal("That one is in the bank already, not out in a game.",
            bank.Import("platinum-us", new SaveSlot(1, 3), how: ImportAs.Return(inBank.Id)).Refusal);
    }

    [Fact]
    public void Returning_to_an_unrelated_record_is_refused()
    {
        var (bank, record, _) = KirliaInTheBank();
        Ok(bank.Withdraw(record, "emerald"));

        var result = bank.Import("platinum-us", new SaveSlot(1, 3), how: ImportAs.Return(record.Id)); // a Gastly

        Assert.Contains("can't be the same Pokémon", result.Refusal);
        Assert.False(record.IsInBank);
    }

    [Fact]
    public void Two_checked_out_records_that_both_fit_are_not_guessed_between()
    {
        var bank = new TestBank("platinum-us", "emerald");
        var kirlia = (PK4)Fixtures.Party("platinum-us", 1);
        var first = bank.Add(kirlia, "platinum-us", slot: 1);
        var second = bank.Add(kirlia, "platinum-us", slot: 2);
        var out1 = Ok(bank.Withdraw(first, "emerald"));
        Ok(bank.Withdraw(second, "emerald"));

        var asked = bank.Import("emerald", out1.SaveSlot!.Value, Wednesday);

        Assert.Equal(2, asked.Candidates.Count);
    }

    [Fact]
    public void HM_moves_stay_when_a_Pokemon_moves_forward()
    {
        // Pal Park refuses a Pokémon that knows an HM, and PKHeX strips them; rhome keeps them.
        var bank = new TestBank("emerald", "platinum-us");
        var treecko = (PK3)Fixtures.Party("emerald", 1);
        treecko.Move4 = (ushort)Move.Cut;
        treecko.HealPP();
        treecko.RefreshChecksum();
        var record = bank.Add(treecko, "emerald");

        var result = Ok(bank.Withdraw(record, "platinum-us"));

        Assert.Equal((ushort)Move.Cut, result.Written!.Move4);
        Assert.DoesNotContain(result.Notes, n => n.Field == ChangedField.Moves);
    }

    static ushort[] Moves(Moveset m) => [m.Move1, m.Move2, m.Move3, m.Move4];
}
