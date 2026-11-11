using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>First-run setup (issue #42): find emulators, give access, first scan. Also opened again from the saves.</summary>
public partial class SetupView : UserControl
{
    readonly MySaves? saves;
    readonly Setup? setup;
    IReadOnlyList<KnownEmulator> found = [];
    bool busy;

    /// <summary>Raised when the user taps Done after the scan.</summary>
    public event Action? Finished;

    public SetupView()
    {
        InitializeComponent();
        FindButton.Click += (_, _) => Find();
        AddFileButton.Click += (_, _) => _ = Run(async () => AppLog.Write(await setup!.AddSaveFileAsync()));
        ScanButton.Click += (_, _) => _ = Run(ScanAsync);
        DoneButton.Click += (_, _) => Finished?.Invoke();
    }

    public SetupView(MySaves saves) : this()
    {
        this.saves = saves;
        setup = saves.Setup;
        if (setup.State.Sources.Count > 0)
            Find(); // run again: show where things stand
        Refresh();
    }

    void Find()
    {
        if (setup is null)
            return;
        found = setup.FindEmulators();
        var names = string.Join(" and ", found.Select(e => e.Name));
        FindText.Text = found.Count > 0
            ? $"Found {names}."
            : $"None of the emulators rhome knows ({string.Join(", ", KnownEmulators.All.Select(e => e.Name))}) is installed. " +
              "Install one and tap Find emulators again, or pick a save file by hand below.";
        FindButton.Content = "Find again";
        AppLog.Write(found.Count > 0 ? $"found {names}" : "found no known emulators");
        Refresh();
    }

    /// <summary>Redraws step 2's rows and whether Scan can be tapped.</summary>
    void Refresh()
    {
        if (setup is null)
            return;

        FolderRows.Children.Clear();
        foreach (var step in setup.FolderSteps(found))
            FolderRows.Children.Add(FolderRow(step));

        FileRows.Children.Clear();
        foreach (var file in setup.PickedFiles)
        {
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) =>
            {
                setup.Remove(file);
                AppLog.Write($"removed {file.Name}");
                Refresh();
            };
            FileRows.Children.Add(Row("✓ " + file.Name, null, remove));
        }

        FindButton.IsEnabled = AddFileButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy && setup.State.Sources.Count > 0;
    }

    Control FolderRow(FolderStep step)
    {
        var button = new Button { Content = step.Source is null ? "Give access" : "Change", IsEnabled = !busy };
        button.Click += (_, _) => _ = Run(async () => AppLog.Write(await setup!.GiveAccessAsync(step.Emulator, step.Folder)));

        var title = $"{(step.IsDone ? "✓" : step.Source is null ? "○" : "✗")} {step.Emulator.Name} · {step.Folder.Name}";
        var detail = step switch
        {
            { IsDone: true } => step.Source!.Name,
            { Source: not null } => $"{step.Source.Name} can't be reached (SD card out, or access lost). Tap Change to pick it again.",
            _ when step.Folder.RomFolder =>
                $"{step.Emulator.Name} saves next to the ROMs once it can write there. Pick the folder with your ROMs.",
            _ => Setup.Describe(step.Folder),
        };
        return Row(title, detail, button);
    }

    static Control Row(string title, string? detail, Button button)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (detail is not null)
            text.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });

        var row = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        button.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(button);
        row.Children.Add(text);
        return row;
    }

    async Task ScanAsync()
    {
        ScanRows.Children.Clear();
        var result = await Task.Run(() => saves!.Scan(DateTimeOffset.Now));

        foreach (var folder in result.Folders)
        {
            var line = folder.State switch
            {
                FolderScanState.Scanned => $"✓ {folder.Folder.Name}: {Plural(folder.Saves, "Pokémon save")}",
                FolderScanState.Unreachable => $"✗ {folder.Folder.Name}: can't be reached",
                _ => $"✗ {folder.Folder.Name}: couldn't be read ({folder.Error})",
            };
            ScanRows.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            AppLog.Write($"scanned {folder.Folder.Access.DisplayName}: {folder.State}, {folder.Files} files, " +
                         $"{folder.Saves} saves, {folder.Took.TotalMilliseconds:0} ms{(folder.Error is null ? "" : ": " + folder.Error)}");
        }
        foreach (var problem in result.Problems)
            AppLog.Write(problem);

        ScanRows.Children.Add(new TextBlock
        {
            Text = result.Games.Count == 0
                ? "No Pokémon saves yet. Play a little and save in game, check the folders above, then scan again."
                : $"Found {Plural(result.Games.Count, "game")}: {string.Join(", ", result.Games.Select(g => SaveText.Title(g)))}.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        });
        ScanButton.Content = "Scan again";
        DoneButton.IsVisible = true;
    }

    static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary>Runs one step at a time, logs what goes wrong, and redraws after.</summary>
    async Task Run(Func<Task> step)
    {
        if (busy || setup is null)
            return;
        busy = true;
        Refresh();
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            AppLog.Write($"setup failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }
}
