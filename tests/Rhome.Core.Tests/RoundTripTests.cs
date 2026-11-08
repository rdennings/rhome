using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>
/// Automated round trips proving nothing is lost (issue #22, PLAN.md §6.2–6.3): M1's exit test. Each trip goes
/// through the whole pipeline (changesets on save files in memory, so every step is written and read back as
/// bytes): game A → bank → game B, where it makes progress → bank → game A. What comes home must equal what left,
/// plus the progress.
/// </summary>
public class RoundTripTests
{
    static readonly DateTimeOffset Start = TestBank.Monday;

    /// <summary>
    /// Every fixture can receive; every one with a Pokémon of its own can send. Read from fixtures/, so each Tier 1
    /// game added there (#4) joins every pair on its own.
    /// </summary>
    static readonly string[] Receivers = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.sav")
        .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).ToArray()!;
    static readonly string[] Senders = Receivers
        .Where(f => SaveContents.Party(Fixtures.Load(f)).Count + SaveContents.Boxes(Fixtures.Load(f)).Count > 0).ToArray();

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var a in Senders)
            foreach (var b in Receivers.Where(b => b != a))
                data.Add(a, b);
        return data;
    }

    /// <summary>
    /// The Pokémon that travels: the first of A's own that can exist in B, put in A's first empty box slot (a
    /// party's last Pokémon can't leave). When A has none (an Unova species going to Gen 3 or 4, say), its first
    /// Pokémon turned into a Kirlia, which every Tier 1 game has: everything but the species is what A's game wrote.
    /// </summary>
    static PKM Traveller(SaveFile a, SaveFile b)
    {
        var maxSpecies = Math.Min(a.MaxSpeciesID, b.MaxSpeciesID);
        var own = SaveContents.Party(a).Concat(SaveContents.Boxes(a)).Select(s => s.Pokemon).ToList();
        var pk = own.FirstOrDefault(p => p.Species <= maxSpecies && !p.IsEgg)?.Clone();
        if (pk is null)
        {
            pk = own[0].Clone();
            pk.Species = (ushort)Species.Kirlia;
            pk.Gender = EntityGender.GetFromPID(pk.Species, pk.PID);
            pk.RefreshAbility(pk is PK5 ? (int)((pk.PID >> 16) & 1) : (int)(pk.PID & 1));
            pk.IsNicknamed = false;
            pk.Nickname = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
        }
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return pk;
    }

    /// <summary>Play in game B: three more levels (or to 100), a new move in place of the last one, more friendship.</summary>
    static void MakeProgress(PKM pk, int maxMove)
    {
        var level = (byte)Math.Min(100, pk.CurrentLevel + 3);
        pk.EXP = Experience.GetEXP(level, pk.PersonalInfo.EXPGrowth);
        var newMove = new ushort[] { (ushort)Move.Protect, (ushort)Move.Rest, (ushort)Move.Toxic }
            .First(m => m <= maxMove && !pk.HasMove(m));
        var last = Enumerable.Range(0, 4).Last(i => pk.GetMove(i) != 0 || i == 0);
        var slot = Enumerable.Range(0, 4).FirstOrDefault(i => pk.GetMove(i) == 0, last);
        pk.SetMove(slot, newMove);
        switch (slot)
        {
            case 0: pk.Move1_PPUps = 0; break;
            case 1: pk.Move2_PPUps = 0; break;
            case 2: pk.Move3_PPUps = 0; break;
            default: pk.Move4_PPUps = 0; break;
        }
        pk.HealPPIndex(slot);
        pk.OriginalTrainerFriendship = (byte)Math.Min(255, pk.OriginalTrainerFriendship + 25);
        pk.RefreshChecksum();
    }

    /// <summary>What progress in B changes, and what's worked out from those (stats, level).</summary>
    static readonly HashSet<string> Progress =
    [
        "EXP", "CurrentLevel", "Stat_Level", "OriginalTrainerFriendship", "CurrentFriendship",
        "Move1", "Move2", "Move3", "Move4", "Move1_PP", "Move2_PP", "Move3_PP", "Move4_PP",
        "Move1_PPUps", "Move2_PPUps", "Move3_PPUps", "Move4_PPUps", "MoveCount",
        "Stat_HPCurrent", "Stat_HPMax", "Stat_ATK", "Stat_DEF", "Stat_SPA", "Stat_SPD", "Stat_SPE",
    ];

    static PKM AtIndex(SaveFile save, int index) => save.GetBoxSlotAtIndex(index);

    static SaveFile Read(MemorySave save) => SaveDetector.Detect(save.Bytes)!.Save;

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_Pokemon_goes_from_A_to_B_and_back_with_its_progress_and_nothing_lost(string a, string b)
    {
        var world = new World(a, b);

        // Set up: the traveller in A's first empty box slot, as if A's game had put it there.
        var saveA = Read(world.Save(a));
        var start = saveA.NextOpenBoxSlot();
        var original = Traveller(saveA, Read(world.Save(b)));
        saveA.SetBoxSlotAtIndex(original, start, EntityImportSettings.None);
        world.Save(a).Bytes = saveA.Write().ToArray();
        var fromSlot = new SaveSlot(start / saveA.BoxSlotCount + 1, start % saveA.BoxSlotCount + 1);

        // A → bank → B.
        Apply(world, Start, a, new ImportChange(world.Id(a), fromSlot));
        var record = Assert.Single(world.Bank.Pokemon);
        var there = Apply(world, Start.AddHours(1), b, new WithdrawChange(record.Id, world.Id(b)));
        var slotInB = ((WithdrawChange)there.Outcomes[0].Change, Assert.Single(world.Bank.Pokemon).CheckedOut!.Slot).Item2;

        // Progress in B, saved by B's game.
        var saveB = Read(world.Save(b));
        var indexB = (slotInB.Box!.Value - 1) * saveB.BoxSlotCount + slotInB.Slot - 1;
        var inB = AtIndex(saveB, indexB);
        MakeProgress(inB, saveB.MaxMoveID);
        saveB.SetBoxSlotAtIndex(inB, indexB, EntityImportSettings.None);
        world.Save(b).Bytes = saveB.Write().ToArray();

        // B → bank (recognized, not a new record) → A.
        var back = Apply(world, Start.AddHours(2), b, new ImportChange(world.Id(b), slotInB));
        Assert.StartsWith("Return ", back.Outcomes[0].Description);
        Apply(world, Start.AddHours(3), a, new WithdrawChange(record.Id, world.Id(a), fromSlot));
        var home = AtIndex(Read(world.Save(a)), start);

        // The progress it made in B…
        Assert.Equal(inB.CurrentLevel, home.CurrentLevel);
        Assert.Equal(inB.EXP, home.EXP);
        Assert.Equal(inB.OriginalTrainerFriendship, home.OriginalTrainerFriendship);
        Assert.Equal(Rhome.Core.Moveset.Of(inB), Rhome.Core.Moveset.Of(home));
        // …and everything else exactly as it left.
        var lost = PkmFields.Differences(home, original).Where(d => !Progress.Contains(d.Split(':')[0]));
        Assert.Empty(lost);
        Assert.True(home.ChecksumValid);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_Pokemon_that_made_no_progress_comes_home_unchanged_when_its_moves_all_exist_in_B(string a, string b)
    {
        var world = new World(a, b);
        var saveA = Read(world.Save(a));
        var start = saveA.NextOpenBoxSlot();
        var original = Traveller(saveA, Read(world.Save(b)));
        var maxMoveB = Read(world.Save(b)).MaxMoveID;
        if (Enumerable.Range(0, 4).Any(i => original.GetMove(i) > maxMoveB))
            return; // it loses a move in B, and comes home with its newest moveset; the test above covers that
        saveA.SetBoxSlotAtIndex(original, start, EntityImportSettings.None);
        world.Save(a).Bytes = saveA.Write().ToArray();
        var fromSlot = new SaveSlot(start / saveA.BoxSlotCount + 1, start % saveA.BoxSlotCount + 1);

        Apply(world, Start, a, new ImportChange(world.Id(a), fromSlot));
        var record = Assert.Single(world.Bank.Pokemon);
        Apply(world, Start.AddHours(1), b, new WithdrawChange(record.Id, world.Id(b)));
        Apply(world, Start.AddHours(2), b, new ImportChange(world.Id(b), world.Bank.Pokemon[0].CheckedOut!.Slot));
        Apply(world, Start.AddHours(3), a, new WithdrawChange(record.Id, world.Id(a), fromSlot));

        var lost = PkmFields.Differences(AtIndex(Read(world.Save(a)), start), original)
            .Where(d => !d.StartsWith("Stat_", StringComparison.Ordinal)).ToList();
        Assert.Empty(lost);
    }

    [Fact]
    public void M1_exit_Platinum_to_Emerald_and_back()
    {
        // Spelled out for the milestone: the Kirlia the prototype once moved by hand.
        A_Pokemon_goes_from_A_to_B_and_back_with_its_progress_and_nothing_lost("platinum-us", "emerald");
    }

    [Fact]
    public void M1_exit_White_to_Platinum_and_back()
    {
        // White stands in for Black: it's the Gen 5 fixture there is.
        A_Pokemon_goes_from_A_to_B_and_back_with_its_progress_and_nothing_lost("white", "platinum-us");
    }

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var name in Receivers)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Every_box_Pokemon_out_and_back_through_changesets_leaves_the_file_byte_identical(string fixture)
    {
        // PLAN.md §6.2 through the whole pipeline: the no-op round trip with every write going to the file.
        var world = new World(fixture);
        var original = world.Save(fixture).Bytes.ToArray();
        var slots = SaveContents.Boxes(Read(world.Save(fixture))).Select(s => s.Where).ToList();

        var import = world.Changeset(fixture);
        foreach (var slot in slots)
            import.Add(new ImportChange(world.Id(fixture), slot));
        Assert.True(world.Runner.Apply(import, Start).Succeeded);
        var withdraw = world.Changeset(fixture);
        foreach (var (pokemon, slot) in world.Bank.Pokemon.Zip(slots))
            withdraw.Add(new WithdrawChange(pokemon.Id, world.Id(fixture), slot));
        Assert.True(world.Runner.Apply(withdraw, Start.AddHours(1)).Succeeded);

        Assert.Equal(original, world.Save(fixture).Bytes);
    }

    static ChangesetResult Apply(World world, DateTimeOffset when, string save, Change change)
    {
        var changeset = world.Changeset(save);
        changeset.Add(change);
        var result = world.Runner.Apply(changeset, when);
        Assert.True(result.Written, result.Refusal);
        return result;
    }
}
