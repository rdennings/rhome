using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>Changesets, backups, bank snapshots and in-place writes (issue #21), with in-memory storage.</summary>
public class ChangesetTests
{
    static readonly DateTimeOffset Monday = TestBank.Monday;

    sealed class MemorySave(byte[] bytes) : ISaveLocation
    {
        public byte[] Bytes { get; set; } = bytes;
        public int Writes { get; private set; }
        public Exception? FailNextWrite { get; set; }

        public byte[] Read() => Bytes.ToArray();

        public void WriteInPlace(ReadOnlySpan<byte> contents)
        {
            if (FailNextWrite is { } e)
            {
                FailNextWrite = null;
                throw e;
            }
            Assert.Equal(Bytes.Length, contents.Length);
            Bytes = contents.ToArray();
            Writes++;
        }
    }

    sealed class MemoryFile : IStoredFile
    {
        public byte[]? Bytes { get; set; }
        public bool FailWrites { get; set; }
        public bool Exists => Bytes is not null;
        public Stream OpenRead() => new MemoryStream(Bytes!);

        public void Replace(ReadOnlySpan<byte> contents)
        {
            if (FailWrites)
                throw new IOException("disk full");
            Bytes = contents.ToArray();
        }
    }

    sealed class MemoryBackups : IBackups
    {
        public List<(Guid Save, byte[] Bytes)> Saves { get; } = [];
        public List<byte[]> Banks { get; } = [];
        public void KeepSave(Guid saveId, ReadOnlySpan<byte> contents, DateTimeOffset when) => Saves.Add((saveId, contents.ToArray()));
        public void KeepBank(ReadOnlySpan<byte> contents, DateTimeOffset when) => Banks.Add(contents.ToArray());
    }

    /// <summary>A stored bank that knows the given fixture saves, each in a memory save.</summary>
    sealed class World
    {
        public MemoryFile BankFile { get; } = new();
        public MemoryBackups Backups { get; } = new();
        public Dictionary<string, (Guid Id, MemorySave Save)> Saves { get; } = [];
        public JsonBankStore Store { get; }
        public ChangesetRunner Runner { get; }

        public World(params string[] fixtures)
        {
            Store = new JsonBankStore(BankFile);
            var bank = new Bank();
            foreach (var name in fixtures)
            {
                var bytes = Fixtures.Bytes(name);
                var save = SaveDetector.Detect(bytes)!.Save;
                var id = Guid.NewGuid();
                bank.Saves.Add(new BankSave(id, SaveDetector.GameOf(save), new TrainerInfo(save.OT, save.TID16, save.SID16), name, null));
                Saves[name] = (id, new MemorySave(bytes));
            }
            Store.Save(bank);
            Runner = new ChangesetRunner(Store, id => Saves.Values.FirstOrDefault(s => s.Id == id).Save, Backups);
        }

        public Guid Id(string name) => Saves[name].Id;
        public MemorySave Save(string name) => Saves[name].Save;
        public Bank Bank => Store.Load();

        public Changeset Changeset(params string[] open)
        {
            var changeset = new Changeset();
            foreach (var name in open)
                changeset.Opened(Id(name), Save(name).Read());
            return changeset;
        }

        public Snapshot Files() => new(BankFile.Bytes!.ToArray(), Saves.ToDictionary(s => s.Key, s => s.Value.Save.Bytes.ToArray()));
    }

    sealed record Snapshot(byte[] Bank, Dictionary<string, byte[]> Saves)
    {
        public void AssertSameAs(World world)
        {
            Assert.Equal(Bank, world.BankFile.Bytes);
            foreach (var (name, bytes) in Saves)
                Assert.Equal(bytes, world.Save(name).Bytes);
        }
    }

    static SaveFile Read(MemorySave save) => SaveDetector.Detect(save.Bytes)!.Save;

    [Fact]
    public void A_preview_says_what_would_happen_and_writes_nothing()
    {
        var world = new World("platinum-us", "emerald");
        var before = world.Files();
        var changeset = world.Changeset("platinum-us");
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(null, 1)));

        var preview = world.Runner.Preview(changeset, Monday);

        Assert.True(preview.Succeeded, preview.Refusal);
        Assert.False(preview.Written);
        Assert.Equal("Import Kirlia from platinum-us, party slot 1, to bank box 1 slot 1", Assert.Single(preview.Outcomes).Description);
        before.AssertSameAs(world);
        Assert.Empty(world.Backups.Saves);
        Assert.Empty(world.Backups.Banks);
    }

    [Fact]
    public void Applying_writes_every_save_in_place_and_the_bank_after_backing_them_up()
    {
        var world = new World("platinum-us", "emerald");
        var originals = world.Files();
        var first = world.Changeset("platinum-us");
        first.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(null, 1)));
        Assert.True(world.Runner.Apply(first, Monday).Written);
        var kirlia = Assert.Single(world.Bank.Pokemon);

        var second = world.Changeset("emerald");
        second.Add(new WithdrawChange(kirlia.Id, world.Id("emerald")));
        var result = world.Runner.Apply(second, Monday.AddHours(1));

        Assert.True(result.Written, result.Refusal);
        Assert.Contains(result.Lines, l => l.Contains("Lucky Chant", StringComparison.Ordinal));
        Assert.Equal((ushort)Species.Kirlia, Read(world.Save("emerald")).GetBoxSlotAtIndex(1).Species);
        Assert.Equal(5, Read(world.Save("platinum-us")).PartyCount);
        Assert.Equal(1, world.Save("emerald").Writes);

        var bank = world.Bank;
        Assert.False(Assert.Single(bank.Pokemon).IsInBank);
        Assert.Equal(SaveHash.Of(world.Save("emerald").Bytes), bank.Saves.Single(s => s.Id == world.Id("emerald")).LastSeenHash);

        // Each save was backed up as it was before its write, and the bank before each apply.
        Assert.Equal([world.Id("platinum-us"), world.Id("emerald")], world.Backups.Saves.Select(b => b.Save));
        Assert.Equal(originals.Saves["platinum-us"], world.Backups.Saves[0].Bytes);
        Assert.Equal(originals.Saves["emerald"], world.Backups.Saves[1].Bytes);
        Assert.Equal(2, world.Backups.Banks.Count);
        Assert.Equal(originals.Bank, world.Backups.Banks[0]);
    }

    [Fact]
    public void One_change_that_cant_be_done_means_nothing_is_written()
    {
        var world = new World("platinum-us", "emerald");
        var before = world.Files();
        var changeset = world.Changeset("platinum-us", "emerald");
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(null, 1)));
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 5))); // empty

        var result = world.Runner.Apply(changeset, Monday);

        Assert.False(result.Succeeded);
        Assert.Equal(changeset.Changes[1], result.Failed);
        Assert.Single(result.Outcomes);
        before.AssertSameAs(world);
        Assert.Empty(world.Backups.Saves);
    }

    [Fact]
    public void A_write_that_fails_halfway_puts_back_what_was_written()
    {
        var world = new World("platinum-us", "emerald");
        var first = world.Changeset("platinum-us");
        first.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(null, 1)));
        first.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(null, 1)));
        world.Runner.Apply(first, Monday);
        var (kirlia, monferno) = (world.Bank.Pokemon[0], world.Bank.Pokemon[1]);
        var before = world.Files();

        var changeset = world.Changeset("platinum-us", "emerald");
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3)));
        changeset.Add(new WithdrawChange(kirlia.Id, world.Id("emerald")));
        world.Save("emerald").FailNextWrite = new IOException("card removed");

        var e = Assert.Throws<ChangesetWriteException>(() => world.Runner.Apply(changeset, Monday.AddHours(1)));

        Assert.Contains("put back", e.Message);
        before.AssertSameAs(world);
        Assert.Equal(3, world.Save("platinum-us").Writes); // the first apply, this one's write, then putting it back
        Assert.NotEmpty(world.Backups.Saves);
        Assert.True(world.Bank.Pokemon.Single(p => p.Id == monferno.Id).IsInBank);
    }

    [Fact]
    public void A_bank_that_cant_be_written_puts_the_saves_back()
    {
        var world = new World("platinum-us");
        var before = world.Files();
        var changeset = world.Changeset("platinum-us");
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3)));
        world.BankFile.FailWrites = true;

        Assert.Throws<ChangesetWriteException>(() => world.Runner.Apply(changeset, Monday));

        before.AssertSameAs(world);
    }

    [Fact]
    public void A_save_that_changed_since_it_was_opened_is_refused()
    {
        var world = new World("platinum-us");
        var changeset = world.Changeset("platinum-us");
        changeset.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3)));
        world.Save("platinum-us").Bytes[0x10] ^= 0xFF; // the game was played and saved
        var before = world.Files();

        var result = world.Runner.Apply(changeset, Monday);

        Assert.Contains("changed since it was opened", result.Refusal);
        before.AssertSameAs(world);
    }

    [Fact]
    public void Staging_a_change_to_a_save_that_wasnt_opened_is_a_mistake()
    {
        var world = new World("platinum-us");

        Assert.Throws<InvalidOperationException>(() =>
            new Changeset().Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3))));
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
    public void Cut_short_blank_or_random_saves_are_refused_and_never_written(string fixture)
    {
        var original = Fixtures.Bytes(fixture);
        var random = new byte[original.Length];
        new Random(42).NextBytes(random);
        byte[][] broken = [original[..^1], original[..(original.Length / 2)], new byte[original.Length], random];

        foreach (var bytes in broken)
        {
            var world = new World(fixture);
            world.Save(fixture).Bytes = bytes;
            var changeset = world.Changeset(fixture);
            changeset.Add(new ImportChange(world.Id(fixture), new SaveSlot(1, 1)));

            var result = world.Runner.Apply(changeset, Monday);

            Assert.False(result.Succeeded);
            Assert.Contains("isn't a save rhome recognizes", result.Refusal);
            Assert.Equal(0, world.Save(fixture).Writes);
            Assert.Empty(world.Backups.Saves);
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Damaged_saves_are_refused_and_nothing_crashes(string fixture)
    {
        // PLAN.md §6.6. Flip bytes at random: a save that no longer passes its checksums is refused; one that still
        // does (the byte was outside every checked block) is a valid save as far as the game can tell.
        var original = Fixtures.Bytes(fixture);
        var rng = new Random(fixture.GetHashCode(StringComparison.Ordinal));
        var refused = 0;
        for (var round = 0; round < 40; round++)
        {
            var bytes = original.ToArray();
            bytes[rng.Next(bytes.Length)] ^= (byte)rng.Next(1, 256);
            var world = new World(fixture);
            world.Save(fixture).Bytes = bytes;
            var changeset = world.Changeset(fixture);
            changeset.Add(new MoveInBankChange(Guid.NewGuid(), new BankSlot(1, 2))); // touches no save
            changeset.Add(new ImportChange(world.Id(fixture), new SaveSlot(null, 1), How: ImportAs.New));

            var detected = SaveDetector.Detect(bytes);
            var result = world.Runner.Preview(changeset, Monday);

            if (detected is null || !detected.Save.ChecksumsValid)
            {
                Assert.False(result.Succeeded);
                Assert.Null(result.Failed); // refused for the save as a whole, before any change ran
                refused++;
            }
        }
        Assert.NotEqual(0, refused);
    }

    [Fact]
    public void Moving_within_the_bank_swaps_with_whatever_is_there()
    {
        var world = new World("platinum-us");
        var import = world.Changeset("platinum-us");
        import.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 1)));
        import.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 2)));
        world.Runner.Apply(import, Monday);
        var (buneary, bidoof) = (world.Bank.Pokemon[0], world.Bank.Pokemon[1]);

        var move = new Changeset();
        move.Add(new MoveInBankChange(buneary.Id, new BankSlot(1, 2)));
        move.Add(new MoveInBankChange(bidoof.Id, new BankSlot(3, 7)));
        var result = world.Runner.Apply(move, Monday.AddHours(1));

        Assert.True(result.Written, result.Refusal);
        Assert.Equal(["Swap Buneary (bank box 1 slot 1) with Bidoof (bank box 1 slot 2)", "Move Bidoof to bank box 3 slot 7"],
            result.Outcomes.Select(o => o.Description));
        var bank = world.Bank;
        Assert.Equal(new BankSlot(1, 2), bank.Pokemon.Single(p => p.Id == buneary.Id).Slot);
        Assert.Equal(new BankSlot(3, 7), bank.Pokemon.Single(p => p.Id == bidoof.Id).Slot);
    }

    [Fact]
    public void An_import_that_needs_an_answer_stops_the_changeset_with_the_candidates()
    {
        var world = new World("platinum-us");
        var first = world.Changeset("platinum-us");
        first.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3)));
        world.Runner.Apply(first, Monday);
        // Put the same Gastly back into the save by hand, as a copy.
        var bank = world.Bank;
        var save = Read(world.Save("platinum-us"));
        save.SetBoxSlotAtIndex(bank.Pokemon[0].Newest.ToPkm(), 2, EntityImportSettings.None);
        world.Save("platinum-us").Bytes = save.Write().ToArray();

        var again = world.Changeset("platinum-us");
        again.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 3)));
        var result = world.Runner.Apply(again, Monday.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Equal(bank.Pokemon[0].Id, Assert.Single(result.Candidates).Id);
    }
}
