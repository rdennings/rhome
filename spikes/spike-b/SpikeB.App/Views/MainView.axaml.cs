using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpikeB.Core;

namespace SpikeB.App.Views;

public partial class MainView : UserControl
{
    readonly Dictionary<string, ISaveFolder> _folders = [];
    readonly Dictionary<string, string> _lastBackup = [];
    BottomScreenView? _bottom;
    bool _busy;

    string StatePath => Path.Combine(Host.DataDir, "scan-state.json");
    string BackupDir => Path.Combine(Host.DataDir, "backups");

    public MainView()
    {
        InitializeComponent();
        InfoText.Text = $"{Host.Platform} · .NET {Environment.Version}";
        RescanButton.Click += (_, _) => _ = ScanAsync("scan now", full: true);
        SavesList.SelectionChanged += (_, _) => UpdateSelection();
        WriteSameButton.Click += (_, _) => _ = WriteTestAsync(WriteTest.SameBytes);
        WriteMoneyButton.Click += (_, _) => _ = WriteTestAsync(WriteTest.MoneyPlusOne);
        RestoreButton.Click += (_, _) => _ = RestoreAsync();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Directory.CreateDirectory(Host.DataDir);
        ShowBottomScreen();
        _ = ScanAsync("launch");
    }

    /// <summary>
    /// On a two-screen device the log and the last scan report move to the second screen, which gives
    /// the main screen more room. Elsewhere the log stays in the left column.
    /// </summary>
    void ShowBottomScreen()
    {
        if (_bottom is not null || Host.ShowOnSecondScreen is not { } show)
            return;
        var view = new BottomScreenView();
        try
        {
            if (!show(view))
                return;
        }
        catch (Exception ex)
        {
            Log($"second screen: couldn't show ({ex.GetType().Name}: {ex.Message})");
            return;
        }
        _bottom = view;
        view.LogText.Text = LogText.Text;
        LogHeader.IsVisible = false;
        LogText.IsVisible = false;
        Log("second screen: showing the log and last scan there");
    }

    FoundSave? Selected => (SavesList.SelectedItem as Control)?.Tag as FoundSave;

    // ---- Emulators -------------------------------------------------------------------------

