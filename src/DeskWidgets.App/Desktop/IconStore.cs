using System.Text.Json;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Desktop;

/// <summary>
/// The icon arranger's file in %LOCALAPPDATA%\DeskWidgets: the learnt layout
/// (desktop-icons.json). When it does not exist yet, the standalone
/// DesktopRightAlign tool's settings are taken over, so the icon order carries on.
/// </summary>
internal static class IconStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets");

    private static readonly string LegacyFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tools", "DesktopRightAlign", "data");

    public static string LayoutPath => Path.Combine(Folder, "desktop-icons.json");

    /// <param name="save">Write a layout taken over from DesktopRightAlign to our own file.</param>
    public static IconLayoutSettings LoadLayout(bool save = true)
    {
        try
        {
            if (File.Exists(LayoutPath))
                return Read<IconLayoutSettings>(LayoutPath);
            string legacy = Path.Combine(LegacyFolder, "settings.json");
            if (File.Exists(legacy))
            {
                var layout = Read<IconLayoutSettings>(legacy);
                if (save)
                {
                    SaveLayout(layout);
                    Log.Info($"desktop icons: took over DesktopRightAlign's icon order ({layout.Order.Count} entries)");
                }
                return layout;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Warn("desktop icons: layout file unreadable, starting a new one", ex);
        }
        return new IconLayoutSettings();
    }

    public static void SaveLayout(IconLayoutSettings layout)
    {
        Directory.CreateDirectory(Folder);
        string temp = LayoutPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(layout, JsonOptions));
        File.Move(temp, LayoutPath, overwrite: true);
    }

    private static T Read<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"{path} is empty");
}
