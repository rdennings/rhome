namespace Rhome.Core;

/// <summary>
/// Where the bank is kept. Everything above this interface works on an in-memory <see cref="Bank"/>,
/// so moving to another store later (e.g. SQLite for sync, v1.3) only touches implementations of it.
/// </summary>
public interface IBankStore
{
    /// <summary>The stored bank, or a new empty one when nothing has been stored yet.</summary>
    /// <exception cref="BankFormatException">What's stored can't be read as a bank.</exception>
    Bank Load();

    /// <summary>Stores the bank in full, replacing what was there.</summary>
    void Save(Bank bank);
}

/// <summary>
/// One file the platform layer looks after for the core, which never sees its path (PLAN.md §4).
/// </summary>
public interface IStoredFile
{
    bool Exists { get; }

    Stream OpenRead();

    /// <summary>
    /// Replaces the file's contents in one step: the platform writes them somewhere else first (a temp
    /// file beside it), then swaps that in, so a crash or a full disk leaves either the old file or the
    /// new one, never half of each.
    /// </summary>
    void Replace(ReadOnlySpan<byte> contents);
}

/// <summary>The bank as one plain JSON file (<see cref="BankJson"/>).</summary>
public sealed class JsonBankStore(IStoredFile file) : IBankStore
{
    public Bank Load()
    {
        if (!file.Exists)
            return new Bank();

        using var stream = file.OpenRead();
        return BankJson.Read(stream);
    }

    // Serialized in full before the file is touched, so a bank that can't be written leaves the old file as it was.
    public void Save(Bank bank) => file.Replace(BankJson.ToBytes(bank));
}
