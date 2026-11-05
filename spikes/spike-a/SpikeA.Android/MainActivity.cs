using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace SpikeA.Android;

[Activity(
    Label = "rhome spike A",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
