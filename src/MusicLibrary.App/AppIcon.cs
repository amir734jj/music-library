using Avalonia.Controls;

namespace MusicLibrary.App;

// Allows platform-specific hosts (e.g. Desktop) to supply a window icon without MainWindow depending on them.
public static class AppIcon
{
    public static WindowIcon? Icon { get; set; }
}
