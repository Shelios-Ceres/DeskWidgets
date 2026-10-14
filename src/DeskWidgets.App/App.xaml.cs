using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Desktop;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Widgets;
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

        // Maintenance commands that run and quit, alongside a running instance.
        int inspect = Array.FindIndex(commandLine, a => a.Equals("--icons-inspect", StringComparison.OrdinalIgnoreCase));
        if (inspect >= 0)
        {
            // Read-only: what the desktop icon arranger sees and would move.
            string path = inspect + 1 < commandLine.Length ? commandLine[inspect + 1] : Path.Combine(AppContext.BaseDirectory, "icons-inspect.json");
            try
            {
                IconArranger.Inspect(path, SavedCards());
            }
            catch (Exception ex)
            {
                File.WriteAllText(path, ex.ToString());
            }
            Exit();
            return;
        }
        if (Has(commandLine, "--enable-startup") || Has(commandLine, "--disable-startup"))
        {
            try
            {
                StartupRegistration.SetEnabled(Has(commandLine, "--enable-startup"));
            }
            catch (Exception ex)
            {
                Log.Error("Changing Start with Windows failed", ex);
            }
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

        bool demo = Has(commandLine, "--demo");
        Log.Info(demo ? "starting (demo devices)" : Has(commandLine, "--autostart") ? "starting (sign-in task)" : "starting");
        _host = new WidgetHost(demo);
        _host.Start();
    }

    private static bool Has(string[] commandLine, string flag) => commandLine.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The saved widgets' cards in screen pixels (the widgets themselves may belong to the running instance).</summary>
    private static List<Win32.RECT> SavedCards()
    {
        var settings = new SettingsStore().Current;
        return settings.Widgets.Select(widget =>
        {
            double scale = Win32.GetScaleAt(new Win32.POINT { X = widget.X + 1, Y = widget.Y + 1 });
            var card = WidgetLayout.GetCardPixels(widget.Size, settings.Theme, scale);
            return new Win32.RECT { Left = widget.X, Top = widget.Y, Right = widget.X + card.Width, Bottom = widget.Y + card.Height };
        }).ToList();
    }
}
