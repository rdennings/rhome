using System.Text;
using System.Text.Json.Nodes;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>The bank's data model and its JSON format (issue #15).</summary>
public class BankTests
{
    static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(-5));

    static SaveFile Load(string name) =>
        SaveDetector.Detect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav")))!.Save;

    static BankSave SaveRecord(SaveFile save, string name) =>
        new(Guid.NewGuid(), save.Version, new TrainerInfo(save.OT, save.TID16, save.SID16), name, "ab12");

    /// <summary>A bank holding one Pokémon from each generation, one of them checked out, and a renamed box.</summary>
    static Bank SampleBank()
    {
        var bank = new Bank();
        bank.Boxes.Box(2)!.Name = "Keepers";

        foreach (var (fixture, slot) in new[] { ("emerald", 1), ("platinum-us", 2), ("white", 3) })
        {
            var save = Load(fixture);
            var record = SaveRecord(save, fixture);
            bank.Saves.Add(record);
            var pk = SaveContents.Party(save)[0].Pokemon;
            bank.Pokemon.Add(BankPokemon.From(pk, new BankSlot(1, slot), record.Id, Monday));
        }

        bank.Pokemon[2].CheckedOut = new CheckOut(bank.Saves[2].Id, new SaveSlot(3, 7));
        return bank;
    }

    static Bank RoundTrip(Bank bank) => BankJson.Read(new MemoryStream(BankJson.ToBytes(bank)));

    static Bank ReadJson(string json) => BankJson.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static JsonObject AsJson(Bank bank) => JsonNode.Parse(BankJson.ToBytes(bank))!.AsObject();

    [Fact]
    public void A_saved_bank_reloads_as_the_same_bank()
    {
        var bank = SampleBank();
        var reloaded = RoundTrip(bank);

        Assert.Equal(BankJson.ToBytes(bank), BankJson.ToBytes(reloaded));
        Assert.Equal("Keepers", reloaded.Boxes.Boxes[1].Name);
        Assert.Equal(bank.Saves, reloaded.Saves);
        for (var i = 0; i < bank.Pokemon.Count; i++)
        {
            var (before, after) = (bank.Pokemon[i], reloaded.Pokemon[i]);
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Slot, after.Slot);
            Assert.Equal(before.CheckedOut, after.CheckedOut);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Equal(before.Summary, after.Summary);
            Assert.Equal(before.Snapshots, after.Snapshots);
        }
        Assert.False(reloaded.Pokemon[2].IsInBank);
    }

    [Theory]
    [InlineData("emerald", "PK3")]
    [InlineData("platinum-us", "PK4")]
    [InlineData("white", "PK5")]
    public void A_snapshot_gives_back_the_Pokemon_byte_for_byte(string fixture, string format)
    {
        var pk = SaveContents.Party(Load(fixture))[0].Pokemon;
        var snapshot = Snapshot.Of(pk, null, Monday);

        var back = snapshot.ToPkm();

        Assert.Equal(format, snapshot.Format);
        Assert.Equal(pk.GetType(), back.GetType());
        Assert.Equal(pk.PID, back.PID);
        Assert.Equal(pk.Nickname, back.Nickname);
        Assert.Equal(snapshot.Data, Snapshot.Of(back, null, Monday).Data);
    }

    [Theory]
    [InlineData("emerald")]
    [InlineData("platinum-us")]
    [InlineData("white")]
    public void The_summary_lists_the_moves_in_slot_order(string fixture)
    {
        var pk = SaveContents.Party(Load(fixture))[0].Pokemon;

        var moves = PokemonSummary.Of(pk).Moves;

        Assert.Equal(new Moveset(pk.Move1, pk.Move2, pk.Move3, pk.Move4), moves);
        Assert.NotEqual(0, moves.Move1);
    }

    [Fact]
    public void Snapshot_bytes_are_base64_in_the_JSON()
    {
        var bank = SampleBank();
        var snapshot = bank.Pokemon[0].Snapshots.Single();

        var json = AsJson(bank)["pokemon"]![0]!["snapshots"]![0]!;

        Assert.Equal(Convert.ToBase64String(snapshot.Data), json["data"]!.GetValue<string>());
        Assert.Equal("PK3", json["format"]!.GetValue<string>());
    }

    [Fact]
    public void The_JSON_starts_with_its_format_and_version()
    {
        var json = AsJson(new Bank());

        Assert.Equal(["format", "version", "boxes", "saves", "pokemon"], json.Select(p => p.Key));
        Assert.Equal(BankJson.FormatName, json["format"]!.GetValue<string>());
        Assert.Equal(BankJson.CurrentVersion, json["version"]!.GetValue<int>());
    }

    [Fact]
    public void A_save_record_holds_no_location()
    {
        var save = AsJson(SampleBank())["saves"]![0]!.AsObject();

        Assert.Equal(["id", "game", "trainer", "displayName", "lastSeenHash"], save.Select(p => p.Key));
    }

    [Fact]
    public void Keeping_a_snapshot_replaces_the_one_in_the_same_format()
    {
        var pokemon = SampleBank().Pokemon[0];
        var older = pokemon.Snapshots.Single();
        var newer = older with { CapturedAt = Monday.AddDays(1) };
        var other = older with { Format = "PK4", CapturedAt = Monday.AddDays(2) };

        pokemon.Keep(newer);
        pokemon.Keep(other);

        Assert.Equal([newer, other], pokemon.Snapshots);
        Assert.Same(newer, pokemon.SnapshotIn("PK3"));
        Assert.Same(other, pokemon.Newest);
    }

    [Fact]
    public void A_new_bank_has_one_group_of_named_empty_boxes()
    {
        var bank = new Bank();

        Assert.Equal(BoxGroup.DefaultBoxCount, bank.Boxes.Boxes.Count);
        Assert.Equal(Enumerable.Range(1, BoxGroup.DefaultBoxCount), bank.Boxes.Boxes.Select(b => b.Id));
        Assert.Equal("Box 1", bank.Boxes.Boxes[0].Name);
        Assert.Equal(new BankSlot(1, 1), bank.FirstFreeSlot());
        Assert.Empty(bank.Problems());
    }

    [Fact]
    public void Renaming_and_reordering_boxes_leaves_every_Pokemon_where_it_was()
    {
        var bank = SampleBank(); // three Pokémon in box ID 1
        var before = bank.Pokemon.Select(p => p.Slot).ToList();
        var first = bank.Boxes.Box(1)!;

        first.Name = "Starters";
        bank.Boxes.Boxes.Remove(first);
        bank.Boxes.Boxes.Add(first); // now shown last

        var reloaded = RoundTrip(bank);
        Assert.Equal(before, reloaded.Pokemon.Select(p => p.Slot));
        Assert.Equal("Starters", reloaded.Boxes.Boxes[^1].Name);
        Assert.Equal(1, reloaded.Boxes.Boxes[^1].Id);
        Assert.Same(reloaded.Pokemon[0], reloaded.At(new BankSlot(1, 1)));
    }

    [Fact]
    public void A_new_box_gets_an_ID_no_other_box_has()
    {
        var bank = new Bank();
        bank.Boxes.Boxes.RemoveAt(0); // box ID 1 is gone; IDs 2–10 remain

        var added = bank.Boxes.Add("Extras");

        Assert.Equal(BoxGroup.DefaultBoxCount + 1, added.Id);
        Assert.Same(added, bank.Boxes.Boxes[^1]);
    }

    [Fact]
    public void The_first_free_slot_follows_the_box_order()
    {
        var bank = new Bank();
        var last = bank.Boxes.Boxes[^1];
        bank.Boxes.Boxes.Remove(last);
        bank.Boxes.Boxes.Insert(0, last);

        Assert.Equal(new BankSlot(last.Id, 1), bank.FirstFreeSlot());
    }

    [Fact]
    public void A_bank_with_two_boxes_sharing_an_ID_is_refused()
    {
        var json = AsJson(new Bank());
        json["boxes"]!["boxes"]![1]!["id"] = 1;

        var e = Assert.Throws<BankFormatException>(() => ReadJson(json.ToJsonString()));
        Assert.Contains("box ID 1", e.Message);
    }

    [Fact]
    public void The_first_free_slot_skips_taken_ones_and_checked_out_placeholders()
    {
        var bank = SampleBank(); // box 1 slots 1–3 are taken; slot 3's Pokémon is checked out

        Assert.Equal(new BankSlot(1, 4), bank.FirstFreeSlot());
        Assert.Same(bank.Pokemon[2], bank.At(new BankSlot(1, 3)));
    }

    [Fact]
    public void A_bank_from_a_newer_rhome_is_refused()
    {
        var json = AsJson(new Bank());
        json["version"] = BankJson.CurrentVersion + 1;

        var e = Assert.Throws<BankFormatException>(() => ReadJson(json.ToJsonString()));
        Assert.Contains("newer rhome", e.Message);
    }

    [Theory]
    [InlineData("""{"format":"rhome-bank","boxes":{"boxes":[]}}""")]
    [InlineData("""{"format":"rhome-bank","version":0}""")]
    [InlineData("""{"format":"rhome-bank","version":"1"}""")]
    [InlineData("""{"format":"rhome-bank","version":1.5}""")]
    [InlineData("""{"format":"pkhex-bank","version":1}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""[1, 2, 3]""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void A_file_without_a_known_format_and_version_is_refused(string json) =>
        Assert.Throws<BankFormatException>(() => ReadJson(json));

    [Fact]
    public void A_bank_with_a_missing_field_is_refused()
    {
        var json = AsJson(SampleBank());
        json["pokemon"]![0]!.AsObject().Remove("fingerprint");

        Assert.Throws<BankFormatException>(() => ReadJson(json.ToJsonString()));
    }

    [Fact]
    public void A_bank_with_two_Pokemon_in_one_slot_is_refused()
    {
        var json = AsJson(SampleBank());
        json["pokemon"]![1]!["slot"] = json["pokemon"]![0]!["slot"]!.DeepClone();

        var e = Assert.Throws<BankFormatException>(() => ReadJson(json.ToJsonString()));
        Assert.Contains("bank box 1 slot 1", e.Message);
    }

    [Fact]
    public void A_broken_bank_is_never_written()
    {
        var bank = SampleBank();
        bank.Pokemon[1].Slot = bank.Pokemon[0].Slot;

        Assert.Throws<InvalidOperationException>(() => BankJson.ToBytes(bank));
    }

    [Fact]
    public void The_bank_lists_every_problem_it_finds()
    {
        var bank = SampleBank();
        bank.Pokemon[0].Slot = new BankSlot(1, 31);
        bank.Pokemon[1].Snapshots.Clear();
        bank.Pokemon[2].CheckedOut = new CheckOut(Guid.NewGuid(), new SaveSlot(null, 1));
        bank.Saves.Add(bank.Saves[0]);

        var problems = bank.Problems();

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("doesn't exist"));
        Assert.Contains(problems, p => p.Contains("no snapshot"));
        Assert.Contains(problems, p => p.Contains("the bank doesn't know"));
        Assert.Contains(problems, p => p.Contains("listed more than once"));
    }

    [Fact]
    public void The_store_starts_an_empty_bank_when_nothing_is_stored()
    {
        var store = new JsonBankStore(new MemoryFile());

        Assert.Empty(store.Load().Pokemon);
    }

    [Fact]
    public void The_store_gives_back_what_it_saved()
    {
        var file = new MemoryFile();
        var bank = SampleBank();

        new JsonBankStore(file).Save(bank);
        var loaded = new JsonBankStore(file).Load();

        Assert.Equal(BankJson.ToBytes(bank), BankJson.ToBytes(loaded));
    }

    [Fact]
    public void A_failed_save_leaves_the_stored_bank_alone()
    {
        var file = new MemoryFile();
        var store = new JsonBankStore(file);
        store.Save(SampleBank());
        var before = file.Contents;

        var broken = SampleBank();
        broken.Pokemon.Add(broken.Pokemon[0]);

        Assert.Throws<InvalidOperationException>(() => store.Save(broken));
        Assert.Same(before, file.Contents);
    }

    sealed class MemoryFile : IStoredFile
    {
        public byte[]? Contents { get; private set; }

        public bool Exists => Contents is not null;

        public Stream OpenRead() => new MemoryStream(Contents!, writable: false);

        public void Replace(ReadOnlySpan<byte> contents) => Contents = contents.ToArray();
    }
}
