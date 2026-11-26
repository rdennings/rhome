using Avalonia.Media;

namespace Rhome.App.Views;

/// <summary>The theme's colours (App.axaml), for views built in code that can't look a resource up.</summary>
static class Palette
{
    /// <summary>Warnings and caveats: "RhomeAmber".</summary>
    public static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#E0B33A"));

    /// <summary>The card outline: "RhomeCardBorder".</summary>
    public static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#2A2E34"));

    /// <summary>Behind a sheet, dimming the screen.</summary>
    public static readonly IBrush Scrim = new SolidColorBrush(Colors.Black, 0.6);
}
