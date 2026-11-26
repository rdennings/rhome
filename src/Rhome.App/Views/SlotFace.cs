using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.App.Sprites;

namespace Rhome.App.Views;

/// <summary>How a box slot is drawn (issue #49, mockup A).</summary>
public enum SlotState
{
    Normal,

    /// <summary>The Pokémon picked up: a solid accent outline.</summary>
    Picked,

    /// <summary>A Pokémon arrives here when the pending changes are saved: a dashed accent outline.</summary>
    Arriving,

    /// <summary>A Pokémon leaves this slot when the pending changes are saved: an empty dashed outline.</summary>
    Leaving,

    /// <summary>A bank Pokémon that's out in a game: its place is kept, faded, with a dashed outline.</summary>
    Away,
}

/// <summary>
/// A box slot (issues #48, #49): the Pokémon's sprite with its level and gender under it, in a button styled by its
/// <see cref="SlotState"/>. Shininess shows in the sprite. The name only shows when there's no sprite (offline,
/// before it's downloaded); the selected Pokémon's card has the rest.
/// </summary>
static class SlotFace
{
    public static Control For(SpriteRequest sprite, string name, string line, double fontSize)
    {
        var level = new TextBlock { Text = line, FontSize = fontSize, LineHeight = fontSize + 2, Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center };
        DockPanel.SetDock(level, Dock.Bottom);
        var fallback = new TextBlock
        {
            Text = name,
            FontWeight = FontWeight.SemiBold,
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var view = new SpriteView();
        view.SpriteChanged += () => fallback.IsVisible = !view.HasSprite;
        SpriteImages.Show(view, sprite);
        return new DockPanel { Margin = new Avalonia.Thickness(1, 1, 1, 2), Children = { level, new Panel { Children = { fallback, view } } } };
    }

    /// <summary>"Lv13 ♂": its level and gender; an egg has neither.</summary>
    public static string Line(bool isEgg, int level, int gender) =>
        isEgg ? "" : $"Lv{level}{gender switch { 0 => " ♂", 1 => " ♀", _ => "" }}";

    /// <summary>The slot's button, with <paramref name="face"/> (or nothing, when it's empty) drawn as <paramref name="state"/> says.</summary>
    public static Button Button(Control? face, SlotState state)
    {
        var content = new Panel();
        var button = new Button { Content = content, Classes = { "slot" } };
        if (face is not null)
        {
            if (state == SlotState.Away)
                face.Opacity = 0.35;
            content.Children.Add(face);
        }
        switch (state)
        {
            case SlotState.Picked:
                button.Classes.Add("picked");
                break;
            case SlotState.Arriving:
                button.Classes.Add("dashed");
                content.Children.Add(Dashes("RhomeAccent"));
                break;
            case SlotState.Leaving:
            case SlotState.Away:
                button.Classes.Add("dashed");
                button.Classes.Add("hollow");
                content.Children.Add(Dashes("RhomeDashed"));
                break;
        }
        return button;
    }

    static Rectangle Dashes(string brush)
    {
        var dashes = new Rectangle
        {
            StrokeThickness = 1.5,
            StrokeDashArray = new AvaloniaList<double> { 3, 2 },
            RadiusX = 10,
            RadiusY = 10,
            IsHitTestVisible = false,
        };
        dashes.Bind(Shape.StrokeProperty, dashes.GetResourceObservable(brush));
        return dashes;
    }
}
