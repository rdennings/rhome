using Android.App;
using Android.Hardware.Display;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;

namespace SpikeB.Android;

/// <summary>
/// Shows an Avalonia control on a second display (the Ayn Thor's bottom screen) through an Android
/// <see cref="Presentation"/>. A presentation stays on screen even when our app is in the background,
/// where it would cover an emulator's bottom screen, so it's only shown while the activity is started.
/// </summary>
sealed class SecondScreen(Activity activity)
{
    Control? _content;
    Presentation? _presentation;
    AvaloniaView? _view;

    /// <summary>The first display Android offers for presentations, or null on a one-screen device.</summary>
    public Display? FindDisplay()
    {
        var displays = (DisplayManager)activity.GetSystemService(global::Android.Content.Context.DisplayService)!;
        return displays.GetDisplays(DisplayManager.DisplayCategoryPresentation).FirstOrDefault();
    }

    /// <summary>Puts <paramref name="content"/> on the second screen. Returns false if there's none.</summary>
    public bool Show(Control content)
    {
        if (FindDisplay() is null)
            return false;
        _content = content;
        Attach();
        return true;
    }

    /// <summary>Activity started: put the content back.</summary>
    public void OnStart()
    {
        if (_content is not null && _presentation is null)
            Attach();
    }

    /// <summary>Activity stopped: take the presentation down so the emulator gets its screen back.</summary>
    public void OnStop()
    {
        if (_view is not null)
            _view.Content = null; // a control can only have one parent; free it for the next view
        _presentation?.Dismiss();
        _presentation = null;
        _view = null;
    }

    void Attach()
    {
        if (FindDisplay() is not { } display || _content is null)
            return;
        var presentation = new Presentation(activity, display);
        _view = new AvaloniaView(presentation.Context) { Content = _content };
        presentation.SetContentView(_view);
        try
        {
            presentation.Show();
            _presentation = presentation;
        }
        catch (WindowManagerInvalidDisplayException)
        {
            // The display went away between finding it and showing on it.
            _view.Content = null;
            _view = null;
        }
    }
}
