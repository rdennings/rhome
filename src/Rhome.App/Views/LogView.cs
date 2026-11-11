using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Rhome.App.Views;

/// <summary>The app log (<see cref="AppLog"/>), newest line first. Keeps showing new lines while it's on screen.</summary>
public sealed class LogView : ScrollViewer
{
    readonly SelectableTextBlock text = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };

    public LogView() => Content = text;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        text.Text = string.Join("\n", AppLog.Recent);
        AppLog.Added += OnAdded;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppLog.Added -= OnAdded;
        base.OnDetachedFromVisualTree(e);
    }

    void OnAdded(string line) => Dispatcher.UIThread.Post(() => text.Text = line + "\n" + text.Text);
}
