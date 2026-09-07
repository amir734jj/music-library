using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace MusicLibrary.App.Android;

[Application]
public sealed class Application(nint javaReference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<MusicLibrary.App.App>(javaReference, transfer);
