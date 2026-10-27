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

    /// <summary>How many damaged layout files are kept beside it (the newest).</summary>
    private const int BadFilesKept = 3;

    /// <summary>The waits between the tries at reading a layout file that is locked for a moment (a scanner, a backup tool).</summary>
    private static readonly TimeSpan[] ReadDelays = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];

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
                return ReadOwn(layoutPath);
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

    /// <summary>
    /// Our own layout file, read again while it is locked for a moment. One that is there but does not parse is copied aside as
    /// desktop-icons.bad-&lt;time&gt;.json before it is given up: the arranger's first save replaces it, and the order it held would be gone.
    /// </summary>
    private static IconLayoutSettings ReadOwn(string path)
    {
        string text = ReadWithRetries(path);
        try
        {
            return JsonSerializer.Deserialize<IconLayoutSettings>(text, JsonOptions) ?? throw new InvalidDataException($"{path} is empty");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            KeepBadFile(path);
            throw;
        }
    }

    private static string ReadWithRetries(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < ReadDelays.Length)
            {
                Thread.Sleep(ReadDelays[attempt]);
            }
        }
    }

    private static void KeepBadFile(string path)
    {
        try
        {
            string folder = Path.GetDirectoryName(path)!;
            string stem = Path.GetFileNameWithoutExtension(path);
            string kept = Path.Combine(folder, $"{stem}.bad-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Copy(path, kept, overwrite: true);
            foreach (string old in Directory.GetFiles(folder, stem + ".bad-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BadFilesKept))
                File.Delete(old);
            Log.Warn($"desktop icons: the damaged layout file is kept as {Path.GetFileName(kept)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("desktop icons: keeping the damaged layout file aside failed", ex);
        }
    }
}
