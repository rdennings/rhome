using Avalonia.Controls;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// A save opened from My saves (issue #43): its copies, party and boxes, read from disk when it was opened. A
/// stand-in for the split view (#45), so it keeps the test writes from #40.
/// </summary>
public partial class SaveView : UserControl
{
    readonly OpenedSave? opened;
    bool busy;

    /// <summary>The game this is, to open it again.</summary>
    public FoundGame? Game => opened?.Game;

    /// <summary>Raised when the user taps back to My saves.</summary>
    public event Action? Back;

    public SaveView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => Back?.Invoke();
        MoneyButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: true);
        SameButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: false);
    }

    public SaveView(OpenedSave opened) : this()
    {
        this.opened = opened;
        var game = opened.Game;
        var save = opened.Save.Save;
        TitleText.Text = SaveText.Title(game);

        foreach (var copy in game.Copies)
            CopyRows.Children.Add(new TextBlock
            {
                Text = SaveText.Copy(game, copy),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = game.HasSeveralCopies && ReferenceEquals(copy, game.Loaded) ? FontWeight.SemiBold : FontWeight.Normal,
            });

        if (!opened.Save.IsSupported)
        {
            PartyText.Text = $"Gen {opened.Save.Generation} saves aren't supported yet.";
            BoxesText.Text = "";
            MoneyButton.IsEnabled = SameButton.IsEnabled = false;
            return;
        }

        var party = SaveContents.Party(save);
        PartyText.Text = party.Count == 0 ? "Empty." : string.Join(", ", party.Select(p => Pokemon(p.Pokemon)));

        var boxes = SaveContents.Boxes(save);
        BoxesHeader.Text = $"Boxes ({boxes.Count} Pokémon)";
        BoxesText.Text = boxes.Count == 0
            ? "Empty."
            : string.Join("\n", boxes.GroupBy(b => b.Where.Box!.Value).Select(g =>
                $"{SaveContents.BoxName(save, g.Key)}: {string.Join(", ", g.Select(b => Pokemon(b.Pokemon)))}"));

        WriteText.Text = $"Test writes go to the copy the emulator loads, {game.Loaded.Entry.RelativePath}. " +
                         "Each one backs the save up first. Close the game in the emulator before writing.";
    }

    static string Pokemon(PKHeX.Core.PKM pk) =>
        (pk.IsEgg ? "Egg" : EnglishNames.Species(pk.Species)) + (pk.IsEgg ? "" : $" Lv{pk.CurrentLevel}");

    async Task WriteAsync(bool moneyPlusOne)
    {
        if (Host.Platform is not { } platform || opened is null || busy)
            return;
        busy = true;
        MoneyButton.IsEnabled = SameButton.IsEnabled = false;
        var file = opened.Game.Loaded;
        try
        {
            var line = await Task.Run(() => WriteTest.Run(
                file.Where.Access.Open(file.Entry.Id), platform.Storage.Backups, WriteTest.BackupId(file.Entry.Id),
                moneyPlusOne, DateTimeOffset.Now));
            AppLog.Write($"{(moneyPlusOne ? "money +1" : "unchanged")}: {file.Entry.RelativePath}: {line}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"write failed for {file.Entry.RelativePath}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            busy = false;
            MoneyButton.IsEnabled = SameButton.IsEnabled = true;
        }
    }
}
