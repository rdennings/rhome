using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.App.Sprites;

namespace Rhome.App.Views;

/// <summary>
/// The picked-up Pokémon at a glance (issue #49, mockup A): sprite, name, level, nature and ability, its moves, and its
/// trainer and where it was met; on the bottom screen, bigger and with its stats. Tapping it opens the full detail view.
/// With nothing picked up it says what to do.
/// </summary>
public sealed class SelectedCard : Button
{
    readonly SpriteView sprite = new() { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock title = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Grid stats = new() { ColumnDefinitions = new ColumnDefinitions("Auto,8,Auto,8,*"), Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
    readonly TextBlock subtitle = new() { FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    readonly UniformGrid moves = new() { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock origin = new() { FontSize = 11, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock more = new() { Text = "Details ›", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock hint = new() { FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    readonly StackPanel shown;
    readonly bool large;

    public SelectedCard() : this(large: false)
    {
    }

    /// <param name="large">The bottom screen's card: a bigger sprite and title, and the stats.</param>
    public SelectedCard(bool large)
    {
        this.large = large;
        if (large)
        {
            sprite.Width = sprite.Height = 60;
            title.FontSize = 20;
        }
        Classes.Add("card");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(14);
        this[!BackgroundProperty] = this.GetResourceObservable("RhomeCard").ToBinding();
        this[!BorderBrushProperty] = this.GetResourceObservable("RhomeCardBorder").ToBinding();
        more[!TextBlock.ForegroundProperty] = this.GetResourceObservable("RhomeAccent").ToBinding();

        var head = new DockPanel { Children = { sprite, new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 0, 0), Children = { title, subtitle } } } };
        DockPanel.SetDock(sprite, Dock.Left);
        shown = new StackPanel { Children = { head, stats, moves, origin, more } };
        Content = hint;
        IsEnabled = false;
    }

    /// <summary>What to say when nothing is picked up.</summary>
    public string Hint
    {
        get => hint.Text ?? "";
        set => hint.Text = value;
    }

    /// <summary>Shows a Pokémon, or the hint when <paramref name="details"/> is null.</summary>
    public void Show(PokemonDetails? details)
    {
        IsEnabled = details is not null;
        if (details is null)
        {
            Content = hint;
            return;
        }

        SpriteImages.Show(sprite, details.Sprite);
        title.Text = details.Title;
        var nature = Fact(details.Facts, "Nature")?.Split(" (")[0];
        subtitle.Text = string.Join(" · ", new[] { details.Subtitle, nature, Fact(details.Facts, "Ability") }.Where(s => !string.IsNullOrEmpty(s)));

        ShowStats(details.Stats);
        moves.Children.Clear();
        foreach (var move in details.Moves)
            moves.Children.Add(new Border { Classes = { "chip" }, Margin = new Thickness(2), Child = new TextBlock { Text = move.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis } });
        moves.IsVisible = details.Moves.Count > 0;

        var met = details.Origin.FirstOrDefault(f => f.Label is "Met" or "Hatched" or "Received");
        origin.Text = string.Join("\n", new[] { Fact(details.Origin, "Trainer") is { } ot ? "OT " + ot : null, met is null ? null : $"{met.Label} {met.Value}" }
            .Where(s => s is not null));
        Content = shown;
    }

    /// <summary>Each stat with a bar, as long as the stat is against its biggest one.</summary>
    void ShowStats(IReadOnlyList<StatLine> lines)
    {
        stats.Children.Clear();
        stats.RowDefinitions.Clear();
        stats.IsVisible = large && lines.Count > 0;
        if (!stats.IsVisible)
            return;
        var most = Math.Max(1, lines.Max(l => l.Value));
        for (var i = 0; i < lines.Count; i++)
        {
            stats.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = lines[i].Name, FontSize = 11, Classes = { "muted" }, Margin = new Thickness(0, 1) };
            var value = new TextBlock { Text = lines[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            var bar = new ProgressBar { Minimum = 0, Maximum = most, Value = lines[i].Value, MinWidth = 0, Height = 6, MinHeight = 6, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (control, column) in new (Control, int)[] { (name, 0), (value, 2), (bar, 4) })
            {
                Grid.SetRow(control, i);
                Grid.SetColumn(control, column);
                stats.Children.Add(control);
            }
        }
    }

    static string? Fact(IEnumerable<Fact> facts, string label) => facts.FirstOrDefault(f => f.Label == label)?.Value;
}

/// <summary>The pending list (issue #49, mockup A): each change with a dot for its direction, and where it goes.</summary>
static class PendingList
{
    public static void Fill(Panel list, IReadOnlyList<PendingLine> lines, string empty)
    {
        list.Children.Clear();
        if (lines.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = empty, FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var line in lines)
        {
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            dot[!Avalonia.Controls.Shapes.Shape.FillProperty] = dot.GetResourceObservable(line.Direction switch
            {
                PendingDirection.IntoBank => "RhomeAccent",
                PendingDirection.OutToGame => "RhomeAmber",
                _ => "RhomeMuted",
            }).ToBinding();
            DockPanel.SetDock(dot, Dock.Left);
            var route = line.Route + (line.Notes switch { 0 => "", 1 => " · 1 note in Review", var n => $" · {n} notes in Review" });
            var text = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = line.Name, FontSize = 13, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap } } };
            if (route.Length > 0)
                text.Children.Add(new TextBlock { Text = route.TrimStart(' ', '·'), FontSize = 11, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            var row = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 6),
                Child = new DockPanel { Children = { dot, text } },
            };
            row[!Border.BorderBrushProperty] = row.GetResourceObservable("RhomeCardBorder").ToBinding();
            list.Children.Add(row);
        }
    }
}
