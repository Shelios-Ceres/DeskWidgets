using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Hosting;
using Microsoft.UI.Xaml;

namespace DeskWidgets;

public partial class App : Application
{
    private static Mutex? s_singleInstance;
    private WidgetHost? _host;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // A widget bug must not take every other widget down with it.
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        int preview = Array.FindIndex(commandLine, a => a.Equals("--pixel-preview", StringComparison.OrdinalIgnoreCase));
        if (preview >= 0)
        {
            // Design aid: draw every pixel-theme layout to PNG sheets and quit (works while the app is running).
            string folder = preview + 1 < commandLine.Length ? commandLine[preview + 1] : Path.Combine(AppContext.BaseDirectory, "pixel-preview");
            Pixel.PixelPreview.Render(folder);
            Exit();
            return;
        }

        s_singleInstance = new Mutex(initiallyOwned: true, @"Local\DeskWidgets.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Exit();
            return;
        }

        // Widgets are windows that come and go; the tray icon keeps the app alive.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        bool demo = commandLine.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        Log.Info(demo ? "starting (demo devices)" : "starting");
        _host = new WidgetHost(demo);
        _host.Start();
    }
}
