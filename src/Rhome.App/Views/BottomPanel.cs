using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Rhome.App.Views;

/// <summary>
/// A screen that changes its layout on a two-screen device (issue #49): with a second screen it hands part of itself
/// to the bottom screen and gives the top screen to what's left.
/// </summary>
interface IDualScreen
{
    /// <summary>What goes on the bottom screen, or null to show the log there.</summary>
    Control? Bottom { get; }

    /// <summary>True when there's a second screen: leave out of the top screen what the bottom screen shows.</summary>
    void SetDual(bool dual);
}

/// <summary>
/// The bottom screen while moving Pokémon (issue #49, mockup A dual): the picked-up Pokémon with its stats and moves,
/// the pending list, and big Save, Undo and Discard buttons, so the top screen is all box grids.
/// </summary>
sealed class BottomPanel : Border
{
    readonly TextBlock pendingLabel = new() { Classes = { "label" }, Margin = new Thickness(0, 0, 0, 8) };
    readonly StackPanel pendingList = new();

    public BottomPanel()
    {
        Padding = new Thickness(12);
        this[!BackgroundProperty] = this.GetResourceObservable("RhomeBackground").ToBinding();

        Selected = new SelectedCard(large: true) { VerticalAlignment = VerticalAlignment.Stretch };
        Save = new Button { Content = "Review and save", Classes = { "accent" }, Height = 52, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        Undo = new Button { Content = "Undo last", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        Discard = new Button { Content = "Discard all", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

        var buttons = new StackPanel { Children = { Save, new UniformGrid { Columns = 2, Children = { Undo, Discard } } } };
        DockPanel.SetDock(pendingLabel, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var pending = new Border
        {
            Classes = { "card" },
            Padding = new Thickness(12),
            Child = new DockPanel { Children = { pendingLabel, buttons, new ScrollViewer { Content = pendingList } } },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.1*,10,*") };
        Grid.SetColumn(pending, 2);
        grid.Children.Add(new ScrollViewer { Content = Selected });
        grid.Children.Add(pending);
        Child = grid;
    }

    public SelectedCard Selected { get; }

    public Button Save { get; }

    public Button Undo { get; }

    public Button Discard { get; }

    /// <summary>Shows the pending list and enables the buttons for it.</summary>
    public void ShowPending(IReadOnlyList<PendingLine> lines, string empty)
    {
        pendingLabel.Text = lines.Count == 0 ? "PENDING" : $"PENDING · {lines.Count}";
        PendingList.Fill(pendingList, lines, empty);
        Save.IsEnabled = Undo.IsEnabled = Discard.IsEnabled = lines.Count > 0;
    }
}

/// <summary>The bottom screen everywhere else: the app log, newest first.</summary>
sealed class BottomLog : Border
{
    public BottomLog()
    {
        Padding = new Thickness(12);
        this[!BackgroundProperty] = this.GetResourceObservable("RhomeBackground").ToBinding();
        var label = new TextBlock { Text = "LOG", Classes = { "label" }, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top);
        Child = new Border { Classes = { "card" }, Padding = new Thickness(12), Child = new DockPanel { Children = { label, new LogView() } } };
    }
}
