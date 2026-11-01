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

    /// <summary>Where new Big Folders are made. Null (absent from the file) keeps the built-in default: under the user profile, on the Desktop's drive.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderRoot { get; set; }

    public List<WidgetPlacement> Widgets { get; set; } = [];

    /// <summary>The docks (Phase 15): at most one per screen, saved with their programs. A file from before the dock has none.</summary>
    public List<Dock.DockSettings> Docks { get; set; } = [];
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

    /// <summary>Takes the window's position; false when the placement already says so, and there is nothing to save.</summary>
    public bool MoveTo(int x, int y)
    {
        if (X == x && Y == y)
            return false;
        (X, Y) = (x, y);
        return true;
    }
}

/// <summary>What loading settings.json did besides reading it (Phase 13.1): nothing, restored the newest good snapshot, or started fresh.</summary>
internal enum SettingsRecoveryKind
{
    None,
    FromBackup,
    Fresh,

    /// <summary>The file exists but could not be read at all (locked, access denied) even after retries: a snapshot is used from memory, the file is left alone and saves are held until it can be read.</summary>
    Held,

    /// <summary>There was no settings.json although snapshots were there (it was deleted, by a cleaner or by hand): the newest snapshot that reads was put back.</summary>
    Missing,
}

internal readonly record struct SettingsRecovery(SettingsRecoveryKind Kind, DateTime BackupUtc = default, string? BadFile = null);

