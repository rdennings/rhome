using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>Bank boxes (issue #44): add, rename, reorder and delete, all as pending changes.</summary>
public class BankBoxTests
{
    static readonly DateTimeOffset Monday = TestBank.Monday;

    /// <summary>A bank holding Buneary (box 1 slot 1) and Bidoof (box 1 slot 2) from Platinum.</summary>
    static World WithTwoPokemon()
    {
        var world = new World("platinum-us");
        var import = world.Changeset("platinum-us");
        import.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 1)));
        import.Add(new ImportChange(world.Id("platinum-us"), new SaveSlot(1, 2)));
        Assert.True(world.Runner.Apply(import, Monday).Written);
        return world;
    }

    static Changeset With(params Change[] changes)
    {
        var changeset = new Changeset();
        foreach (var change in changes)
            changeset.Add(change);
        return changeset;
    }

    static IEnumerable<string> Names(Bank bank) => bank.Boxes.Boxes.Select(b => b.Name);

    [Fact]
    public void Boxes_are_added_renamed_moved_and_deleted_on_apply()
    {
        var world = new World();
        var changes = With(
            new AddBoxChange(11, "  Starters "),
            new RenameBoxChange(2, "Legends"),
            new MoveBoxChange(11, 1),
            new DeleteBoxChange(10));

        var result = world.Runner.Apply(changes, Monday);

        Assert.True(result.Written, result.Refusal);
        Assert.Equal(["Add box Starters", "Rename box Box 2 to Legends", "Move box Starters to place 1", "Delete box Box 10"],
            result.Outcomes.Select(o => o.Description));
        var bank = world.Bank;
        Assert.Equal(["Starters", "Box 1", "Legends", "Box 3", "Box 4", "Box 5", "Box 6", "Box 7", "Box 8", "Box 9"], Names(bank));
        Assert.Equal(11, bank.Boxes.Boxes[0].Id);
        Assert.Single(world.Backups.Banks); // the bank before the change
        Assert.Empty(world.Backups.Saves);
    }

    [Fact]
    public void A_preview_shows_the_bank_as_the_changes_leave_it_and_stores_nothing()
    {
        var world = new World();
        var before = world.BankFile.Bytes!.ToArray();

        var preview = world.Runner.Preview(With(new AddBoxChange(11, "New"), new MoveBoxChange(11, 1)), Monday);

        Assert.Equal("New", preview.Bank!.Boxes.Boxes[0].Name);
        Assert.Equal(before, world.BankFile.Bytes);
        Assert.Empty(world.Backups.Banks);
    }

    [Fact]
    public void A_moved_box_takes_its_pokemon_along()
    {
        var world = WithTwoPokemon();
        var buneary = world.Bank.Pokemon[0];

        Assert.True(world.Runner.Apply(With(new MoveBoxChange(1, 5)), Monday).Written);

        var bank = world.Bank;
        Assert.Equal(5, bank.Boxes.Position(1));
        Assert.Equal(new BankSlot(1, 1), bank.Pokemon.Single(p => p.Id == buneary.Id).Slot);
        Assert.Equal("Box 1 slot 1", bank.Boxes.Describe(bank.Pokemon[0].Slot));
    }

    [Fact]
    public void Pokemon_can_move_into_a_box_added_in_the_same_changes()
    {
        var world = WithTwoPokemon();
        var bidoof = world.Bank.Pokemon[1];

        var result = world.Runner.Apply(With(new AddBoxChange(11, "Normal types"), new MoveInBankChange(bidoof.Id, new BankSlot(11, 30))), Monday);

        Assert.True(result.Written, result.Refusal);
        Assert.Equal("Move Bidoof to Normal types slot 30", result.Outcomes[1].Description);
        Assert.Equal(new BankSlot(11, 30), world.Bank.Pokemon.Single(p => p.Id == bidoof.Id).Slot);
    }

    [Fact]
    public void Undoing_an_earlier_add_doesnt_renumber_a_later_box()
    {
        var world = WithTwoPokemon();
        var bidoof = world.Bank.Pokemon[1];
        var first = new AddBoxChange(11, "First");
        var changes = With(first, new AddBoxChange(12, "Second"), new MoveInBankChange(bidoof.Id, new BankSlot(12, 1)));

        changes.Remove(first);
        var result = world.Runner.Apply(changes, Monday);

        Assert.True(result.Written, result.Refusal);
        Assert.Equal(new BankSlot(12, 1), world.Bank.Pokemon.Single(p => p.Id == bidoof.Id).Slot);
    }

    [Fact]
    public void A_box_with_pokemon_in_it_cant_be_deleted()
    {
        var world = WithTwoPokemon();

        var result = world.Runner.Preview(With(new DeleteBoxChange(1)), Monday);

        Assert.Equal("Box 1 isn't empty: move its 2 Pokémon out first.", result.Refusal);
        Assert.Null(result.Bank);
    }

    [Fact]
    public void A_box_holding_a_place_for_a_pokemon_out_in_a_game_cant_be_deleted()
    {
        var world = WithTwoPokemon();
        var buneary = world.Bank.Pokemon[0];
        var bidoof = world.Bank.Pokemon[1];
        var withdraw = world.Changeset("platinum-us");
        withdraw.Add(new WithdrawChange(buneary.Id, world.Id("platinum-us")));
        withdraw.Add(new MoveInBankChange(bidoof.Id, new BankSlot(2, 1)));
        Assert.True(world.Runner.Apply(withdraw, Monday).Written);

        var result = world.Runner.Preview(With(new DeleteBoxChange(1)), Monday);

        Assert.Equal("Box 1 isn't empty: move its 1 Pokémon out first (1 of them out in a game, holding their place).", result.Refusal);
    }

    [Fact]
    public void The_last_box_cant_be_deleted()
    {
        var world = new World();
        var all = Enumerable.Range(1, 9).Select(id => (Change)new DeleteBoxChange(id)).ToArray();
        Assert.True(world.Runner.Preview(With(all), Monday).Succeeded);

        var result = world.Runner.Preview(With([.. all, new DeleteBoxChange(10)]), Monday);

        Assert.Equal("The bank needs at least one box.", result.Refusal);
    }

    [Theory]
    [InlineData("", "A box needs a name.")]
    [InlineData("   ", "A box needs a name.")]
    [InlineData("A name much too long for any box", "A box name can be at most 30 characters.")]
    public void A_box_needs_a_sensible_name(string name, string refusal)
    {
        var world = new World();

        Assert.Equal(refusal, world.Runner.Preview(With(new RenameBoxChange(1, name)), Monday).Refusal);
        Assert.Equal(refusal, world.Runner.Preview(With(new AddBoxChange(11, name)), Monday).Refusal);
    }

    [Fact]
    public void Changes_to_boxes_that_dont_exist_are_refused()
    {
        var world = new World();

        Assert.Equal("There's already a box with ID 3.", world.Runner.Preview(With(new AddBoxChange(3, "Again")), Monday).Refusal);
        Assert.Equal("The bank has no box with ID 42.", world.Runner.Preview(With(new RenameBoxChange(42, "x")), Monday).Refusal);
        Assert.Equal("The bank has no box with ID 42.", world.Runner.Preview(With(new DeleteBoxChange(42)), Monday).Refusal);
        Assert.Equal("There's no place 11: the bank has 10 boxes.", world.Runner.Preview(With(new MoveBoxChange(1, 11)), Monday).Refusal);
    }

    [Fact]
    public void The_next_id_is_one_past_the_highest_even_after_reordering()
    {
        var group = BoxGroup.CreateDefault();
        group.Boxes.Reverse();

        Assert.Equal(11, group.NextId);
        Assert.Equal(10, group.Position(1));
        Assert.Equal(0, group.Position(99));
    }
}
