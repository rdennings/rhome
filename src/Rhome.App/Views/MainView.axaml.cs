using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Rhome.App.Views;

/// <summary>The app's one window: first-run setup until it has been done once, then My saves and the opened save.</summary>
public partial class MainView : UserControl
{
    readonly MySaves? saves;
    SavesView? savesView;
    IActivatableLifetime? lifetime;

    public MainView()
    {
        InitializeComponent();
        if (Host.Platform is not { } platform)
        {
            Screen.Content = new TextBlock { Text = "No platform: nothing to show.", Margin = new Thickness(16) };
            return;
        }

        AppLog.Write($"rhome on {platform.Description} · .NET {Environment.Version} · app storage: {platform.Storage.Root}");
        var setup = new Setup(platform);
        if (setup.LoadProblem is { } problem)
            AppLog.Write(problem);
        saves = new MySaves(setup, platform.Storage);
        if (saves.LoadProblem is { } savesProblem)
            AppLog.Write(savesProblem);

        if (setup.State.Done)
            ShowSaves();
        else
            ShowSetup();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        lifetime = Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
        if (lifetime is not null)
            lifetime.Activated += OnActivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (lifetime is not null)
            lifetime.Activated -= OnActivated;
        lifetime = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Back from the emulator: the saves may have changed while rhome was in the background, so check them again, and
    /// read an open save again.
    /// </summary>
    void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background || savesView is null)
            return;
        switch (Screen.Content)
        {
            case SavesView view:
                _ = view.RefreshAsync();
                break;
            case SaveView open when open.Game is { } game:
                _ = savesView.OpenAsync(game);
                break;
        }
    }

    void ShowSetup()
    {
        var view = new SetupView(saves!);
        view.Finished += ShowSaves;
        Screen.Content = view;
    }

    void ShowSaves()
    {
        if (savesView is null)
        {
            savesView = new SavesView(saves!);
            savesView.SetUpAgain += ShowSetup;
            savesView.Opened += ShowSave;
        }
        Screen.Content = savesView; // checks the saves again each time it's shown
    }

    void ShowSave(Rhome.App.OpenedSave opened)
    {
        var view = new SaveView(opened);
        view.Back += ShowSaves;
        Screen.Content = view;
    }
}
