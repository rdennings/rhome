using Android.App;
using Android.Hardware.Display;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Threading;
using Rhome.App;
using Display = Android.Views.Display;

namespace Rhome.Android;

/// <summary>
/// The bottom screen of a two-screen device such as the Ayn Thor (issue #49), as Spike B found it: a second Android
/// display flagged for presentations, so an Android <see cref="Presentation"/> with its own Avalonia view can draw on
/// it, with no permission. A presentation stays up while rhome is in the background, where it would cover an
/// emulator's bottom screen, so it's only shown while the activity is started (<see cref="OnStart"/>/<see cref="OnStop"/>).
/// </summary>
sealed class AndroidSecondScreen : Java.Lang.Object, DisplayManager.IDisplayListener, ISecondScreen
{
    readonly Activity activity;
    readonly DisplayManager displays;
    Control? content;
    Presentation? presentation;
    AvaloniaView? view;
    bool started;

    public AndroidSecondScreen(Activity activity)
    {
        this.activity = activity;
        displays = (DisplayManager)activity.GetSystemService(global::Android.Content.Context.DisplayService)!;
        IsPresent = FindDisplay() is not null;
        displays.RegisterDisplayListener(this, handler: null);
    }

    public bool IsPresent { get; private set; }

    public event Action? PresenceChanged;

    public void Show(Control? content)
    {
        this.content = content;
        if (view is not null)
            view.Content = content;
        else if (started)
            Attach();
    }

    /// <summary>The activity is in front: put the bottom screen back.</summary>
    public void OnStart()
    {
        started = true;
        if (presentation is null)
            Attach();
    }

    /// <summary>The activity went to the background: give the bottom screen back to whatever's in front.</summary>
    public void OnStop()
    {
        started = false;
        Detach();
    }

    /// <summary>The first display Android offers for presentations, or null on a one-screen device.</summary>
    Display? FindDisplay() => displays.GetDisplays(DisplayManager.DisplayCategoryPresentation)?.FirstOrDefault();

    void Attach()
    {
        if (content is null || FindDisplay() is not { } display)
            return;
        var shown = new Presentation(activity, display);
        view = new AvaloniaView(shown.Context) { Content = content };
        shown.SetContentView(view);
        try
        {
            shown.Show();
            presentation = shown;
        }
        catch (WindowManagerInvalidDisplayException)
        {
            // The display went away between finding it and showing on it.
            view.Content = null;
            view = null;
        }
    }

    void Detach()
    {
        if (view is not null)
            view.Content = null; // a control can only have one parent: free it for the next view
        presentation?.Dismiss();
        presentation = null;
        view = null;
    }

    void Changed()
    {
        var present = FindDisplay() is not null;
        if (present == IsPresent)
            return;
        IsPresent = present;
        Detach();
        if (present && started)
            Attach();
        Dispatcher.UIThread.Post(() => PresenceChanged?.Invoke());
    }

    public void OnDisplayAdded(int displayId) => Changed();

    public void OnDisplayRemoved(int displayId) => Changed();

    public void OnDisplayChanged(int displayId)
    {
    }
}
