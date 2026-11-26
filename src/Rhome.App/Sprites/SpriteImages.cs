using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Rhome.App.Sprites;

/// <summary>
/// Sprites on screen (issue #48): fills a <see cref="SpriteView"/> once its sprite is loaded, from memory, the device,
/// or the network. Until then, or when it can't be loaded, the view stays empty.
/// </summary>
public static class SpriteImages
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Dictionary<string, (Bitmap Bitmap, PixelRect Pokemon)> Loaded = [];
    static SpriteCache? cache;

    /// <summary>The device's sprite cache, in app storage; null when there's no platform (in tests).</summary>
    public static SpriteCache? Cache => cache ??= Host.Platform is { } platform
        ? new SpriteCache(SpriteTable.Default, platform.Storage.SpritesFolder, Http)
        : null;

    /// <summary>Shows the Pokémon's sprite in <paramref name="view"/>, now if it's in memory, otherwise once it's loaded.</summary>
    public static void Show(SpriteView view, SpriteRequest request)
    {
        var path = Cache?.Table.PathFor(request);
        view.Path = path;
        if (path is not null && Loaded.TryGetValue(path, out var loaded))
        {
            view.Set(loaded.Bitmap, loaded.Pokemon);
            return;
        }
        view.Set(null, default);
        if (path is not null)
            _ = LoadAsync(Cache!, view, path);
    }

    static async Task LoadAsync(SpriteCache sprites, SpriteView view, string path)
    {
        byte[]? bytes;
        try
        {
            bytes = await sprites.GetAsync(path);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (bytes is null)
            return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!Loaded.TryGetValue(path, out var loaded))
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new Bitmap(stream);
                    loaded = (bitmap, PokemonIn(bitmap));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    AppLog.Write($"sprites: {path} isn't an image rhome can show ({ex.Message})");
                    return;
                }
                Loaded[path] = loaded;
            }
            if (view.Path == path) // still showing the same Pokémon
                view.Set(loaded.Bitmap, loaded.Pokemon);
        });
    }

    /// <summary>Where the Pokémon is in its sprite: the smallest rectangle holding every pixel that isn't see-through.</summary>
    static PixelRect PokemonIn(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (bitmap.Format is not { BitsPerPixel: 32 })
            return new PixelRect(size);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pinned.Free();
        }

        int left = size.Width, top = size.Height, right = -1, bottom = -1;
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] == 0) // alpha is the fourth byte in both BGRA and RGBA
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? new PixelRect(size) : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }
}
