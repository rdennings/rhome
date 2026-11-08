using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

// In-memory storage for changeset tests: saves, the bank file and backups, with switches to make writes fail.

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

    public FilesSnapshot Files() => new(BankFile.Bytes!.ToArray(), Saves.ToDictionary(s => s.Key, s => s.Value.Save.Bytes.ToArray()));
}

sealed record FilesSnapshot(byte[] Bank, Dictionary<string, byte[]> Saves)
{
    public void AssertSameAs(World world)
    {
        Assert.Equal(Bank, world.BankFile.Bytes);
        foreach (var (name, bytes) in Saves)
            Assert.Equal(bytes, world.Save(name).Bytes);
    }
}
