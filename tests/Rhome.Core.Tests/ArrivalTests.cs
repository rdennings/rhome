using System.Runtime.CompilerServices;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>Arrival bookkeeping per game (issue #19, src/Rhome.Core/Arrival/CHECKLIST.md).</summary>
public class ArrivalTests
{
    static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(-5));

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.sav").Order())
            data.Add(Path.GetFileNameWithoutExtension(path));
        return data;
    }

    /// <summary>A Pokémon in the save's own format of a species the save has never seen, made from one of its own.</summary>
    static PKM Unseen(SaveFile save)
    {
        var pk = save.BlankPKM;
        var donor = SaveContents.Party(save).Concat(SaveContents.Boxes(save)).FirstOrDefault()?.Pokemon;
        if (donor is not null)
            pk = donor.Clone();
        else
        {
            pk.Species = 1;
            pk.Language = save.Language;
            pk.OriginalTrainerName = save.OT;
            pk.ID32 = save.ID32;
            pk.SetMoves([(ushort)Move.Tackle]);
            pk.Version = save.Version;
        }
        var species = Enumerable.Range(1, save.MaxSpeciesID).Select(i => (ushort)i)
            .First(s => !save.GetSeen(s) && !save.Personal[s].HasForms && s is not (ushort)Species.Unown and not (ushort)Species.Spinda);
        return WithSpecies(pk, species);
    }

    static PKM WithSpecies(PKM pk, ushort species)
    {
        pk = pk.Clone();
        pk.Species = species;
        pk.Form = 0;
        pk.Gender = EntityGender.GetFromPID(species, pk.PID);
        pk.IsNicknamed = false;
        pk.RefreshAbility(0);
        pk.RefreshChecksum();
        return pk;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void A_new_species_is_registered_as_owned_and_seen(string fixture)
    {
        var save = Fixtures.Load(fixture);
        var pk = Unseen(save);
        Assert.False(save.GetCaught(pk.Species));

        var changes = Arrival.Record(save, pk);

        Assert.True(save.GetCaught(pk.Species));
        Assert.True(save.GetSeen(pk.Species));
        Assert.Equal([$"Pokédex: {EnglishNames.Species(pk.Species)} is registered as seen and owned."], changes);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Nothing_but_the_Pokedex_changes(string fixture)
    {
        var save = Fixtures.Load(fixture);
        var pk = Unseen(save);
        var before = Buffers(save).Select(b => b.Data.ToArray()).ToList();

        Arrival.Record(save, pk);

        var buffers = Buffers(save);
        var changed = 0;
        for (var i = 0; i < buffers.Count; i++)
        {
            var (name, data, dex) = buffers[i];
            for (var at = 0; at < data.Length; at++)
            {
                if (data[at] == before[i][at])
                    continue;
                changed++;
                Assert.True(dex.Any(r => at >= r.Start && at < r.End), $"{name} byte 0x{at:X} changed outside the Pokédex");
            }
        }
        Assert.NotEqual(0, changed);
    }

    [Fact]
    public void Gen_3_sets_seen_in_all_three_copies()
    {
        // The prototype's Emerald fix: the game only counts a species as seen when all three copies agree.
        var save = (SAV3)Fixtures.Load("emerald");
        var pk = Unseen(save);
        var (bit, at) = (1 << ((pk.Species - 1) & 7), (pk.Species - 1) >> 3);

        Arrival.Record(save, pk);

        Assert.NotEqual(0, save.Small[0x18 + 0x10 + at] & bit); // owned
        Assert.NotEqual(0, save.Small[0x18 + 0x44 + at] & bit); // seen
        Assert.NotEqual(0, save.Large[0x988 + at] & bit);       // seen, copy 2
        Assert.NotEqual(0, save.Large[0x3B24 + at] & bit);      // seen, copy 3
    }

    [Theory]
    [InlineData(Species.Unown)]
    [InlineData(Species.Spinda)]
    public void Gen_3_remembers_the_first_Unown_and_Spinda_seen(Species species)
    {
        var save = (SAV3)Fixtures.Load("emerald");
        var first = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)species);
        var second = first.Clone();
        second.PID ^= 0x01010101;

        var changes = Arrival.Record(save, first);
        Arrival.Record(save, second);

        var remembered = species == Species.Unown ? save.SmallBlock.DexPIDUnown : save.SmallBlock.DexPIDSpinda;
        Assert.Equal(first.PID, remembered);
        Assert.Contains(changes, c => c.Contains($"first {species}", StringComparison.Ordinal));
    }

    [Fact]
    public void Gen_4_remembers_the_first_Spinda_seen_which_PKHeX_leaves_out()
    {
        var save = (SAV4)Fixtures.Load("platinum-us");
        var first = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)Species.Spinda);
        var second = first.Clone();
        second.PID ^= 0x01010101;

        Arrival.Record(save, first);
        Arrival.Record(save, second);

        Assert.Equal(first.PID, save.Dex.SpindaPID);
    }

    [Fact]
    public void Gen_5_remembers_the_first_Spinda_seen()
    {
        var save = (SAV5)Fixtures.Load("white");
        var spinda = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)Species.Spinda);

        Arrival.Record(save, spinda);

        Assert.Equal(spinda.PID, save.Zukan.Spinda);
    }

    [Fact]
    public void Gen_4_records_the_language_seen()
    {
        var save = (SAV4)Fixtures.Load("platinum-us");
        var pk = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)Species.Bidoof);
        pk.Language = (int)LanguageID.French;
        pk.RefreshChecksum();

        Arrival.Record(save, pk);

        Assert.True(save.Dex.GetLanguageBitIndex(pk.Species, Zukan4.GetGen4LanguageBitIndex((int)LanguageID.French)));
    }

    [Theory]
    // Shellos's byte at 0x108 of the Pokédex, 0xFF in a new game. Bit 0 is the first form seen, bit 1 the second;
    // the first form seen goes into both (pret/pokeplatinum src/pokedex.c, UpdateForms_TwoForms).
    [InlineData(new byte[] { 0 }, 0xFC)]        // West only
    [InlineData(new byte[] { 1 }, 0xFF)]        // East only
    [InlineData(new byte[] { 0, 1 }, 0xFE)]     // West, then East
    [InlineData(new byte[] { 1, 0 }, 0xFD)]     // East, then West
    [InlineData(new byte[] { 0, 0, 1, 1 }, 0xFE)]
    public void Gen_4_records_two_form_species_the_way_the_game_does(byte[] forms, int expected)
    {
        var save = (SAV4)Fixtures.Load("platinum-us");
        Assert.False(save.Dex.GetSeen((ushort)Species.Shellos));
        Assert.Equal(0xFF, save.Dex.Data[0x108]);
        var shellos = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)Species.Shellos);

        foreach (var form in forms)
        {
            shellos.Form = form;
            Arrival.Record(save, shellos);
        }

        Assert.Equal(expected, save.Dex.Data[0x108]);
    }

    [Fact]
    public void Gen_4_records_Unown_letters_in_the_order_seen()
    {
        var save = (SAV4)Fixtures.Load("platinum-us");
        var unown = WithSpecies(SaveContents.Party(save)[0].Pokemon, (ushort)Species.Unown);

        foreach (var letter in new byte[] { 7, 2 })
        {
            unown.Form = letter;
            Arrival.Record(save, unown);
        }

        Assert.Equal([7, 2, 0xFF], save.Dex.GetForms((ushort)Species.Unown).Take(3));
    }

    [Fact]
    public void A_species_already_owned_changes_nothing_worth_saying()
    {
        var save = Fixtures.Load("platinum-us");

        Assert.Empty(Arrival.Record(save, SaveContents.Party(save)[0].Pokemon));
    }

    [Fact]
    public void Eggs_arent_registered()
    {
        var save = Fixtures.Load("platinum-us");
        var egg = Unseen(save);
        egg.IsEgg = true;

        Assert.Empty(Arrival.Record(save, egg));
        Assert.False(save.GetSeen(egg.Species));
    }

    [Fact]
    public void An_egg_from_another_trainer_is_marked_as_traded()
    {
        var save = Fixtures.Load("platinum-us");
        var egg = (PK4)Unseen(save);
        egg.IsEgg = true;
        egg.ID32 ^= 0x12345678;
        egg.MetLocation = Locations.Daycare4;
        egg.RefreshChecksum();

        var changes = Arrival.Adapt(save, egg);

        Assert.Equal(Locations.LinkTrade4, egg.MetLocation);
        Assert.Single(changes);
    }

    [Fact]
    public void Withdrawing_does_the_bookkeeping_and_reports_it()
    {
        var save = Fixtures.Load("emerald");
        var bank = new Bank();
        var record = new BankSave(Guid.NewGuid(), GameVersion.E, new TrainerInfo(save.OT, save.TID16, save.SID16), "Emerald", null);
        bank.Saves.Add(record);
        var pk = Unseen(save);
        var pokemon = BankPokemon.From(pk, new BankSlot(1, 1), record.Id, Monday);
        bank.Pokemon.Add(pokemon);

        var result = Transfers.Withdraw(bank, pokemon.Id, save, record.Id, Monday);

        Assert.True(result.Succeeded, result.Refusal);
        Assert.True(save.GetCaught(pk.Species));
        Assert.Single(result.Bookkeeping);
    }

    /// <summary>The save's memory, and where its Pokédex lives in each part.</summary>
    static List<(string Name, byte[] Data, (int Start, int End)[] Dex)> Buffers(SaveFile save) => save switch
    {
        SAV3 s => [
            ("small block", s.Small.ToArray(), [(0x18, 0x18 + 0x44 + 0x34)]),
            ("large block", s.Large.ToArray(), [Range(SeenCopy(s, 2), 0x34), Range(SeenCopy(s, 3), 0x34)]),
            ("box storage", s.Storage.ToArray(), []),
        ],
        SAV4 s => [("save", s.Data.ToArray(), [Range(Offset(s.Data, s.Dex.Data), s.Dex.Data.Length)])],
        SAV5 s => [("save", s.Data.ToArray(), [Range(Offset(s.Data, s.Zukan.Data), s.Zukan.Data.Length)])],
        _ => throw new NotSupportedException(),
    };

    // Where the second and third seen copies are in each Gen 3 game's large block (CHECKLIST.md).
    static int SeenCopy(SAV3 save, int copy) => (save, copy) switch
    {
        (SAV3E, 2) => 0x988,
        (SAV3E, _) => 0x3B24,
        (SAV3RS, 2) => 0x938,
        (SAV3RS, _) => 0x3A8C,
        (_, 2) => 0x5F8,
        _ => 0x3A18,
    };

    static (int, int) Range(int start, int length) => (start, start + length);

    static int Offset(Span<byte> outer, Span<byte> inner) =>
        (int)Unsafe.ByteOffset(ref outer[0], ref inner[0]);
}
