namespace DeskWidgets.Widgets.Folder;

/// <summary>What a drop does to one item (the values are the OLE DROPEFFECT flags).</summary>
internal enum DropEffect : uint
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
}

/// <summary>One thing being dropped. <see cref="Path"/> is null for a virtual item (Start menu, AppsFolder).</summary>
internal sealed record DropEntry(string? Path, bool IsFolder, string Name)
{
    public bool IsVirtual => Path is null;
}

/// <param name="Rule">Why, in words, for logs and tests.</param>
/// <param name="Target">For a shortcut: what it points to when that is not the dropped item itself (a portable app's exe).</param>
internal sealed record Decision(DropEffect Effect, string Rule, string? Target = null);

/// <summary>What the hover label says for a whole drop.</summary>
internal enum DropVerb
{
    None,
    Move,
    Copy,
    Shortcut,
    Mixed,
}

/// <summary>
/// The Big Folder's drop table: programs are never moved, ordinary items from the desktop always move, everything else follows Explorer's rules.
/// Pure logic; the only file-system question it asks (is there an exe directly inside this folder)
/// goes through <c>portableExe</c> so tests can answer it.
/// </summary>
internal sealed class DropPolicy
{
    private static readonly string[] JunkExeNames = ["unins", "uninstall", "update", "updater", "setup", "install", "crashpad", "vc_redist", "vcredist", "dxsetup"];

    private readonly Func<string, string?> _portableExe;

    /// <summary>The portable exe of each dropped folder, found once: DragOver asks about the same items dozens of times a second.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DropEntry, ExeFact> _exeFacts = new();

    private sealed class ExeFact(string? exe)
    {
        public string? Exe { get; } = exe;
    }

    /// <param name="protectedRoots">Everything under these becomes a shortcut, even with Shift (Program Files, Windows, ProgramData).</param>
    /// <param name="programOnlyRoots">Only programs under these become shortcuts (AppData); anything else follows the ordinary rules.</param>
    /// <param name="desktopRoots">The Desktop and the Public Desktop: ordinary items taken from there move into the folder, even across drives.</param>
    public DropPolicy(IEnumerable<string> protectedRoots, IEnumerable<string> programOnlyRoots, IEnumerable<string> desktopRoots, Func<string, string?>? portableExe = null)
    {
        ProtectedRoots = Normalize(protectedRoots);
        ProgramOnlyRoots = Normalize(programOnlyRoots);
        DesktopRoots = Normalize(desktopRoots);
        _portableExe = portableExe ?? FindPortableExe;
    }

    public string[] ProtectedRoots { get; }

    public string[] ProgramOnlyRoots { get; }

    public string[] DesktopRoots { get; }

    /// <summary>The policy for this PC's known folders.</summary>
    public static DropPolicy ForThisPc() => new(
        protectedRoots: Known(ShellNative.FOLDERID_ProgramFiles, ShellNative.FOLDERID_ProgramFilesX86, ShellNative.FOLDERID_Windows, ShellNative.FOLDERID_ProgramData),
        programOnlyRoots: Known(ShellNative.FOLDERID_RoamingAppData, ShellNative.FOLDERID_LocalAppData),
        desktopRoots: Known(ShellNative.FOLDERID_Desktop, ShellNative.FOLDERID_PublicDesktop));

    private static IEnumerable<string> Known(params Guid[] ids) => ids.Select(Shell.KnownFolder).Where(p => p is not null)!;

    private static string[] Normalize(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrEmpty(p)).Select(Normalize).ToArray();

    public static string Normalize(string path) => System.IO.Path.GetFullPath(path).TrimEnd('\\').ToUpperInvariant();

    private static bool IsUnder(string path, string[] roots)
    {
        string full = Normalize(path);
        return roots.Any(root => full == root || full.StartsWith(root + "\\", StringComparison.Ordinal));
    }

    public bool IsOnDesktop(string path) => System.IO.Path.GetDirectoryName(path) is { } parent && DesktopRoots.Contains(Normalize(parent));

