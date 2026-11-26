using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Rhome.App.Views;

/// <summary>
/// The app's one window: first-run setup until it has been done once, then My saves and the opened save. On a
/// two-screen device (issue #49) each screen also gets the bottom screen: the split view and the bank put the picked-up
/// Pokémon, the pending list and Save there; elsewhere it shows the log.
/// </summary>
public partial class MainView : UserControl
{
    readonly MySaves? saves;
    readonly PendingChanges? pending;
    readonly BottomLog bottomLog = new();
    readonly ISecondScreen? second;
    SavesView? savesView;
    IActivatableLifetime? lifetime;
    TopLevel? topLevel;

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
        pending = new PendingChanges(platform.Storage);
        if (saves.LoadProblem is { } savesProblem)
            AppLog.Write(savesProblem);
        second = platform.SecondScreen;
        if (second is not null)
        {
            AppLog.Write(second.IsPresent ? "second screen: found, using it" : "second screen: none");
            second.PresenceChanged += () =>
            {
                AppLog.Write(second.IsPresent ? "second screen: connected" : "second screen: gone");
                Arrange();
            };
        }

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
        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
            topLevel.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (lifetime is not null)
            lifetime.Activated -= OnActivated;
        lifetime = null;
        if (topLevel is not null)
            topLevel.BackRequested -= OnBackRequested;
        topLevel = null;
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
            case TransferView open when open.Game is { } game:
                _ = savesView.OpenAsync(game);
                break;
        }
    }

    /// <summary>
    /// The system back button (Android) does what the screen's own back button does, so leaving a save with unsaved
    /// changes asks first. On My saves it closes About if it's open; otherwise it isn't handled, and the system leaves
    /// the app as usual.
    /// </summary>
    void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (Screen.Content is IGoesBack screen)
        {
            e.Handled = true;
            screen.GoBack();
        }
        else if (Screen.Content is SavesView view && view.CloseSheet())
        {
            e.Handled = true;
        }
    }

    void Present(Control screen)
    {
        Screen.Content = screen;
        Arrange();
    }

    /// <summary>Lays the screen out for one screen or two, and puts its part (or the log) on the bottom screen.</summary>
    void Arrange()
    {
        if (second is null)
            return;
        var dual = second.IsPresent;
        var screen = Screen.Content as IDualScreen;
        screen?.SetDual(dual);
        second.Show(dual ? screen?.Bottom ?? bottomLog : null);
    }

    void ShowSetup()
    {
        var view = new SetupView(saves!);
        view.Finished += ShowSaves;
        Present(view);
    }

    void ShowSaves()
    {
        if (savesView is null)
        {
            savesView = new SavesView(saves!);
            savesView.SetUpAgain += ShowSetup;
            savesView.Opened += ShowSave;
            savesView.OpenBank += ShowBank;
        }
        Present(savesView); // checks the saves again each time it's shown
    }

    void ShowBank()
    {
        var view = new BankView(pending!);
        view.Back += ShowSaves;
        Present(view);
    }

    /// <summary>Opens the split view for a save, unless it can't take part in transfers; My saves then says why.</summary>
    void ShowSave(OpenedSave opened)
    {
        Guid saveId;
        try
        {
            saveId = pending!.Open(opened);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Write($"can't move Pokémon in {SaveText.Title(opened.Game)}: {ex.Message}");
            ShowSaves();
            savesView!.ShowNotice(ex.Message);
            return;
        }
        var view = new TransferView(pending, saveId, opened);
        view.Back += ShowSaves;
        Present(view);
    }
}

/// <summary>A screen with a way back, which the system back button uses too.</summary>
interface IGoesBack
{
    /// <summary>Does what the screen's back button does; it may ask first.</summary>
    void GoBack();
}
