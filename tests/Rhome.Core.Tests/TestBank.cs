using PKHeX.Core;

namespace Rhome.Core.Tests;

/// <summary>A fresh bank that knows these fixture saves, each loaded fresh, with shortcuts for transfers.</summary>
sealed class TestBank
{
    public static readonly DateTimeOffset Monday = new(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(-5));
    public static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    public Bank Bank { get; } = new();
    readonly Dictionary<string, (SaveFile Save, Guid Id)> _saves = [];

    public TestBank(params string[] fixtures)
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

    public TransferResult Import(string name, SaveSlot slot, DateTimeOffset? when = null, ImportAs how = default) =>
        Transfers.Import(Bank, Save(name), Id(name), slot, when ?? Monday, how: how);

    public TransferResult Withdraw(BankPokemon pokemon, string name, SaveSlot? slot = null, DateTimeOffset? when = null,
        MovesetChoice moves = MovesetChoice.Newest) =>
        Transfers.Withdraw(Bank, pokemon.Id, Save(name), Id(name), when ?? Tuesday, slot, moves);

    /// <summary>A bank record made straight from a Pokémon, as if imported from <paramref name="save"/>.</summary>
    public BankPokemon Add(PKM pk, string save, int slot = 1)
    {
        var record = BankPokemon.From(pk, new BankSlot(1, slot), Id(save), Monday);
        Bank.Pokemon.Add(record);
        return record;
    }
}
