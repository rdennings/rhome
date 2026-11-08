using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>Import from a save and withdraw into a save (issue #18).</summary>
public class TransferTests
{
    static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(-5));
    static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    /// <summary>A fresh bank that knows these fixture saves, loaded fresh.</summary>
    sealed class Setup
    {
        public Bank Bank { get; } = new();
        readonly Dictionary<string, (SaveFile Save, Guid Id)> _saves = [];

        public Setup(params string[] fixtures)
        {
            foreach (var name in fixtures)
            {
                var save = Fixtures.Load(name);
                var record = new BankSave(Guid.NewGuid(), SaveDetector.GameOf(save),
                    new TrainerInfo(save.OT, save.TID16, save.SID16), name, null);
                Bank.Saves.Add(record);
                _saves[name] = (save, record.Id);
            }
        }

        public SaveFile Save(string name) => _saves[name].Save;

        public Guid Id(string name) => _saves[name].Id;

        public TransferResult Import(string name, SaveSlot slot, DateTimeOffset? when = null) =>
            Transfers.Import(Bank, Save(name), Id(name), slot, when ?? Monday);

        public TransferResult Withdraw(BankPokemon pokemon, string name, SaveSlot? slot = null, DateTimeOffset? when = null) =>
            Transfers.Withdraw(Bank, pokemon.Id, Save(name), Id(name), when ?? Tuesday, slot);
    }

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.sav").Order())
            data.Add(Path.GetFileNameWithoutExtension(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Every_box_Pokemon_imported_and_withdrawn_back_leaves_the_save_byte_identical(string fixture)
    {
        var setup = new Setup(fixture);
        var save = setup.Save(fixture);
        var original = save.Write().ToArray();
        var slots = SaveContents.Boxes(save).Select(s => s.Where).ToList();

        var imported = slots.Select(slot => Ok(setup.Import(fixture, slot)).Pokemon!).ToList();
        Assert.Empty(SaveContents.Boxes(save));
        foreach (var (pokemon, slot) in imported.Zip(slots))
            Assert.Equal(slot, Ok(setup.Withdraw(pokemon, fixture, slot)).SaveSlot);

        Assert.Equal(original, save.Write().ToArray());
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Every_party_Pokemon_comes_back_byte_identical(string fixture)
    {
        var setup = new Setup(fixture);
        var save = setup.Save(fixture);
        var party = SaveContents.Party(save).Select(s => s.Pokemon).ToList();
        if (party.Count < 2)
            return; // the last Pokémon can't leave the party; covered below

        // Take the first one out and back in (it goes to the end of the party, as in the games).
        var record = Ok(setup.Import(fixture, new SaveSlot(null, 1))).Pokemon!;
        Assert.Equal(party.Count - 1, save.PartyCount);
        var back = Ok(setup.Withdraw(record, fixture, new SaveSlot(null, party.Count)));

        Assert.Equal(StoredBytes(party[0]), StoredBytes(save.GetPartySlotAtIndex(party.Count - 1)));
        Assert.Equal(StoredBytes(party[0]), StoredBytes(back.Written!));
        Assert.Equal(party.Skip(1).Select(StoredBytes), SaveContents.Party(save).Take(party.Count - 1).Select(s => StoredBytes(s.Pokemon)));
    }

    [Fact]
    public void Importing_makes_a_bank_record_and_empties_the_slot()
    {
        var setup = new Setup("platinum-us");
        var kirlia = Fixtures.Box("platinum-us", 1, 3); // Gastly

        var result = Ok(setup.Import("platinum-us", new SaveSlot(1, 3)));

        var record = Assert.Single(setup.Bank.Pokemon);
        Assert.Same(record, result.Pokemon);
        Assert.Equal(new BankSlot(1, 1), record.Slot);
        Assert.True(record.IsInBank);
        Assert.Equal(PokemonFingerprint.Of(kirlia), record.Fingerprint);
        var snapshot = Assert.Single(record.Snapshots);
        Assert.Equal(("PK4", setup.Id("platinum-us"), Monday), (snapshot.Format, snapshot.FromSave, snapshot.CapturedAt));
        Assert.Equal(0, setup.Save("platinum-us").GetBoxSlotAtIndex(2).Species);
        Assert.Empty(setup.Bank.Problems());
    }

    [Fact]
    public void The_last_Pokemon_that_isnt_an_egg_cant_leave_the_party()
    {
        var setup = new Setup("emerald");

        var result = setup.Import("emerald", new SaveSlot(null, 1));

        Assert.Contains("last Pokémon", result.Refusal);
        Assert.Empty(setup.Bank.Pokemon);
        Assert.Equal(1, setup.Save("emerald").PartyCount);
    }

    [Fact]
    public void Importing_from_an_empty_or_missing_slot_is_refused()
    {
        var setup = new Setup("platinum-us");

        Assert.Contains("no Pokémon", setup.Import("platinum-us", new SaveSlot(1, 5)).Refusal);
        Assert.Contains("has no box 19", setup.Import("platinum-us", new SaveSlot(19, 1)).Refusal);
        Assert.Empty(setup.Bank.Pokemon);
    }

    [Fact]
    public void A_full_bank_refuses_an_import()
    {
        var setup = new Setup("platinum-us");
        setup.Bank.Boxes.Boxes.RemoveAll(b => b.Id != 1);
        var donor = SaveContents.Party(setup.Save("platinum-us"))[0].Pokemon;
        for (var slot = 1; slot <= BankBox.SlotCount; slot++)
            setup.Bank.Pokemon.Add(BankPokemon.From(donor, new BankSlot(1, slot), setup.Id("platinum-us"), Monday));

        Assert.Equal("Every bank box is full.", setup.Import("platinum-us", new SaveSlot(1, 1)).Refusal);
    }

    [Fact]
    public void A_Platinum_Pokemon_withdrawn_into_Emerald_is_converted_and_checked_out()
    {
        var setup = new Setup("platinum-us", "emerald");
        var record = Ok(setup.Import("platinum-us", new SaveSlot(null, 1))).Pokemon!; // Kirlia

        var result = Ok(setup.Withdraw(record, "emerald"));

        Assert.Equal(new SaveSlot(1, 2), result.SaveSlot); // box 1 slot 1 holds the prototype's Kirlia
        var pk3 = Assert.IsType<PK3>(setup.Save("emerald").GetBoxSlotAtIndex(1));
        Assert.Equal((ushort)Species.Kirlia, pk3.Species);
        Assert.Contains(result.Notes, n => n.Message.Contains("Lucky Chant", StringComparison.Ordinal));

        Assert.Equal(new CheckOut(setup.Id("emerald"), new SaveSlot(1, 2)), record.CheckedOut);
        Assert.Equal(new BankSlot(1, 1), record.Slot); // the placeholder keeps its bank slot
        Assert.Equal(["PK4", "PK3"], record.Snapshots.Select(s => s.Format));
        Assert.Equal("PK3", record.Newest.Format);
        Assert.Equal(StoredBytes(pk3), record.Newest.Data);
        Assert.Empty(setup.Bank.Problems());
    }

    [Fact]
    public void A_Pokemon_that_cant_exist_in_the_game_is_refused_and_nothing_changes()
    {
        var setup = new Setup("platinum-us", "emerald");
        var luxray = Ok(setup.Import("platinum-us", new SaveSlot(null, 3))).Pokemon!;
        var emerald = setup.Save("emerald").Write().ToArray();
        var bank = BankJson.ToBytes(setup.Bank);

        var result = setup.Withdraw(luxray, "emerald");

        Assert.Equal("Luxray doesn't exist in the Gen 3 games.", result.Refusal);
        Assert.Equal(emerald, setup.Save("emerald").Write().ToArray());
        Assert.Equal(bank, BankJson.ToBytes(setup.Bank));
    }

    [Fact]
    public void A_form_the_game_lacks_is_refused_by_the_structural_check()
    {
        // Same format, different game: a Spiky-eared Pichu from SoulSilver can't go into Platinum.
        var setup = new Setup("soulsilver", "platinum-us");
        var pichu = (PK4)Fixtures.Party("soulsilver", 1);
        pichu.Species = (ushort)Species.Pichu;
        pichu.Form = 1;
        pichu.RefreshChecksum();
        var record = BankPokemon.From(pichu, new BankSlot(1, 1), setup.Id("soulsilver"), Monday);
        setup.Bank.Pokemon.Add(record);

        var result = setup.Withdraw(record, "platinum-us");

        Assert.StartsWith("It can't go into Platinum:", result.Refusal);
        Assert.True(record.IsInBank);
    }

    [Fact]
    public void Withdrawing_into_a_taken_slot_or_a_full_party_is_refused()
    {
        var setup = new Setup("platinum-us");
        var record = Ok(setup.Import("platinum-us", new SaveSlot(1, 1))).Pokemon!;

        Assert.Equal("box 1 slot 2 isn't empty.", setup.Withdraw(record, "platinum-us", new SaveSlot(1, 2)).Refusal);
        Assert.Equal("The party is full.", setup.Withdraw(record, "platinum-us", new SaveSlot(null, 1)).Refusal);
        Assert.True(record.IsInBank);
    }

    [Fact]
    public void A_checked_out_Pokemon_cant_be_withdrawn_again()
    {
        var setup = new Setup("platinum-us", "emerald");
        var record = Ok(setup.Import("platinum-us", new SaveSlot(null, 1))).Pokemon!;
        Ok(setup.Withdraw(record, "emerald"));

        Assert.Equal("It's already out, in emerald.", setup.Withdraw(record, "platinum-us").Refusal);
    }

    [Fact]
    public void Coming_back_to_its_format_restores_what_only_that_format_holds()
    {
        // The Kirlia went to Emerald and levelled up there. Back in Platinum it keeps its progress and gets
        // its Platinum met data, held item and Sinnoh ribbons back.
        var setup = new Setup("platinum-us", "emerald");
        var kirlia = (PK4)Fixtures.Party("platinum-us", 1);
        kirlia.HeldItem = 112; // Griseous Orb: Gen 3 doesn't have it
        kirlia.RibbonChampionSinnoh = true;
        kirlia.RefreshChecksum();
        var record = BankPokemon.From(kirlia, new BankSlot(1, 1), setup.Id("platinum-us"), Monday);
        setup.Bank.Pokemon.Add(record);
        var inEmerald = (PK3)Ok(setup.Withdraw(record, "emerald")).Written!;
        Assert.Equal(0, inEmerald.HeldItem);

        // What #20's return step will do: the Pokémon as Emerald left it becomes the newest snapshot.
        var played = inEmerald.Clone();
        played.EXP = Experience.GetEXP(25, played.PersonalInfo.EXPGrowth);
        played.OriginalTrainerFriendship = 150;
        played.RibbonCountG3Cool = 1;
        played.RefreshChecksum();
        record.Keep(Snapshot.Of(played, setup.Id("emerald"), Tuesday.AddDays(1)));
        record.CheckedOut = null;

        var back = (PK4)Ok(setup.Withdraw(record, "platinum-us", null, Tuesday.AddDays(2))).Written!;

        Assert.Equal(25, back.CurrentLevel);
        Assert.Equal(150, back.OriginalTrainerFriendship);
        Assert.Equal(112, back.HeldItem);
        Assert.True(back.RibbonChampionSinnoh);
        Assert.True(back.RibbonG3Cool);
        Assert.Equal(kirlia.Version, back.Version);
        Assert.Equal(kirlia.MetLocation, back.MetLocation);
        Assert.Equal(kirlia.MetLevel, back.MetLevel);
        Assert.Equal(kirlia.MetDate, back.MetDate);
        Assert.Equal(kirlia.EggLocation, back.EggLocation);
        Assert.Equal(kirlia.Ball, back.Ball);
        Assert.Equal(kirlia.PID, back.PID);
        Assert.Equal(kirlia.Nickname, back.Nickname);
    }

    [Theory]
    [InlineData(true, 234)]   // kept Leftovers in Emerald → Leftovers
    [InlineData(false, 0)]    // the player took them in Emerald → nothing, not a second pair
    public void A_held_item_the_other_game_could_hold_follows_that_game(bool keptInEmerald, int expected)
    {
        var setup = new Setup("platinum-us", "emerald");
        var kirlia = (PK4)Fixtures.Party("platinum-us", 1);
        kirlia.HeldItem = 234; // Leftovers, in Gen 3 too
        kirlia.RefreshChecksum();
        var record = BankPokemon.From(kirlia, new BankSlot(1, 1), setup.Id("platinum-us"), Monday);
        setup.Bank.Pokemon.Add(record);
        var played = (PK3)((PK3)Ok(setup.Withdraw(record, "emerald")).Written!).Clone();
        if (!keptInEmerald)
            played.HeldItem = 0;
        record.Keep(Snapshot.Of(played, setup.Id("emerald"), Tuesday.AddDays(1)));
        record.CheckedOut = null;

        var back = Ok(setup.Withdraw(record, "platinum-us", null, Tuesday.AddDays(2))).Written!;

        Assert.Equal(expected, back.HeldItem);
    }

    [Fact]
    public void A_Gen_5_Pokemon_back_from_Gen_4_gets_its_hidden_ability_PID_and_nature_back()
    {
        var setup = new Setup("white", "platinum-us");
        var eevee = (PK5)Fixtures.Party("white", 1);
        eevee.Species = (ushort)Species.Eevee;
        eevee.Gender = EntityGender.GetFromPID(eevee.Species, eevee.PID);
        eevee.Nickname = "Eevee";
        eevee.IsNicknamed = false;
        eevee.RefreshAbility(2); // Anticipation, hidden
        eevee.RefreshChecksum();
        Assert.NotEqual(eevee.Nature, (Nature)(eevee.PID % 25));
        var record = BankPokemon.From(eevee, new BankSlot(1, 1), setup.Id("white"), Monday);
        setup.Bank.Pokemon.Add(record);

        var inPlatinum = Ok(setup.Withdraw(record, "platinum-us"));
        var pk4 = (PK4)inPlatinum.Written!;
        Assert.NotEqual(eevee.PID, pk4.PID);
        Assert.NotEqual(eevee.Ability, pk4.Ability);
        Assert.Contains(inPlatinum.Notes, n => n.Field == ChangedField.Ability);

        // It evolves into Espeon in Platinum, then comes back.
        var played = (PK4)pk4.Clone();
        played.Species = (ushort)Species.Espeon;
        played.RefreshAbility((int)(played.PID & 1));
        played.RefreshChecksum();
        record.Keep(Snapshot.Of(played, setup.Id("platinum-us"), Tuesday.AddDays(1)));
        record.CheckedOut = null;

        var back = (PK5)Ok(setup.Withdraw(record, "white", null, Tuesday.AddDays(2))).Written!;

        Assert.Equal((ushort)Species.Espeon, back.Species);
        Assert.Equal(eevee.PID, back.PID);
        Assert.Equal(eevee.Nature, back.Nature);
        Assert.True(back.HiddenAbility);
        Assert.Equal((int)Ability.MagicBounce, back.Ability); // Espeon's hidden ability
        Assert.Equal(eevee.Version, back.Version);
        Assert.Equal(eevee.MetLocation, back.MetLocation);
    }

    static TransferResult Ok(TransferResult result)
    {
        Assert.True(result.Succeeded, result.Refusal);
        return result;
    }

    static byte[] StoredBytes(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }
}
