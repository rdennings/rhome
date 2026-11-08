using PKHeX.Core;
using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>
/// The slot addresses and names SaveContents gives. Its party and box listings are also checked against
/// every fixture's manifest in <see cref="FixtureTests"/>.
/// </summary>
public class SaveContentsTests
{
    static SaveFile Load(string name) =>
        SaveDetector.Detect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".sav")))!.Save;

    [Fact]
    public void Box_slots_are_counted_from_1_in_every_box()
    {
        var save = Load("platinum-us");
        var eevee = save.GetBoxSlotAtIndex(0, 5);
        save.SetBoxSlotAtIndex(eevee, 2, 6); // box 3, slot 7 counted from 1

        var found = Assert.Single(SaveContents.Boxes(save), s => s.Where.Box == 3);

        Assert.Equal(new SaveSlot(3, 7), found.Where);
        Assert.Equal("box 3 slot 7", found.Where.ToString());
    }

    [Fact]
    public void Party_slots_have_no_box()
    {
        var party = SaveContents.Party(Load("white"));

        Assert.Equal([1, 2, 3, 4, 5, 6], party.Select(s => s.Where.Slot));
        Assert.All(party, s => Assert.True(s.Where.IsParty));
        Assert.Equal("party slot 2", party[1].Where.ToString());
    }

    [Theory]
    [InlineData("emerald", "BOX1")]
    [InlineData("platinum-us", "BOX 1")]
    [InlineData("white", "BOX 1")]
    public void Box_names_are_the_games_own(string fixture, string expected) =>
        Assert.Equal(expected, SaveContents.BoxName(Load(fixture), 1));

    [Fact]
    public void A_Pokemon_from_another_game_is_not_from_this_trainer()
    {
        var emerald = Load("emerald");
        var treecko = SaveContents.Party(emerald).Single().Pokemon;
        var kirlia = SaveContents.Boxes(emerald).Single().Pokemon; // moved over from Platinum

        Assert.True(SaveContents.IsFromThisTrainer(emerald, treecko));
        Assert.False(SaveContents.IsFromThisTrainer(emerald, kirlia));
    }
}
