using System.Globalization;

namespace DeskWidgets.Widgets.Folder;

/// <summary>The 14 icon types of the Big Folder (THEMES 4.1 and 4.2). Image, Video and App are fallbacks for real content.</summary>
internal enum FolderItemKind
{
    Folder,
    Document,
    Spreadsheet,
    Presentation,
    Pdf,
    Text,
    Code,
    Archive,
    Audio,
    WebLink,
    Image,
    Video,
    App,
    Other,
}

internal static class FolderKinds
{
    private static readonly Dictionary<string, FolderItemKind> ByExtension = Build();

    private static Dictionary<string, FolderItemKind> Build()
    {
        var map = new Dictionary<string, FolderItemKind>(StringComparer.OrdinalIgnoreCase);
        void Add(FolderItemKind kind, string extensions)
        {
            foreach (string extension in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                map[extension] = kind;
        }
        Add(FolderItemKind.Document, "doc docx dot dotx odt rtf pages wps");
        Add(FolderItemKind.Spreadsheet, "xls xlsx xlsm xlt xltx csv ods numbers et");
        Add(FolderItemKind.Presentation, "ppt pptx pps ppsx odp key dps");
        Add(FolderItemKind.Pdf, "pdf");
        Add(FolderItemKind.Text, "txt md markdown log ini cfg conf");
        Add(FolderItemKind.Code, "cs js ts tsx jsx py json xml html htm css scss ps1 psm1 bat cmd sh c cpp cc h hpp java kt go rs rb php swift sql yaml yml toml vue lua csproj sln props targets");
        Add(FolderItemKind.Archive, "zip 7z rar tar gz tgz bz2 xz cab iso");
        Add(FolderItemKind.Audio, "mp3 m4a wav flac aac ogg wma opus");
        Add(FolderItemKind.Image, "jpg jpeg png gif webp heic heif bmp tif tiff avif ico");
        Add(FolderItemKind.Video, "mp4 mov mkv avi wmv webm m4v flv");
        Add(FolderItemKind.WebLink, "url");
        Add(FolderItemKind.App, "exe lnk appref-ms msi");
        return map;
    }

    /// <summary>The kind for a file name by its extension (no dot needed); Folder is the caller's to decide.</summary>
    public static FolderItemKind OfFile(string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.');
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var kind) ? kind : FolderItemKind.Other;
    }

    /// <summary>Document, Spreadsheet and Presentation tiles carry their extension: at most 4 characters, uppercase ("DOCX", "PAGE").</summary>
    public static string ExtensionLabel(string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.').ToUpper(CultureInfo.InvariantCulture);
        return extension.Length <= 4 ? extension : extension[..4];
    }

    /// <summary>
    /// The symbol on an Other tile: the extension, uppercase; more than 4 characters become the first 3 and "…".
    /// Empty for a file without an extension (the plain tile).
    /// </summary>
    public static string OtherSymbol(string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.').ToUpper(CultureInfo.InvariantCulture);
        return extension.Length <= 4 ? extension : extension[..3] + "…";
    }

    /// <summary>
    /// The font size of an Other tile's symbol in Glass's 64-unit box, by length: 1 → 24, 2 → 22, 3 → 19, 4 → 15.5.
    /// The ellipsis form counts as 4.
    /// </summary>
    public static double OtherSymbolSize(string symbol) => symbol.Length switch
    {
        <= 1 => 24,
        2 => 22,
        3 => 19,
        _ => 15.5,
    };

    /// <summary>Order of the types for the "Type" sort.</summary>
    public static int SortRank(FolderItemKind kind) => (int)kind;
}
