using System.Text.Json;
using System.Text.Json.Serialization;
using DeskWidgets.Core;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets;

namespace DeskWidgets.Hosting;

internal sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>False until the first run has placed the default widget.</summary>
    public bool Initialized { get; set; }

    /// <summary>Snap widgets to the desktop icon grid (otherwise to screen edges and neighbours).</summary>
    public bool SnapToDesktopGrid { get; set; } = true;

    /// <summary>Keep the desktop icons arranged from the top-right corner, clear of the widgets.</summary>
    public bool ArrangeDesktopIcons { get; set; }

    public AppLanguage Language { get; set; } = AppLanguage.English;

    public WidgetTheme Theme { get; set; } = WidgetTheme.Glass;

    public List<WidgetPlacement> Widgets { get; set; } = [];
}

/// <summary>One widget on the desktop: its type, size, position and per-widget options.</summary>
internal sealed class WidgetPlacement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "";
    public WidgetSize Size { get; set; } = WidgetSize.Medium;

    /// <summary>Window position in physical screen pixels.</summary>
    public int X { get; set; }

    public int Y { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool Locked { get; set; }
    public Dictionary<string, string> Options { get; set; } = new();
}

/// <summary>JSON settings in settings.json under the data root (%LOCALAPPDATA%\DeskWidgets unless --data-dir says otherwise), written atomically.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string Folder = DataRoot.Path;

    private static readonly string FilePath = DataRoot.SettingsFile;

    public AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn("settings.json is unreadable; starting fresh (old file kept as settings.bad.json)", ex);
            try
            {
                File.Copy(FilePath, Path.Combine(Folder, "settings.bad.json"), overwrite: true);
            }
            catch (IOException)
            {
            }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Saving settings failed", ex);
        }
    }
}
