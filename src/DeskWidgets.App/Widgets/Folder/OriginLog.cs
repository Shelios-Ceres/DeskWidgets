using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// Where each item of a Big Folder came from and when it was added, so "Put back" can return it and "Newest" means
/// "most recently added" (a file moved in keeps its old dates). Keyed by the item's file name inside the folder.
/// A shortcut the widget created has no origin, only a date.
/// </summary>
internal sealed class OriginLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, OriginEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public sealed record OriginEntry(
        [property: JsonPropertyName("origin")] string? Origin,
        [property: JsonPropertyName("added")] DateTime AddedUtc);

    public int Count => _entries.Count;

    /// <summary>Notes that <paramref name="name"/> was added, from <paramref name="origin"/> (null when it was created here).</summary>
    public void Record(string name, string? origin, DateTime addedUtc) => _entries[name] = new OriginEntry(origin, addedUtc);

    public OriginEntry? Get(string name) => _entries.GetValueOrDefault(name);

    /// <summary>The folder (and name) to put an item back into, or null when it did not come from anywhere.</summary>
    public string? OriginOf(string name) => Get(name)?.Origin;

    public DateTime? AddedUtc(string name) => Get(name)?.AddedUtc;

    public void Remove(string name) => _entries.Remove(name);

    public void Rename(string oldName, string newName)
    {
        if (_entries.Remove(oldName, out var entry))
            _entries[newName] = entry;
    }

    /// <summary>Drops entries of items that are no longer in the folder.</summary>
    public bool Prune(IEnumerable<string> existingNames)
    {
        var keep = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var gone = _entries.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (string name in gone)
            _entries.Remove(name);
        return gone.Count > 0;
    }

    public string ToJson() => JsonSerializer.Serialize(_entries, JsonOptions);

    public static OriginLog FromJson(string json)
    {
        var log = new OriginLog();
        var entries = JsonSerializer.Deserialize<Dictionary<string, OriginEntry>>(json, JsonOptions);
        if (entries is not null)
        {
            foreach (var (name, entry) in entries)
                log._entries[name] = entry;
        }
        return log;
    }

    /// <summary>The log file for a folder: one per folder, named by a hash of its path, in the app's data folder.</summary>
    public static string PathFor(string folder)
    {
        string key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(folder.TrimEnd('\\').ToLowerInvariant())))[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets", "folder-origins", key + ".json");
    }

    public static OriginLog Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Warn("A Big Folder's origin log is unreadable; starting empty", ex);
        }
        return new OriginLog();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Saving a Big Folder's origin log failed", ex);
        }
    }
}
