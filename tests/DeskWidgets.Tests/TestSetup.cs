using System.Runtime.CompilerServices;
using DeskWidgets.Core;

// Strings.Language is process-wide and the GDI fallback font is single-threaded:
// run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DeskWidgets.Tests;

internal static class TestSetup
{
    /// <summary>The folder every test run uses as its data root, so a test never reads or writes the user's own settings and logs.</summary>
    public static string DataFolder { get; } = Path.Combine(Path.GetTempPath(), "DeskWidgets.Tests", "data");

    // Runs before any test, so DataRoot reads it the first time it is used.
    [ModuleInitializer]
    internal static void UseOwnDataFolder() => Environment.SetEnvironmentVariable(DataRoot.EnvironmentVariable, DataFolder);
}
