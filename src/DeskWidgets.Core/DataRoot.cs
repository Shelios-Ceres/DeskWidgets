namespace DeskWidgets.Core;

/// <summary>
/// The one place that decides where the app keeps its data (settings.json, desktop-icons.json,
/// folder-origins\, logs\). Normally that is %LOCALAPPDATA%\DeskWidgets. A live check starts the
/// app with <c>--data-dir &lt;folder&gt;</c> (or sets DESKWIDGETS_DATA_DIR) and gets a test
/// instance: its own data root, and no way to touch the user's logon task, desktop icons or
/// profile folders.
/// </summary>
public static class DataRoot
{
    public const string Flag = "--data-dir";
    public const string EnvironmentVariable = "DESKWIDGETS_DATA_DIR";

    private static readonly Resolution Current = Resolve(
        Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(EnvironmentVariable), DefaultPath());

    /// <summary>The data folder in use.</summary>
    public static string Path => Current.Path;

    /// <summary>True when the data folder was chosen with <c>--data-dir</c> or the environment variable.</summary>
    public static bool IsTestInstance => Current.IsTestInstance;

    /// <summary>Set when <c>--data-dir</c> or the variable is present but unusable. The app quits then: carrying on would fall back to the user's own data.</summary>
    public static string? Error => Current.Error;

    public static string SettingsFile => System.IO.Path.Combine(Path, "settings.json");

    public static string LogsDirectory => System.IO.Path.Combine(Path, "logs");

    public static string FolderOrigins => System.IO.Path.Combine(Path, "folder-origins");

    /// <summary>Where a test instance's new Big Folders are made, instead of the user's profile.</summary>
    public static string BigFolders => System.IO.Path.Combine(Path, "Big Folders");

    public static string DefaultPath()
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets");

    public readonly record struct Resolution(string Path, bool IsTestInstance, string? Error);

    /// <summary>
    /// The flag wins over the variable, which wins over <paramref name="defaultPath"/>. Both
    /// <c>--data-dir folder</c> and <c>--data-dir=folder</c> work; a relative folder is taken
    /// against the current directory. An unusable value is an error, never a silent fallback.
    /// </summary>
    public static Resolution Resolve(IReadOnlyList<string> arguments, string? environmentValue, string defaultPath)
    {
        string? value = null;
        bool given = false;
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            if (argument.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument[(Flag.Length + 1)..];
                given = true;
            }
            else if (argument.Equals(Flag, StringComparison.OrdinalIgnoreCase))
            {
                value = i + 1 < arguments.Count ? arguments[i + 1] : null;
                given = true;
            }
        }
        string source = Flag;
        if (!given && environmentValue is not null)
        {
            value = environmentValue;
            given = true;
            source = EnvironmentVariable;
        }
        if (!given)
            return new Resolution(defaultPath, false, null);

        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
            return new Resolution(defaultPath, true, $"{source} needs a folder.");
        value = value.Trim().Trim('"');
        try
        {
            // GetFullPath lets wildcards and '|' through; they can never be part of a real folder.
            if (value.IndexOfAny(['*', '?', '<', '>', '|', '\0']) >= 0)
                throw new ArgumentException("invalid character");
            return new Resolution(System.IO.Path.GetFullPath(value), true, null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return new Resolution(defaultPath, true, $"{source} is not a usable folder: {value}");
        }
    }
}
