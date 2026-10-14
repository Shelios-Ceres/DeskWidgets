using System.Text.Json;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Desktop;

/// <summary>
/// The icon arranger's files in %LOCALAPPDATA%\DeskWidgets: the learnt layout
/// (desktop-icons.json) and the layout from before any arranging
/// (desktop-icons-original.json, written once and never overwritten). When they
/// do not exist yet, the standalone DesktopRightAlign tool's files are taken
/// over, so the icon order and its restore point carry on.
/// </summary>
internal static class IconStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets");

    private static readonly string LegacyFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tools", "DesktopRightAlign", "data");

    public static string LayoutPath => Path.Combine(Folder, "desktop-icons.json");

    public static string OriginalPath => Path.Combine(Folder, "desktop-icons-original.json");

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

    public static void SaveLayout(IconLayoutSettings layout) => Write(LayoutPath, layout, overwrite: true);

    public static DesktopSnapshot? LoadOriginal() => File.Exists(OriginalPath) ? Read<DesktopSnapshot>(OriginalPath) : null;

    /// <summary>
    /// Makes sure a readable restore point exists before anything moves:
    /// DesktopRightAlign's if it left one, else <paramref name="current"/>.
    /// An existing backup is never replaced.
    /// </summary>
    public static void EnsureOriginal(DesktopSnapshot current)
    {
        if (File.Exists(OriginalPath))
        {
            _ = Read<DesktopSnapshot>(OriginalPath);
            return;
        }
        Directory.CreateDirectory(Folder);
        string legacy = Path.Combine(LegacyFolder, "original-layout.json");
        if (File.Exists(legacy))
        {
            _ = Read<DesktopSnapshot>(legacy);
            File.Copy(legacy, OriginalPath, overwrite: false);
            Log.Info("desktop icons: took over DesktopRightAlign's original-layout backup");
            return;
        }
        Write(OriginalPath, current, overwrite: false);
    }

    private static T Read<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"{path} is empty");

    private static void Write<T>(string path, T value, bool overwrite)
    {
        Directory.CreateDirectory(Folder);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temp, path, overwrite);
    }
}
