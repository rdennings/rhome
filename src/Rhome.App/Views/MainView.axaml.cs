using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Views;

public partial class MainView : UserControl
{
    /// <summary>How deep to look below a granted folder. melonDS and RetroArch keep saves at the top.</summary>
    const int Depth = 2;

    sealed record FoundSave(ISaveFolder Folder, FolderEntry Entry, string Game);

    bool _busy;

    static IAppPlatform? Platform => Host.Platform;

    public MainView()
    {
        InitializeComponent();
        InfoText.Text = Platform is { } p
            ? $"{p.Description} · .NET {Environment.Version} · app storage: {p.Storage.Root}"
            : "No platform: nothing to show.";
        AddFolderButton.Click += (_, _) => _ = AddFolderAsync();
        ScanButton.Click += (_, _) => _ = ScanAsync();
        SavesList.SelectionChanged += (_, _) => UpdateSelection();
        MoneyButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: true);
        SameButton.Click += (_, _) => _ = WriteAsync(moneyPlusOne: false);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _ = ScanAsync();
    }

    FoundSave? Selected => (SavesList.SelectedItem as Control)?.Tag as FoundSave;

    async Task AddFolderAsync()
    {
        if (Platform is not { } platform)
            return;
        try
        {
            Log(await platform.AddFolderAsync());
        }
        catch (Exception ex)
        {
            Log($"add folder failed: {ex.GetType().Name}: {ex.Message}");
        }
        await ScanAsync();
    }

    async Task ScanAsync()
    {
        if (Platform is not { } platform || _busy)
            return;
        SetBusy(true);
        try
        {
            var folders = platform.Folders();
            var (rows, saves, lines) = await Task.Run(() => Scan(folders));

            FolderRows.Children.Clear();
            foreach (var row in rows)
                FolderRows.Children.Add(new TextBlock { Text = row, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            if (rows.Count == 0)
                FolderRows.Children.Add(new TextBlock { Text = "None yet. Tap Add folder and pick an emulator's save folder.", Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

            SavesList.ItemsSource = saves.Select(s => new TextBlock
            {
                Text = $"{s.Game} · {s.Entry.RelativePath} · {s.Folder.DisplayName}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Tag = s,
            }).ToList();
            SavesHeader.Text = $"Saves ({saves.Count})";
            foreach (var line in lines)
                Log(line);
        }
        catch (Exception ex)
        {
            Log($"scan failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    static (List<string> Rows, List<FoundSave> Saves, List<string> Log) Scan(IReadOnlyList<ISaveFolder> folders)
    {
        var rows = new List<string>();
        var saves = new List<FoundSave>();
        var log = new List<string>();
        foreach (var folder in folders)
        {
            if (!folder.IsAvailable())
            {
                rows.Add($"✗ {folder.DisplayName}: can't be reached (card removed, or access lost)");
                continue;
            }
            var started = DateTime.UtcNow;
            IReadOnlyList<FolderEntry> entries;
            try
            {
                entries = folder.List(Depth);
            }
            catch (Exception ex)
            {
                rows.Add($"✗ {folder.DisplayName}: couldn't list it ({ex.GetType().Name})");
                log.Add($"couldn't list {folder.DisplayName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            var found = 0;
            // Only files the size of a save are read, so a ROM folder doesn't load gigabytes.
            foreach (var entry in entries.Where(e => e.Size <= int.MaxValue && SaveUtil.IsSizeValid((int)e.Size)))
            {
                try
                {
                    if (SaveDetector.Detect(folder.Open(entry.Id).Read()) is { } detected)
                    {
                        saves.Add(new FoundSave(folder, entry, $"{detected.Game} ({detected.Trainer})"));
                        found++;
                    }
                }
                catch (Exception ex)
                {
                    log.Add($"couldn't read {entry.RelativePath}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            var ms = (DateTime.UtcNow - started).TotalMilliseconds;
            rows.Add($"✓ {folder.DisplayName}: {entries.Count} files, {found} Pokémon saves");
            log.Add($"scanned {folder.DisplayName}: {entries.Count} files, {found} saves, {ms:0} ms");
        }
        return (rows, saves, log);
    }

    void UpdateSelection()
    {
        var save = Selected;
        SelectedText.Text = save is null
            ? "Tap a save to test writing it."
            : $"{save.Game}\n{save.Entry.RelativePath} in {save.Folder.DisplayName}, {save.Entry.Size} bytes, changed {save.Entry.LastModified.ToLocalTime():g}";
        MoneyButton.IsEnabled = SameButton.IsEnabled = save is not null && !_busy;
    }

    async Task WriteAsync(bool moneyPlusOne)
    {
        if (Platform is not { } platform || Selected is not { } save || _busy)
            return;
        SetBusy(true);
        try
        {
            var line = await Task.Run(() => WriteTest.Run(
                save.Folder.Open(save.Entry.Id), platform.Storage.Backups, WriteTest.BackupId(save.Entry.Id),
                moneyPlusOne, DateTimeOffset.Now));
            Log($"{(moneyPlusOne ? "money +1" : "unchanged")}: {save.Entry.RelativePath}: {line}");
        }
        catch (Exception ex)
        {
            Log($"write failed for {save.Entry.RelativePath}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        AddFolderButton.IsEnabled = ScanButton.IsEnabled = !busy;
        UpdateSelection();
    }

    /// <summary>Shows a line in the log, and appends it to <c>log.txt</c> in app storage for adb to pull.</summary>
    void Log(string line)
    {
        var stamped = $"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {line}";
        Dispatcher.UIThread.Post(() => LogText.Text = stamped + "\n" + LogText.Text);
        if (Platform is not { } platform)
            return;
        try
        {
            Directory.CreateDirectory(platform.Storage.Root);
            File.AppendAllText(Path.Combine(platform.Storage.Root, "log.txt"), stamped + "\n");
        }
        catch (IOException)
        {
            // The log on screen is enough.
        }
    }
}
