using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Rhome.App.Sprites;

namespace Rhome.App.Views;

/// <summary>
/// The Pokémon detail view (issue #47): everything <see cref="PokemonDetails"/> holds, in a card over the bank or the
/// split view. Read-only; Close (or Back) puts the screen behind as it was, with the Pokémon still picked up.
/// </summary>
public sealed class PokemonSheet : Border
{
    static readonly IBrush Raised = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F));
    static readonly IBrush Lowered = new SolidColorBrush(Color.FromRgb(0x42, 0x8B, 0xCA));

    readonly TextBlock title = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock subtitle = new() { FontSize = 15, Opacity = 0.85 };
    readonly TextBlock where = new() { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock note = new() { FontSize = 12, FontStyle = FontStyle.Italic, Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
    readonly SpriteView sprite = new() { Width = 96, Height = 96 };
    readonly StackPanel[] columns = [new() { Spacing = 16 }, new() { Spacing = 16 }, new() { Spacing = 16 }];

    public PokemonSheet()
    {
        IsVisible = false;
        Background = new SolidColorBrush(Colors.Black, 0.5);

        var close = new Button { Content = "Close", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        close.Click += (_, _) => Hide();
        DockPanel.SetDock(close, Dock.Right);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(close);
        sprite.Margin = new Thickness(0, 0, 12, 0);
        sprite.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(sprite, Dock.Left);
        header.Children.Add(sprite);
        header.Children.Add(new StackPanel { Children = { title, subtitle, where, note } });
        DockPanel.SetDock(header, Dock.Top);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,32,Auto,32,*"), Margin = new Thickness(0, 0, 16, 0) }; // the third column has the most text; the margin leaves room for the scroll bar
        for (var i = 0; i < columns.Length; i++)
        {
            Grid.SetColumn(columns[i], i * 2);
            body.Children.Add(columns[i]);
        }

        var card = new Border
        {
            MaxWidth = 1100,
            Margin = new Thickness(16),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.5), // so it stands out from the dimmed screen in the dark theme
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel { Children = { header, new ScrollViewer { Content = body } } },
        };
        card[!BackgroundProperty] = new DynamicResourceExtension("SystemRegionBrush");
        Child = card;
    }

    public void Show(PokemonDetails details)
    {
        SpriteImages.Show(sprite, details.Sprite);
        title.Text = details.Title;
        subtitle.Text = details.Subtitle;
        where.Text = details.Where;
        note.Text = details.Note;
        note.IsVisible = details.Note is not null;

        foreach (var column in columns)
            column.Children.Clear();

        columns[0].Children.Add(Section("Summary", Facts(details.Facts)));
        columns[0].Children.Add(Section("Stats", Stats(details.Stats)));

        columns[1].Children.Add(Section("Moves", details.Moves.Count == 0
            ? Line("No moves.")
            : Rows(details.Moves.Select(m => (m.Name, $"PP {m.PP}/{m.MaxPP}")))));
        columns[1].Children.Add(Section("Ribbons", details.Ribbons.Count == 0
            ? Line("None yet.")
            : Lines(details.Ribbons.Select(r => "• " + r))));

        columns[2].Children.Add(Section("Origin", Facts(details.Origin)));
        columns[2].Children.Add(Section("Visited games", Visits(details)));
        if (details.Kept.Count > 0)
            columns[2].Children.Add(Section("Kept in the bank", Kept(details)));

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    static Control Section(string heading, Control content) => new StackPanel
    {
        Spacing = 6,
        Children = { new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.SemiBold }, content },
    };

    static Control Facts(IEnumerable<Fact> facts) => Rows(facts.Select(f => (f.Label, f.Value)), labelsFaint: true);

    /// <summary>Two columns: a label (or name) on the left, its value on the right.</summary>
    static Grid Rows(IEnumerable<(string Left, string Right)> rows, bool labelsFaint = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
        var row = 0;
        foreach (var (left, right) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(Cell(new TextBlock { Text = left, Opacity = labelsFaint ? 0.7 : 1 }, row, 0));
            grid.Children.Add(Cell(new TextBlock { Text = right, TextWrapping = TextWrapping.Wrap }, row, 2));
            row++;
        }
        return grid;
    }

    /// <summary>Each stat with its value, IV and EV. A stat the nature raises is red, one it lowers blue, as in the games.</summary>
    static Grid Stats(IReadOnlyList<StatLine> stats)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto,16,Auto,16,Auto") };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var (text, column) in new[] { ("IV", 4), ("EV", 6) })
            grid.Children.Add(Cell(new TextBlock { Text = text, Opacity = 0.6, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right }, 0, column));

        for (var i = 0; i < stats.Count; i++)
        {
            var stat = stats[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = stat.Nature switch { > 0 => stat.Name + " +", < 0 => stat.Name + " −", _ => stat.Name } };
            if (stat.Nature != 0)
                name.Foreground = stat.Nature > 0 ? Raised : Lowered;
            grid.Children.Add(Cell(name, i + 1, 0));
            grid.Children.Add(Cell(Number(stat.Value, bold: true), i + 1, 2));
            grid.Children.Add(Cell(Number(stat.IV), i + 1, 4));
            grid.Children.Add(Cell(Number(stat.EV), i + 1, 6));
        }
        return grid;
    }

    static Control Visits(PokemonDetails details)
    {
        if (details.Visits.Count == 0)
            return Line("It isn't in the bank, so the bank holds nothing else for it.");
        var panel = new StackPanel { Spacing = 4 };
        foreach (var visit in details.Visits)
        {
            panel.Children.Add(new TextBlock { Text = $"Gen {visit.Generation} · {visit.Game}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(Line($"Lv{visit.Level}, last seen {PokemonDetails.Date(visit.LastSeen)}{(visit.IsLatest ? " · newest" : "")}"));
        }
        if (details.Visits.Count > 1 && details.Kept.Count == 0)
            panel.Children.Add(Line($"Everything its other data holds fits in {details.InGame} too.", top: 4));
        return panel;
    }

    static Control Kept(PokemonDetails details)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Line($"Its data from these games holds things {details.InGame} can't. The bank keeps them, " +
                                "and they can come back when it returns to a game of that generation."));
        foreach (var kept in details.Kept)
        {
            panel.Children.Add(new TextBlock { Text = $"Gen {kept.Generation} · {kept.Game}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(Lines(kept.Lines.Select(l => "• " + l)));
        }
        return panel;
    }

    static StackPanel Lines(IEnumerable<string> lines)
    {
        var panel = new StackPanel { Spacing = 2 };
        foreach (var line in lines)
            panel.Children.Add(Line(line));
        return panel;
    }

    static TextBlock Line(string text, double top = 0) =>
        new() { Text = text, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

    static TextBlock Number(int value, bool bold = false) => new()
    {
        Text = value.ToString(),
        HorizontalAlignment = HorizontalAlignment.Right,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Opacity = bold ? 1 : 0.75,
    };

    static Control Cell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }
}
