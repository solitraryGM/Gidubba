using Avalonia;
using TableForge.Dice;

namespace TableForge;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The dddice dice window runs as its own process (GTK 4 + WebKitGTK), never inside the Avalonia one: see DddiceHostProgram.
        if (args.Length > 0 && args[0] == DddiceHostProgram.Flag) return DddiceHostProgram.Run(args);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
