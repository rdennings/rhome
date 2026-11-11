using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The saves found by a scan. A stand-in until the My saves list with its launch check (#43): for now every open scans
/// the folders again, which takes a couple of seconds with a big ROM folder.
/// </summary>
public partial class SavesView : UserControl
{
    readonly Setup? setup;
    bool busy;

    /// <summary>Raised when the user taps Set up again.</summary>
    public event Action? SetUpAgain;

    public SavesView()
    {
        InitializeComponent();
        ScanButton.Click += (_, _) => _ = ScanAsync();
        SetupButton.Click += (_, _) => SetUpAgain?.Invoke();
        SavesList.SelectionChanged += (_, _) => UpdateSelection();
        MoneyButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: true);
        SameButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: false);
    }

    public SavesView(Setup setup) : this() => this.setup = setup;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _ = ScanAsync();
    }

    FoundGame? Selected => (SavesList.SelectedItem as Control)?.Tag as FoundGame;

    async Task ScanAsync()
    {
        if (setup is null || busy)
            return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(setup.Scan);
            foreach (var folder in result.Folders)
                AppLog.Write($"scanned {folder.Folder.Access.DisplayName}: {folder.State}, {folder.Files} files, " +
                             $"{folder.Saves} saves, {folder.Took.TotalMilliseconds:0} ms");
            foreach (var problem in result.Problems)
                AppLog.Write(problem);

            var unreachable = result.Folders.Where(f => f.State != FolderScanState.Scanned).Select(f => f.Folder.Name).ToList();
            FolderProblems.IsVisible = unreachable.Count > 0;
            FolderProblems.Text = $"Can't reach {string.Join(", ", unreachable)}. Is the SD card in? Tap Set up again to pick it again.";

            SavesList.ItemsSource = result.Games.Select(g =>
            {
                var item = new StackPanel { Tag = g };
                item.Children.Add(new TextBlock { Text = SaveText.Title(g), FontWeight = FontWeight.SemiBold });
                item.Children.Add(new TextBlock { Text = SaveText.Detail(g), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 });
                return item;
            }).ToList();
            SavesHeader.Text = $"Saves ({result.Games.Count})";
        }
        catch (Exception ex)
        {
            AppLog.Write($"scan failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    void UpdateSelection()
    {
        var game = Selected;
        SelectedText.Text = game is null
            ? "Tap a save to test writing it."
            : $"{SaveText.Title(game)}: test writes go to the copy the emulator loads, {game.Loaded.Entry.RelativePath} " +
              $"({game.Loaded.Entry.Size} bytes, changed {game.Loaded.Entry.LastModified.ToLocalTime():g}).";
        MoneyButton.IsEnabled = SameButton.IsEnabled = game is not null && !busy;
    }

    async Task WriteAsync(bool moneyPlusOne)
    {
        if (Host.Platform is not { } platform || Selected is not { } game || busy)
            return;
        SetBusy(true);
        var file = game.Loaded;
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
            SetBusy(false);
        }
    }

    void SetBusy(bool value)
    {
        busy = value;
        ScanButton.IsEnabled = SetupButton.IsEnabled = !value;
        UpdateSelection();
    }
}
