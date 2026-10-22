namespace DeskWidgets.Core.Diagnostics;

/// <summary>
/// Small file logger. The file is capped at <see cref="MaxBytes"/> and rotated
/// to app.old.log, so it can never grow without bound. Log events, not data
/// dumps: one short line per noteworthy thing.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = DataRoot.LogsDirectory;

    private static readonly string FilePath = Path.Combine(LogDirectory, "app.log");
    private static readonly string OldFilePath = Path.Combine(LogDirectory, "app.old.log");

    public static void Info(string message) => Write("INFO", message);

    /// <summary>One line: the exception's type, HRESULT and message (COM errors often come with an empty message).</summary>
    public static void Warn(string message, Exception? ex = null)
        => Write("WARN", ex is null ? message : $"{message}: {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}".TrimEnd());

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, OldFilePath, overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
