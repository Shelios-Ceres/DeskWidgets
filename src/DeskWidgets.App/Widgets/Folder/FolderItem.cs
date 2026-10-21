using System.Globalization;
using System.Text.Json;

namespace DeskWidgets.Widgets.Folder;

/// <summary>One thing in a Big Folder, as the card and the panel show it.</summary>
internal sealed class FolderItem
{
    /// <summary>Full path of the entry inside the folder (for a shortcut, the .lnk itself).</summary>
    public required string Path { get; init; }

    /// <summary>The entry's file name, with extension: the key of the order list and the origin log.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>What the user reads: the file name, with the extension only if Explorer shows extensions. Shortcuts never show theirs.</summary>
    public required string DisplayName { get; init; }

    public bool IsFolder { get; init; }

    /// <summary>The icon type. For a shortcut to a document or folder, the target's type; for a shortcut to a program, App.</summary>
    public required FolderItemKind Kind { get; init; }

    /// <summary>The file's extension for the label on Document, Spreadsheet, Presentation and Other tiles (the target's, for a shortcut).</summary>
    public string LabelName { get; init; } = "";

    /// <summary>When the item was added (from the origin log), else the later of its creation and write times.</summary>
    public DateTime AddedUtc { get; init; }

    public bool IsShortcut { get; init; }

    /// <summary>A shortcut's target is missing (the program was uninstalled): the item is dimmed.</summary>
    public bool TargetMissing { get; init; }

    public AppIdentity? Identity { get; init; }

    /// <summary>The key of a custom app icon, or null (a real icon on a plate, or a type tile).</summary>
    public string? CustomIcon { get; init; }

    /// <summary>The thing the icon service asks the Shell about: the shortcut itself (the Shell resolves its target).</summary>
    public string IconPath => Path;

    /// <summary>The item is a program (a shortcut to one, an exe, an AppsFolder shortcut): it gets Run as administrator.</summary>
    public bool IsApp => Kind == FolderItemKind.App;
}

internal enum FolderSort
{
    Newest,
    Name,
    Type,
    Manual,
}

/// <summary>The saved options of a Big Folder widget: <c>path</c>, <c>sort</c> and <c>order</c> (a JSON array of file names).</summary>
internal static class FolderOptions
{
    public const string PathKey = "path";
    public const string SortKey = "sort";
    public const string OrderKey = "order";

    public static FolderSort ParseSort(string? text)
        => Enum.TryParse<FolderSort>(text, ignoreCase: true, out var sort) ? sort : FolderSort.Newest;

    public static string SortText(FolderSort sort) => sort.ToString().ToLowerInvariant();

    public static List<string> ParseOrder(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string OrderText(IEnumerable<string> names) => JsonSerializer.Serialize(names);
}

internal static class FolderSorter
{
    private static int Natural(string a, string b)
        => CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>
    /// Newest: most recently added first. Name: natural order ("file 2" before "file 10"). Type: by type, then name.
    /// Manual: the saved order; items not in it (new arrivals) come first, newest first.
    /// </summary>
    public static List<FolderItem> Sort(IEnumerable<FolderItem> items, FolderSort sort, IReadOnlyList<string> manualOrder)
    {
        var list = items.ToList();
        switch (sort)
        {
            case FolderSort.Name:
                list.Sort((a, b) => Natural(a.DisplayName, b.DisplayName) is var c and not 0 ? c : Natural(a.FileName, b.FileName));
                break;
            case FolderSort.Type:
                list.Sort((a, b) =>
                {
                    int byType = FolderKinds.SortRank(a.Kind).CompareTo(FolderKinds.SortRank(b.Kind));
                    return byType != 0 ? byType : Natural(a.DisplayName, b.DisplayName);
                });
                break;
            case FolderSort.Manual:
            {
                var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < manualOrder.Count; i++)
                    position.TryAdd(manualOrder[i], i);
                var fresh = list.Where(i => !position.ContainsKey(i.FileName)).OrderByDescending(i => i.AddedUtc).ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase);
                var known = list.Where(i => position.ContainsKey(i.FileName)).OrderBy(i => position[i.FileName]);
                return [.. fresh, .. known];
            }
            default:
                list.Sort((a, b) =>
                {
                    int byDate = b.AddedUtc.CompareTo(a.AddedUtc);
                    return byDate != 0 ? byDate : Natural(a.DisplayName, b.DisplayName);
                });
                break;
        }
        return list;
    }
}
