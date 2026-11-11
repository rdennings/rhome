using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// My saves (issue #43): shows the known saves at once, then checks them (the launch check: only the known files, no
/// folder listing). Scan now finds new saves. One tap opens a save, reading it again first if it changed.
/// </summary>
public partial class SavesView : UserControl
{
    readonly MySaves? saves;
    bool busy;

    /// <summary>Raised when the user taps Set up again.</summary>
    public event Action? SetUpAgain;

    /// <summary>Raised when a save was opened.</summary>
    public event Action<OpenedSave>? Opened;

    public SavesView()
    {
        InitializeComponent();
        ScanButton.Click += (_, _) => _ = Run(ScanAsync);
        SetupButton.Click += (_, _) => SetUpAgain?.Invoke();
        SavesList.SelectionChanged += (_, _) =>
        {
            if ((SavesList.SelectedItem as Control)?.Tag is FoundGame game)
            {
                SavesList.SelectedItem = null;
                _ = OpenAsync(game);
            }
        };
    }

    public SavesView(MySaves saves) : this()
    {
        this.saves = saves;
        ShowGames(); // what was known last time, before the check: no I/O
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _ = RefreshAsync();
    }

    /// <summary>The launch check; or a full scan when none has run yet (e.g. setup was done before saves.json existed).</summary>
    public Task RefreshAsync() => Run(saves?.ScannedAt is null ? ScanAsync : CheckAsync);

    async Task CheckAsync()
    {
        var result = await Task.Run(saves!.Check);
        int changed = result.In(SaveChange.Changed).Count(), gone = result.In(SaveChange.Gone).Count();
        var unreachable = result.In(SaveChange.Unreachable).Select(f => f.Where).Distinct().ToList();
        AppLog.Write($"launch check: {Plural(result.Files.Count, "file")} in {result.Took.TotalMilliseconds:0} ms, " +
                     $"{changed} changed, {gone} gone, {unreachable.Count} folders not reached");
        foreach (var file in result.In(SaveChange.Changed))
            AppLog.Write($"read again: {file.Entry.RelativePath} in {file.Where.Name}");
        foreach (var problem in result.Problems)
            AppLog.Write(problem);

        var notices = result.In(SaveChange.Gone).Select(SaveText.Gone).ToList();
        if (unreachable.Count > 0)
            notices.Add(SaveText.CantReach(unreachable.Select(f => f.Name)));
        ShowGames($"Checked in {result.Took.TotalMilliseconds:0} ms", notices);
    }

    async Task ScanAsync()
    {
        var result = await Task.Run(() => saves!.Scan(DateTimeOffset.Now));
        foreach (var folder in result.Folders)
            AppLog.Write($"scanned {folder.Folder.Access.DisplayName}: {folder.State}, {folder.Files} files, " +
                         $"{folder.Saves} saves, {folder.Took.TotalMilliseconds:0} ms{(folder.Error is null ? "" : ": " + folder.Error)}");
        foreach (var problem in result.Problems)
            AppLog.Write(problem);

        var missed = result.Folders.Where(f => f.State != FolderScanState.Scanned).Select(f => f.Folder.Name).ToList();
        var took = result.Folders.Sum(f => f.Took.TotalMilliseconds);
        ShowGames($"Scanned in {took:0} ms", missed.Count > 0 ? [SaveText.CantReach(missed)] : []);
    }

    /// <summary>Opens a save, reading it again first if it changed; raises <see cref="Opened"/> when it worked.</summary>
    public Task OpenAsync(FoundGame game) => Run(() => Open(game));

    async Task Open(FoundGame game)
    {
        try
        {
            var opened = await Task.Run(() => saves!.Open(game));
            if (opened.ReadAgain)
                AppLog.Write($"{SaveText.Title(opened.Game)} changed since rhome last read it, so it was read again");
            AppLog.Write($"opened {SaveText.Title(opened.Game)}: {opened.Game.Loaded.Entry.RelativePath} in {opened.Game.Loaded.Where.Name}");
            ShowGames();
            Opened?.Invoke(opened);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"couldn't open {SaveText.Title(game)}: {ex.Message}");
            ShowGames(null, [ex.Message]);
        }
    }

    /// <summary>Redraws the list from <see cref="MySaves.Games"/>.</summary>
    void ShowGames(string? status = null, IReadOnlyList<string>? notices = null)
    {
        if (saves is null)
            return;
        var games = saves.Games;
        SavesList.ItemsSource = games.Select(g =>
        {
            var item = new StackPanel { Tag = g };
            item.Children.Add(new TextBlock { Text = SaveText.Title(g), FontWeight = FontWeight.SemiBold });
            item.Children.Add(new TextBlock { Text = SaveText.Detail(g), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 });
            return item;
        }).ToList();
        SavesHeader.Text = $"My saves ({games.Count})";
        EmptyText.IsVisible = games.Count == 0 && saves.ScannedAt is not null;

        if (status is not null || StatusText.Text is null)
            StatusText.Text = string.Join(" · ", new[]
            {
                status,
                saves.ScannedAt is { } at ? $"last scan {at.ToLocalTime():g}" : "not scanned yet",
            }.OfType<string>());
        if (notices is not null)
        {
            Notices.Text = string.Join("\n", notices);
            Notices.IsVisible = notices.Count > 0;
        }
    }

    static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary>Runs one thing at a time, logs what goes wrong, and re-enables the buttons after.</summary>
    async Task Run(Func<Task> step)
    {
        if (saves is null || busy)
            return;
        busy = true;
        ScanButton.IsEnabled = SetupButton.IsEnabled = SavesList.IsEnabled = false;
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            AppLog.Write($"failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            busy = false;
            ScanButton.IsEnabled = SetupButton.IsEnabled = SavesList.IsEnabled = true;
        }
    }
}
