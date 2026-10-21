using DeskWidgets.PixelUi;

namespace DeskWidgets.Widgets.Folder;

internal sealed partial class FolderWidget
{
    /// <summary>The widget's own rows in the right-click menu (the host adds Size, Theme and the rest around them).</summary>
    public IEnumerable<MenuEntry> CreateMenuItems()
    {
        var rows = new List<MenuEntry>
        {
            MenuEntry.Item(Strings.FolderOpenInExplorer, () => Report(FolderActions.OpenFolder(Model.Path)), glyph: "", icon: PixelIcons.Folder),
            MenuEntry.Item(Strings.FolderAddApps, () => Report(FolderActions.AddApps()), glyph: "", icon: PixelIcons.Add),
            MenuEntry.Sub(Strings.FolderSortBy,
                [.. Enum.GetValues<FolderSort>().Select(sort => MenuEntry.Radio(Strings.FolderSortRow(sort), sort == Model.Sort, () => Model.SetSort(sort), group: "folder-sort"))],
                glyph: "", icon: PixelIcons.Sort),
            MenuEntry.Item(Model.Exists ? Strings.FolderChange : Strings.FolderChoose, ChangeFolder, glyph: "", icon: PixelIcons.Folder),
        };
        if (Model.Exists)
            rows.Add(MenuEntry.Item(Strings.FolderRenameFolder, () => OpenPanel().BeginRenameFolder(), glyph: "", icon: PixelIcons.Pencil));
        return rows;
    }

    private void ChangeFolder()
    {
        string? chosen = FolderActions.ChooseFolder(_site.WindowHandle, Directory.Exists(Model.Path) ? Model.Path : null);
        if (chosen is not null && !string.Equals(chosen, Model.Path, StringComparison.OrdinalIgnoreCase))
            Model.SetFolder(chosen);
    }

    /// <summary>The menu for one item (the card's right-click, and the panel's, where <paramref name="targets"/> may be the selection).</summary>
    internal IReadOnlyList<MenuEntry> ItemMenu(FolderItem item, IReadOnlyList<FolderItem>? targets = null)
    {
        var affected = targets is { Count: > 0 } && targets.Contains(item) ? targets : [item];
        var rows = new List<MenuEntry> { MenuEntry.Item(Strings.FolderOpen, () => Open(item), glyph: "") };
        if (item.IsApp)
        {
            rows.Add(MenuEntry.Item(Strings.FolderRunAsAdministrator, () => Report(FolderActions.RunAsAdministrator(item, _site.WindowHandle)), glyph: "", icon: PixelIcons.Shield));
            rows.Add(new MenuEntry
            {
                Text = Strings.FolderOpenFileLocation, Kind = MenuKind.Item, Enabled = FolderActions.CanOpenFileLocation(item),
                Click = () => Report(FolderActions.OpenFileLocation(item)), Glyph = "", Icon = PixelIcons.Folder,
            });
        }
        rows.Add(MenuEntry.Separator());
        rows.Add(MenuEntry.Item(Strings.FolderShowInExplorer, () => Report(FolderActions.ShowInExplorer(item)), glyph: "", icon: PixelIcons.Folder));
        rows.Add(MenuEntry.Item(Strings.FolderRename, () => OpenPanel().BeginRename(item.FileName), glyph: "", icon: PixelIcons.Pencil));
        if (FolderActions.CanPutBack(Model, item))
            rows.Add(MenuEntry.Item(Strings.FolderPutBack, () => PutBack(affected), glyph: "", icon: PixelIcons.PutBack));
        rows.Add(MenuEntry.Separator());
        // A shortcut is removed on its own (only the shortcut goes); a real file goes to the Recycle Bin. Both can be undone in Explorer.
        string remove = item.IsShortcut ? Strings.FolderRemoveFromFolder : Strings.FolderMoveToRecycleBin;
        rows.Add(MenuEntry.Item(remove, () => Report(FolderActions.Recycle(Model, affected, _site.WindowHandle)), glyph: "", icon: PixelIcons.Remove));
        rows.Add(MenuEntry.Separator());
        rows.Add(MenuEntry.Item(Strings.FolderProperties, () => FolderActions.Properties(item, _site.WindowHandle), glyph: ""));
        return rows;
    }

    private void PutBack(IReadOnlyList<FolderItem> items)
    {
        foreach (var item in items)
        {
            if (FolderActions.CanPutBack(Model, item))
                Report(FolderActions.PutBack(Model, item, _site.WindowHandle));
        }
    }
}
