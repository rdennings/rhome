using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.App.Sprites;

namespace Rhome.App.Views;

/// <summary>
/// What a box slot shows (issue #48): the Pokémon's sprite, and its level and gender under it. Shininess shows in the
/// sprite. The name only shows when there's no sprite (offline, before it's downloaded); picking a Pokémon up names it
/// in the hint, and Details has the rest.
/// </summary>
static class SlotFace
{
    public static Control For(SpriteRequest sprite, string name, string line, double fontSize)
    {
        var level = new TextBlock { Text = line, FontSize = fontSize, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center };
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
        return new DockPanel { Children = { level, new Panel { Children = { fallback, view } } } };
    }

    /// <summary>"Lv13 ♂": its level and gender; an egg has neither.</summary>
    public static string Line(bool isEgg, int level, int gender) =>
        isEgg ? "" : $"Lv{level}{gender switch { 0 => " ♂", 1 => " ♀", _ => "" }}";
}