/// <summary>
/// JSON settings in settings.json under the data root (%LOCALAPPDATA%\DeskWidgets unless --data-dir says otherwise), written atomically.
/// Phase 13.1: good copies are kept in <c>backups\</c> (one when the app starts and at most one an hour while settings change), an unreadable
/// or empty file loads the newest snapshot that parses instead of starting fresh, and a failed save is reported to the caller.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const int BadFilesKept = 3;

    private readonly string _folder;
    private readonly string _file;
    private readonly string _backups;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private readonly TimeSpan[] _readDelays;
    private DateTime? _lastSnapshot;
    private bool _held;

    /// <summary>The waits between the tries at reading settings.json at start (a lock from a scanner or a backup tool is usually short): 6 tries over about 2 s.</summary>
    internal static readonly TimeSpan[] StartReadDelays = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)];

    public SettingsStore() : this(DataRoot.Path)
    {
    }

    internal SettingsStore(string folder, Func<DateTime>? utcNow = null, TimeSpan[]? readDelays = null)
    {
        _readDelays = readDelays ?? StartReadDelays;
        _folder = folder;
        _file = Path.Combine(folder, "settings.json");
        _backups = Path.Combine(folder, "backups");
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        (Current, Recovery) = Load();
    }

    public AppSettings Current { get; }

    /// <summary>What loading did besides reading the file; the host tells the user once.</summary>
    public SettingsRecovery Recovery { get; }

    /// <summary>Why the last save failed, or null when it worked.</summary>
    public Exception? LastError { get; private set; }

    private static AppSettings? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AppSettings? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? TryParse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The settings as they are on disk, for a command that runs beside the app (<c>--icons-inspect</c>): settings.json, else the newest
    /// snapshot that reads, else the defaults. Unlike opening a store it changes nothing: no bad file is kept and nothing is put back.
    /// </summary>
    public static AppSettings Peek(string folder)
    {
        List<(DateTime Time, string Path)> snapshots;
        try
        {
            snapshots = ListSnapshots(Path.Combine(folder, "backups"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            snapshots = [];
        }
        return TryRead(Path.Combine(folder, "settings.json"))
            ?? snapshots.OrderByDescending(s => s.Time).Select(s => TryRead(s.Path)).FirstOrDefault(s => s is not null)
            ?? new AppSettings();
    }

    private (AppSettings, SettingsRecovery) Load()
    {
        if (!File.Exists(_file))
        {
            // No file and no snapshots is a first run. No file beside snapshots means it was deleted: put the newest good one back.
            var snapshots = Snapshots();
            if (snapshots.Count == 0)
                return (new AppSettings(), default);
            Log.Warn("settings.json is missing although there are backups; restoring the newest that reads");
            return Restore(snapshots, SettingsRecoveryKind.Missing, bad: null)
                ?? (new AppSettings(), default); // none of them reads: start as on a first run
        }
        var (text, error) = ReadWithRetries(_file, _readDelays);
        if (text is null)
        {
            // The file is there but cannot be read (locked by a scanner or a backup tool, or denied): that is not a damaged file. Leave it alone,
            // show the newest snapshot, and hold every save until the file reads again, so the newer settings in it are never written over.
            Log.Warn("settings.json exists but could not be read; using the newest backup in memory and holding saves until it can be", error);
            _held = true;
            var newest = Snapshots().OrderByDescending(s => s.Time).Select(s => (AppSettings?)TryRead(s.Path)).FirstOrDefault(r => r is not null);
            return (newest ?? new AppSettings(), new SettingsRecovery(SettingsRecoveryKind.Held));
        }
        var parsed = TryParse(text);
        if (parsed is not null)
            return (parsed, default);

        // Unreadable or empty: keep the bad file, then take the newest snapshot that parses.
        Log.Warn("settings.json is unreadable or empty; looking for a backup");
        string? bad = KeepBadFile();
        if (Restore(Snapshots(), SettingsRecoveryKind.FromBackup, bad) is { } restored)
            return restored;
        Log.Warn("No usable backup; starting fresh");
        return (new AppSettings(), new SettingsRecovery(SettingsRecoveryKind.Fresh, default, bad));
    }

    /// <summary>Puts the newest snapshot that parses back as settings.json (or uses it from memory if that fails); null when none parses.</summary>
    private (AppSettings, SettingsRecovery)? Restore(List<(DateTime Time, string Path)> snapshots, SettingsRecoveryKind kind, string? bad)
    {
        foreach (var (time, path) in snapshots.OrderByDescending(s => s.Time))
        {
            var restored = TryRead(path);
            if (restored is null)
                continue;
            try
            {
                Directory.CreateDirectory(_folder);
                File.Copy(path, _file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Putting the backup back as settings.json failed; it is used from memory", ex);
            }
            Log.Warn($"settings.json restored from the backup of {time:u}");
            return (restored, new SettingsRecovery(kind, time, bad));
        }
        return null;
    }

    /// <summary>Reads a file, trying again after each wait while it is locked or denied. A null text means it never could be read; the last error says why.</summary>
    private static (string? Text, Exception? Error) ReadWithRetries(string path, TimeSpan[] delays)
    {
        Exception? last = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return (File.ReadAllText(path), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
            }
            if (attempt >= delays.Length)
                return (null, last);
            Thread.Sleep(delays[attempt]);
        }
    }

    /// <summary>Copies the unreadable file aside as settings.bad-&lt;time&gt;.json (the newest few are kept); returns its name.</summary>
    private string? KeepBadFile()
    {
        try
        {
            string name = $"settings.bad-{_utcNow():yyyyMMdd-HHmmss}.json";
            File.Copy(_file, Path.Combine(_folder, name), overwrite: true);
            foreach (string old in Directory.GetFiles(_folder, "settings.bad-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BadFilesKept))
                File.Delete(old);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private List<(DateTime Time, string Path)> Snapshots()
    {
        try
        {
            return ListSnapshots(_backups);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Listing the settings backups failed", ex);
            return [];
        }
    }

    private static List<(DateTime Time, string Path)> ListSnapshots(string backups)
    {
        var list = new List<(DateTime, string)>();
        foreach (string path in Directory.Exists(backups) ? Directory.GetFiles(backups, "settings-*.json") : [])
        {
            if (SettingsBackups.TryParse(Path.GetFileName(path), out var time))
                list.Add((time, path));
        }
        return list;
    }

    /// <summary>The app has started: keep a copy of the file as it was loaded (unless the newest snapshot is already the same).</summary>
    public void StartSession()
    {
        lock (_gate)
        {
            if (_held)
                return; // the file cannot be read: nothing to keep
            var snapshots = Snapshots();
            _lastSnapshot = snapshots.Count == 0 ? null : snapshots.Max(s => s.Time);
            if (!File.Exists(_file))
                return;
            try
            {
                var newest = snapshots.OrderByDescending(s => s.Time).FirstOrDefault();
                if (newest.Path is not null && File.ReadAllBytes(newest.Path).AsSpan().SequenceEqual(File.ReadAllBytes(_file)))
                    return; // already kept
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            Snapshot();
        }
    }

    /// <summary>Copies settings.json into the backups folder and drops the snapshots that are no longer wanted. Never throws.</summary>
    private void Snapshot()
    {
        try
        {
            Directory.CreateDirectory(_backups);
            var now = _utcNow();
            string target = Path.Combine(_backups, SettingsBackups.NameFor(now));
            if (!File.Exists(target))
                File.Copy(_file, target);
            _lastSnapshot = now;
            foreach (var time in SettingsBackups.ToDelete(Snapshots().Select(s => s.Time), now))
                File.Delete(Path.Combine(_backups, SettingsBackups.NameFor(time)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Making a settings backup failed", ex);
        }
    }

    /// <summary>
    /// A save while the file could not be read at start: try to read it now. If it still cannot be read, nothing is written (the error is the
    /// reason). Once it can, the file as it is is kept aside as settings.kept-&lt;time&gt;.json before our (older, in-memory) state replaces it.
    /// One try only, unless <paramref name="patient"/>: an ordinary save runs on the UI thread, and the save timer tries again in a few seconds.
    /// </summary>
    private bool ReleaseHold(bool patient)
    {
        var (text, error) = ReadWithRetries(_file, patient ? [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300)] : []);
        if (text is null)
        {
            LastError = error;
            Log.Warn("Saving held: settings.json still cannot be read", error);
            return false;
        }
        try
        {
            string kept = Path.Combine(_folder, $"settings.kept-{_utcNow():yyyyMMdd-HHmmss}.json");
            File.WriteAllText(kept, text);
            Log.Warn($"settings.json can be read again; its content is kept as {Path.GetFileName(kept)} before the settings of this session replace it");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex;
            Log.Warn("Keeping settings.json aside failed; saving stays held", ex);
            return false;
        }
        _held = false;
        return true;
    }

    /// <summary>
    /// Writes settings.json (a temp file flushed to disk, then moved over it). False when it could not be written; <see cref="LastError"/> says why.
    /// <paramref name="patient"/> is for the last save (exit, the session ending): a file held since the start is waited for a moment.
    /// </summary>
    public bool Save(bool patient = false)
    {
        lock (_gate)
        {
            if (_held && !ReleaseHold(patient))
                return false;
            try
            {
                Directory.CreateDirectory(_folder);
                string temp = _file + ".tmp";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Current, JsonOptions));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, _file, overwrite: true);
            }
            catch (Exception ex)
            {
                LastError = ex;
                Log.Warn("Saving settings failed", ex);
                return false;
            }
            LastError = null;
            if (SettingsBackups.Due(_lastSnapshot, _utcNow()))
                Snapshot();
            return true;
        }
    }
}
