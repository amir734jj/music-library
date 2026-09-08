using Android.Runtime;
using Avalonia.Android;

namespace MusicLibrary.App.Android;

[Application(UsesCleartextTraffic = true)]
public sealed class Application(nint javaReference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<App>(javaReference, transfer);