    void RefreshEmulators()
    {
        EmulatorRows.Children.Clear();
        _folders.Clear();
        var access = Host.Folders;
        foreach (var emulator in EmulatorProfile.All)
        {
            string status;
            bool installed = access?.IsInstalled(emulator) ?? false;
            ISaveFolder? folder = null;
            if (access is null)
                status = "no folder access on this platform";
            else if (!installed)
                status = "not installed";
            else
            {
                folder = access.GetFolder(emulator, out status);
                if (folder is not null)
                    _folders[emulator.Key] = folder;
            }

            var button = new Button
            {
                Content = folder is null ? "Give access" : "Pick again",
                IsEnabled = installed && access is not null,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var e = emulator;
            button.Click += async (_, _) =>
            {
                Log($"{e.Name}: opening the folder picker at {e.Where}");
                var message = await access!.RequestAccessAsync(e);
                Log($"{e.Name}: {message}");
                await ScanAsync($"after {e.Name} grant", full: true);
            };

            var text = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = emulator.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = status, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap },
                },
            };
            var row = new DockPanel();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(text);
            EmulatorRows.Children.Add(row);
        }
    }

    // ---- Scan ------------------------------------------------------------------------------

    async Task ScanAsync(string reason, bool full = false)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            RefreshEmulators();
            var sources = EmulatorProfile.All
                .Where(p => _folders.ContainsKey(p.Key))
                .Select(p => (p, _folders[p.Key]))
                .ToList();
            var sw = Stopwatch.StartNew();
            var result = await Task.Run(() =>
            {
                var state = ScanState.Load(StatePath);
                var r = full ? SaveScanner.Scan(sources, state) : SaveScanner.Check(sources, state);
                state.Save(StatePath);
                return r;
            });
            var total = sw.Elapsed;

            var selectedKey = Selected is { } previous ? Key(previous) : null;
            var rows = result.Saves.Select(SaveRow).ToList();
            SavesList.ItemsSource = rows;
            SavesList.SelectedItem = rows.FirstOrDefault(r => r.Tag is FoundSave s && Key(s) == selectedKey);
            int fresh = result.Saves.Count(s => s.Status != SaveStatus.Unchanged);
            SavesHeader.Text = $"My saves ({result.Saves.Count}{(fresh > 0 ? $", {fresh} new or changed" : "")})";

            var summary = $"{(full ? "scan" : "check")} ({reason}): {sources.Count} folders, {result.FilesListed} files listed, {result.FilesChecked} known saves checked, " +
                          $"{result.CandidateFiles} save files, {result.Saves.Count} Pokémon · " +
                          $"list {result.ListTime.TotalMilliseconds:F0} ms, read {result.FilesRead} in " +
                          $"{result.ReadTime.TotalMilliseconds:F0} ms, total {total.TotalMilliseconds:F0} ms";
            Log(summary);
            foreach (var error in result.Errors)
                Log("  " + error);
            WriteScanReport(summary, result);
        }
        catch (Exception ex)
        {
            Log($"scan failed: {ex}");
        }
        finally
        {
            _busy = false;
            UpdateSelection();
        }
    }

    static Control SaveRow(FoundSave s)
    {
        string badge = s.Status switch { SaveStatus.New => "NEW · ", SaveStatus.Changed => "CHANGED · ", _ => "" };
        var row = new StackPanel
        {
            Tag = s,
            Margin = new(0, 2),
            Children =
            {
                new TextBlock
                {
                    Text = $"{badge}{s.Game} · {s.Trainer} · {s.PlayTime}",
                    FontWeight = s.Status == SaveStatus.Unchanged ? FontWeight.Normal : FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = $"{s.Emulator.Name} · {s.Entry.RelativePath} · {s.Entry.LastModified.ToLocalTime():yyyy-MM-dd HH:mm}",
                    FontSize = 12,
                    Opacity = 0.7,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
        if (s.Note is not null)
        {
            bool ignored = s.Note.Contains("won't show", StringComparison.Ordinal);
            var note = new TextBlock
            {
                Text = (ignored ? "⚠ " : "") + s.Note,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            if (ignored)
                note.Foreground = Brushes.Orange;
            row.Children.Add(note);
        }
        return row;
    }

    void WriteScanReport(string summary, ScanResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Host.Platform}\n{summary}\n\n");
        foreach (var s in result.Saves)
        {
            sb.Append($"{s.Status,-9} {s.Emulator.Key,-12} {s.Game,-26} {s.Trainer,-10} {s.PlayTime,7}  {s.Entry.RelativePath}\n");
            if (s.Note is not null)
                sb.Append($"{"",10}note: {s.Note}\n");
        }
        foreach (var e in result.Errors)
            sb.Append($"error: {e}\n");
        var report = sb.ToString();
        TryWrite(Path.Combine(Host.DataDir, "scan.txt"), report, append: false);
        if (_bottom is { } bottom)
            Dispatcher.UIThread.Post(() => bottom.ScanText.Text = report);
    }

    // ---- Write tests -----------------------------------------------------------------------

    void UpdateSelection()
    {
        var s = Selected;
        bool can = s is not null && !_busy;
        WriteSameButton.IsEnabled = can;
        WriteMoneyButton.IsEnabled = can;
        RestoreButton.IsEnabled = can && _lastBackup.ContainsKey(Key(s!));
        SelectedText.Text = s is null
            ? "Tap a save to test writing it."
            : $"{s.Game} ({s.Trainer}) · {s.Emulator.Name} · {s.Entry.RelativePath} · {s.Entry.Size:N0} bytes. " +
              "Each write backs the save up first.";
    }

    static string Key(FoundSave s) => $"{s.Emulator.Key}|{s.Entry.Id}";

    async Task WriteTestAsync(WriteTest test)
    {
        if (Selected is not { } s || !_folders.TryGetValue(s.Emulator.Key, out var folder))
            return;
        await RunWrite($"{test} {s.Entry.RelativePath}", () =>
        {
            var r = InPlaceWriter.Run(folder, s.Entry, test, BackupDir);
            if (r.BackupPath is not null)
                _lastBackup[Key(s)] = r.BackupPath;
            return r;
        });
    }

    async Task RestoreAsync()
    {
        if (Selected is not { } s || !_folders.TryGetValue(s.Emulator.Key, out var folder) ||
            !_lastBackup.TryGetValue(Key(s), out var backup))
            return;
        await RunWrite($"restore {s.Entry.RelativePath}", () => InPlaceWriter.Restore(folder, s.Entry, backup));
    }

    async Task RunWrite(string what, Func<WriteResult> action)
    {
        _busy = true;
        UpdateSelection();
        try
        {
            var r = await Task.Run(action);
            Log($"{what}: {r.Message}{(r.BackupPath is null ? "" : $" (backup {Path.GetFileName(r.BackupPath)})")}");
        }
        catch (Exception ex)
        {
            Log($"{what}: FAILED {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
        await ScanAsync($"after {what}");
    }

    // ---- Log -------------------------------------------------------------------------------

    void Log(string line)
    {
        line = $"{DateTime.Now:HH:mm:ss} {line}";
        Dispatcher.UIThread.Post(() =>
        {
            LogText.Text = line + "\n" + LogText.Text;
            if (_bottom is { } bottom)
                bottom.LogText.Text = LogText.Text;
        });
        TryWrite(Path.Combine(Host.DataDir, "log.txt"), line + "\n", append: true);
    }

    static void TryWrite(string path, string text, bool append)
    {
        try
        {
            if (append) File.AppendAllText(path, text);
            else File.WriteAllText(path, text);
        }
        catch (IOException) { }
    }
}
