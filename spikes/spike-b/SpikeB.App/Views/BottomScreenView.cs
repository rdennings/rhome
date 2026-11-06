using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SpikeB.App.Views;

/// <summary>
/// What the second screen shows: the log on the left and the last scan report on the right, so the
/// main screen keeps its room for emulators and saves. Built in code; it's a debugging panel.
/// </summary>
public sealed class BottomScreenView : UserControl
{
    public SelectableTextBlock LogText { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };

    public SelectableTextBlock ScanText { get; } = new()
    {
        FontFamily = new FontFamily("monospace"),
        FontSize = 11,
        TextWrapping = TextWrapping.NoWrap,
        Opacity = 0.85,
    };

    public BottomScreenView()
    {
        Background = Brushes.Black;
        var grid = new Grid { Margin = new Thickness(16), ColumnDefinitions = new("2*,16,3*"), RowDefinitions = new("Auto,*") };
        Add(grid, Header("Log"), 0, 0);
        Add(grid, new ScrollViewer { Content = LogText }, 1, 0);
        Add(grid, Header("Last scan"), 0, 2);
        Add(grid, new ScrollViewer { Content = ScanText, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, 1, 2);
        Content = grid;
    }

    static TextBlock Header(string text) =>
        new() { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };

    static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
