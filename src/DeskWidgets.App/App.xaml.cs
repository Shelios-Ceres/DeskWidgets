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
        // Design QA: capture either look without switching Windows (the cards, the Glass panel and our own popups all follow it).
        string[] arguments = Environment.GetCommandLineArgs();
        if (Has(arguments, "--force-dark") || Has(arguments, "--force-light"))
        {
            bool dark = Has(arguments, "--force-dark");
            RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            PixelUi.PixelPopupWindow.Forced = dark;
        }
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

        int folderPreview = Array.FindIndex(commandLine, a => a.Equals("--pixel-folder-preview", StringComparison.OrdinalIgnoreCase));
        if (folderPreview >= 0)
        {
            // Design aid: draw the Big Folder's Pixel look with sample data to PNG sheets and quit (works while the app is running).
            string folder = folderPreview + 1 < commandLine.Length ? commandLine[folderPreview + 1] : Path.Combine(AppContext.BaseDirectory, "pixel-folder-preview");
            Widgets.Folder.FolderPixelPreview.Render(folder);
            Exit();
            return;
        }

        int menuPreview = Array.FindIndex(commandLine, a => a.Equals("--pixel-menu-preview", StringComparison.OrdinalIgnoreCase));
        if (menuPreview >= 0)
        {
            // Design aid: draw every Pixel menu, dialog and the tray icon to PNG sheets and quit (works while the app is running).
            string folder = menuPreview + 1 < commandLine.Length ? commandLine[menuPreview + 1] : Path.Combine(AppContext.BaseDirectory, "pixel-menu-preview");
            PixelUi.MenuPreview.Render(folder);
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
        string? open = commandLine.FirstOrDefault(a => a.StartsWith("--pixel-ui-open=", StringComparison.OrdinalIgnoreCase));
        if (open is not null)
        {
            // Design QA: open a menu, dialog or Big Folder panel without a mouse.
            string? Value(string name) => commandLine.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) is { } found ? found[(found.IndexOf('=') + 1)..] : null;
            int Number(string name, int fallback) => int.TryParse(Value(name), out int number) ? number : fallback;
            var state = new Widgets.Folder.FolderCapture(Value("--folder-panel-search") ?? "", Number("--folder-panel-select", 0), Has(commandLine, "--folder-panel-rename"),
                Number("--folder-panel-hover", -1), Number("--folder-panel-focus", -1),
                Enum.TryParse<Widgets.Folder.DropVerb>(Value("--folder-card-drop"), ignoreCase: true, out var verb) ? verb : null);
            _host.OpenForCapture(open[(open.IndexOf('=') + 1)..], Has(commandLine, "--pixel-ui-zh"), state);
        }
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
