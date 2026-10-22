using System.Text.Json;
using DeskWidgets.Core;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Desktop;

/// <summary>
/// The icon arranger's file under the data root: the learnt layout (desktop-icons.json).
/// When it does not exist yet, the standalone DesktopRightAlign tool's settings are taken
/// over, so the icon order carries on (not in a test instance, which stays out of the
/// user's profile folders).
/// </summary>
internal static class IconStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string Folder = DataRoot.Path;

    private static readonly string LegacyFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tools", "DesktopRightAlign", "data");

    public static string LayoutPath => Path.Combine(Folder, "desktop-icons.json");

    /// <param name="save">Write a layout taken over from DesktopRightAlign to our own file.</param>
    public static IconLayoutSettings LoadLayout(bool save = true)
        => LoadLayout(LayoutPath, LegacyFolder, takeOver: !DataRoot.IsTestInstance, save);

    /// <param name="takeOver">Whether DesktopRightAlign's settings in <paramref name="legacyFolder"/> may be read when <paramref name="layoutPath"/> does not exist yet.</param>
    internal static IconLayoutSettings LoadLayout(string layoutPath, string legacyFolder, bool takeOver, bool save)
    {
        try
        {
            if (File.Exists(layoutPath))
                return Read<IconLayoutSettings>(layoutPath);
            string legacy = Path.Combine(legacyFolder, "settings.json");
            if (takeOver && File.Exists(legacy))
            {
                var layout = Read<IconLayoutSettings>(legacy);
                if (save)
                {
                    SaveLayout(layout, layoutPath);
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

    public static void SaveLayout(IconLayoutSettings layout) => SaveLayout(layout, LayoutPath);

    private static void SaveLayout(IconLayoutSettings layout, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(layout, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static T Read<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"{path} is empty");
}