    /// <summary>Same drive letter means same volume. A junction or mount point onto another volume is not recognised.</summary>
    public static bool SameVolume(string a, string b)
        => string.Equals(System.IO.Path.GetPathRoot(a), System.IO.Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Explorer's rule for ordinary items: same volume moves, another volume copies; Ctrl copies, Shift moves, Alt or Ctrl+Shift makes a shortcut.</summary>
    public static DropEffect ExplorerDefault(string source, string destinationFolder, DropKeys keys)
    {
        if (keys.Alt || (keys.Ctrl && keys.Shift))
            return DropEffect.Link;
        if (keys.Ctrl)
            return DropEffect.Copy;
        if (keys.Shift)
            return DropEffect.Move;
        return SameVolume(source, destinationFolder) ? DropEffect.Move : DropEffect.Copy;
    }

    /// <summary>
    /// The executable inside a portable-app folder, or null when there is none directly inside it:
    /// the exe named like the folder, else the biggest one; uninstallers, updaters and the like are skipped.
    /// </summary>
    public static string? FindPortableExe(string folder)
    {
        string[] exes;
        try
        {
            exes = Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return null;
        }
        var candidates = exes.Where(exe =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
            return !JunkExeNames.Any(junk => name.StartsWith(junk, StringComparison.Ordinal));
        }).ToArray();
        if (candidates.Length == 0)
            return null;
        string folderName = new DirectoryInfo(folder).Name;
        return candidates.FirstOrDefault(exe => string.Equals(System.IO.Path.GetFileNameWithoutExtension(exe), folderName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.OrderByDescending(exe => new FileInfo(exe).Length).First();
    }

    private bool IsProgram(DropEntry entry, out string? exe)
    {
        exe = null;
        if (entry.Path is null)
            return false;
        if (!entry.IsFolder)
            return entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        exe = _exeFacts.GetValue(entry, e => new ExeFact(_portableExe(e.Path!))).Exe;
        return exe is not null;
    }

    public Decision Decide(DropEntry entry, DropKeys keys, string destinationFolder)
    {
        if (entry.Path is null)
            return new Decision(DropEffect.Link, "virtual item (Start or AppsFolder): shortcut");

        string path = entry.Path;
        if (System.IO.Path.GetDirectoryName(path) is { } parent && Normalize(parent) == Normalize(destinationFolder))
            return new Decision(DropEffect.None, "already in the folder");

        bool program = IsProgram(entry, out string? exe);
        if (IsUnder(path, ProtectedRoots))
            return new Decision(DropEffect.Link, "under a protected place: shortcut only, even with Shift", exe);
        if (program && IsUnder(path, ProgramOnlyRoots))
            return new Decision(DropEffect.Link, "a program under AppData: shortcut only, even with Shift", exe);

        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (!entry.IsFolder && (extension == ".lnk" || extension == ".url") && IsOnDesktop(path))
            return new Decision(keys.Ctrl ? DropEffect.Copy : DropEffect.Move, "shortcut from the desktop: moves in");

        if (program)
        {
            string what = entry.IsFolder ? "portable-app folder" : "program";
            if (keys.Ctrl && !keys.Alt)
                return new Decision(DropEffect.Copy, what + " with Ctrl: copy");
            if (keys.Shift && !keys.Alt)
                return new Decision(DropEffect.Move, what + " with Shift: moves");
            return new Decision(DropEffect.Link, what + ": new shortcut, the original stays", exe);
        }
        // Whatever sits on the desktop moves in, even from another drive (Explorer would copy). The user keeps their things in this folder instead of on the desktop.
        if (IsOnDesktop(path))
            return new Decision(DesktopDefault(keys), "ordinary item from the desktop: moves in, even across drives");
        return new Decision(ExplorerDefault(path, destinationFolder, keys), "ordinary item: Explorer's rule");
    }

    /// <summary>The rule for an ordinary item taken from the desktop: it moves whatever the drive; Ctrl copies, Alt or Ctrl+Shift makes a shortcut.</summary>
    public static DropEffect DesktopDefault(DropKeys keys)
    {
        if (keys.Alt || (keys.Ctrl && keys.Shift))
            return DropEffect.Link;
        return keys.Ctrl ? DropEffect.Copy : DropEffect.Move;
    }

    /// <summary>What a whole drop is called: all items agree, or "Add to" for a mix. Items that need no work do not count.</summary>
    public static DropVerb Summarize(IEnumerable<Decision> decisions)
    {
        var effects = decisions.Select(d => d.Effect).Where(e => e != DropEffect.None).Distinct().ToList();
        if (effects.Count == 0)
            return DropVerb.None;
        if (effects.Count > 1)
            return DropVerb.Mixed;
        return effects[0] switch
        {
            DropEffect.Move => DropVerb.Move,
            DropEffect.Copy => DropVerb.Copy,
            _ => DropVerb.Shortcut,
        };
    }

    /// <summary>The one DROPEFFECT handed back to the drag source (what the cursor shows), limited to what the source allows.</summary>
    public static DropEffect CursorEffect(IReadOnlyCollection<Decision> decisions, DropEffect allowed)
    {
        var effects = decisions.Select(d => d.Effect).Where(e => e != DropEffect.None).Distinct().ToList();
        if (effects.Count == 0)
            return DropEffect.None;
        var wanted = effects.Count > 1 ? DropEffect.Copy : effects[0];
        if ((wanted & allowed) != 0)
            return wanted;
        // The source does not allow it (a Start-menu drag allows only copy and link): take what it does allow.
        foreach (var fallback in new[] { DropEffect.Link, DropEffect.Copy, DropEffect.Move })
        {
            if ((fallback & allowed) != 0)
                return fallback;
        }
        return DropEffect.None;
    }
}

/// <summary>The modifier keys held during a drag.</summary>
internal readonly record struct DropKeys(bool Ctrl, bool Shift, bool Alt)
{
    public static readonly DropKeys None = new(false, false, false);

    public static DropKeys FromOle(uint keyState) => new(
        (keyState & ShellNative.MK_CONTROL) != 0,
        (keyState & ShellNative.MK_SHIFT) != 0,
        (keyState & ShellNative.MK_ALT) != 0);
}
