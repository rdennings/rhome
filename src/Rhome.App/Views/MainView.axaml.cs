using Avalonia.Controls;

namespace Rhome.App.Views;

/// <summary>The app's one window: first-run setup until it has been done once, the saves after.</summary>
public partial class MainView : UserControl
{
    readonly Setup? setup;

    public MainView()
    {
        InitializeComponent();
        if (Host.Platform is not { } platform)
        {
            Screen.Content = new TextBlock { Text = "No platform: nothing to show.", Margin = new Avalonia.Thickness(16) };
            return;
        }

        AppLog.Write($"rhome on {platform.Description} · .NET {Environment.Version} · app storage: {platform.Storage.Root}");
        setup = new Setup(platform);
        if (setup.LoadProblem is { } problem)
            AppLog.Write(problem);

        if (setup.State.Done)
            ShowSaves();
        else
            ShowSetup();
    }

    void ShowSetup()
    {
        var view = new SetupView(setup!);
        view.Finished += ShowSaves;
        Screen.Content = view;
    }

    void ShowSaves()
    {
        var view = new SavesView(setup!);
        view.SetUpAgain += ShowSetup;
        Screen.Content = view;
    }
}
