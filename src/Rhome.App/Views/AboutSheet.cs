using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Rhome.App.Sprites;

namespace Rhome.App.Views;

/// <summary>
/// About rhome, over My saves: what it is, its licence, and where the sprites come from (issue #48). "Download sprites
/// for my games" fills the sprite cache in one go, so rhome shows every Pokémon offline.
/// </summary>
public sealed class AboutSheet : Border
{
    /// <summary>The Gen 3–5 games rhome supports hold species up to Genesect.</summary>
    const int MyGamesMaxSpecies = 649;

    readonly TextBlock spriteState = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
    readonly Button download = new() { Content = "Download sprites for my games", Classes = { "accent" } };
    bool busy;

    public AboutSheet()
    {
        IsVisible = false;
        Background = Palette.Scrim;

        var close = new Button { Content = "Close", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        close.Click += (_, _) => Hide();
        DockPanel.SetDock(close, Dock.Right);
        download.Click += async (_, _) => await DownloadAsync();

        var body = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new DockPanel { Children = { close, new TextBlock { Text = "About rhome", FontSize = 22, FontWeight = FontWeight.SemiBold } } },
                Paragraph("A Pokémon bank for emulator players: move Pokémon between your games and a collection that keeps everything they have."),
                Paragraph("Free software under the GNU GPL v3. Source code and issues: github.com/rdennings/rhome. " +
                          "Save and Pokémon formats are handled by PKHeX.Core (github.com/kwsch/PKHeX)."),
                new TextBlock { Text = "Sprites", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) },
                Paragraph("Pokémon sprites come from PokeAPI/sprites (github.com/PokeAPI/sprites), thanks to everyone who collected them. " +
                          "rhome doesn't include them: each one is loaded the first time it's shown, then kept on this device. " +
                          "The images are © The Pokémon Company, Nintendo and Game Freak."),
                spriteState,
                new WrapPanel { Children = { download } },
            },
        };

        var card = new Border
        {
            MaxWidth = 720,
            Margin = new Thickness(16),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = Palette.CardBorder,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { Content = body },
        };
        card[!BackgroundProperty] = new DynamicResourceExtension("SystemRegionBrush");
        Child = card;
    }

    public void Show()
    {
        IsVisible = true;
        if (!busy)
            _ = CountAsync();
    }

    public void Hide() => IsVisible = false;

    static IReadOnlyList<string> MyGames(SpriteCache cache) => cache.Table.AllPaths(MyGamesMaxSpecies);

    async Task CountAsync()
    {
        if (SpriteImages.Cache is not { } cache)
        {
            spriteState.Text = "Sprites aren't available here.";
            download.IsEnabled = false;
            return;
        }
        var paths = MyGames(cache);
        var have = await Task.Run(() => paths.Count(cache.Has));
        spriteState.Text = have == paths.Count
            ? $"All {paths.Count} sprites for your games are on this device, so rhome shows them offline."
            : $"{have} of {paths.Count} sprites for your games are on this device. Download the rest to see every Pokémon offline (a few MB).";
        download.IsEnabled = have < paths.Count;
    }

    async Task DownloadAsync()
    {
        if (busy || SpriteImages.Cache is not { } cache)
            return;
        busy = true;
        download.IsEnabled = false;
        var progress = new Progress<(int Done, int Total)>(p => spriteState.Text = $"Downloading sprites: {p.Done} of {p.Total}…");
        try
        {
            var result = await cache.DownloadAllAsync(MyGames(cache), progress);
            AppLog.Write($"sprites: downloaded {result.Downloaded}, already had {result.AlreadyHad}, {result.Failed} failed");
            busy = false;
            await CountAsync();
            if (result.Failed > 0)
                spriteState.Text += $" {result.Failed} couldn't be downloaded: check the connection and try again.";
        }
        finally
        {
            busy = false;
        }
    }

    static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
}
