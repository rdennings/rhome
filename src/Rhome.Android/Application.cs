using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Rhome.Android;

[global::Android.App.Application]
public class Application : AvaloniaAndroidApplication<App.App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
