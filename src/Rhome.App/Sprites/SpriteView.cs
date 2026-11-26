using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Rhome.App.Sprites;

/// <summary>
/// A Pokémon's sprite, trimmed to the Pokémon (issue #48). Black and White's sprites are 96×96 with the Pokémon in the
/// middle and empty space around it, so drawing the whole image would leave a small Pokémon in a small box slot.
/// Every sprite is drawn at the same scale, so Wailord still dwarfs Pichu: <see cref="Reference"/> sprite pixels fill
/// the view's height, and a Pokémon too big for that is shrunk to fit. Pixels stay sharp.
/// </summary>
public sealed class SpriteView : Control
{
    /// <summary>
    /// How many sprite pixels fill the view: a little more than a mid-sized Pokémon such as Pikachu or Eevee, so those
    /// nearly fill a box slot and bigger ones are shrunk to fit.
    /// </summary>
    public const double Reference = 44;

    Bitmap? bitmap;
    PixelRect content;

    public SpriteView() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);

    /// <summary>The file it's showing or waiting for, so a late load doesn't land on a view that has moved on.</summary>
    internal string? Path { get; set; }

    public bool HasSprite => bitmap is not null;

    /// <summary>A sprite appeared or went away.</summary>
    public event Action? SpriteChanged;

    internal void Set(Bitmap? image, PixelRect pokemon)
    {
        bitmap = image;
        content = pokemon;
        InvalidateVisual();
        SpriteChanged?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        if (bitmap is null || content.Width == 0 || content.Height == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Reference;
        scale = Math.Min(scale, Math.Min(Bounds.Width / content.Width, Bounds.Height / content.Height));
        var size = new Size(content.Width * scale, content.Height * scale);
        var at = new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2);
        context.DrawImage(bitmap, new Rect(content.X, content.Y, content.Width, content.Height), new Rect(at, size));
    }
}
