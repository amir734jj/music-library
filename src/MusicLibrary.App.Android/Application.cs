using Android.Runtime;
using Avalonia.Android;
using MusicLibrary.App.Services;
using Serilog;

namespace MusicLibrary.App.Android;

[Application(UsesCleartextTraffic = true)]
public sealed class Application(nint javaReference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<App>(javaReference, transfer)
{
	public override void OnCreate()
	{
		MusicLibraryApi.Configure(new Uri("https://music-library.coolify.hesamian.com/"));
		_ = InitializeLoggingAsync();
		AndroidEnvironment.UnhandledExceptionRaiser += (_, eventArgs) =>
			Log.Fatal(eventArgs.Exception, "Unhandled Android application exception");
		TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
		{
			Log.Error(eventArgs.Exception, "Unobserved Android task exception");
			eventArgs.SetObserved();
		};
		base.OnCreate();
	}

	private static async Task InitializeLoggingAsync()
	{
		if (await AppLogging.ConfigureFromApiAsync("android"))
		{
			Log.Information("Starting Music Library Android application");
		}
	}
}
