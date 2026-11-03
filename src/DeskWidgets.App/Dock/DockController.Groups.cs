using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Dock;

/// <summary>Groups (Build C, PROPOSAL 3.3): making one by holding an icon over another, the group's panel, its menu and "Open all".</summary>
internal sealed partial class DockController
{
    private readonly DockHold _hold = new();
    private FrameClock.Ticket? _holdTimer;
    private bool _forming; // the plate shows on _hold.Target
    private DockItem? _openGroup; // the group whose panel is up

    // ---------------------------------------------------------------- forming a group while an icon is dragged

    /// <summary>The dragged icon is at <paramref name="x"/>: note what it is held over, and show the plate once it has been held long enough (D8).</summary>
    private void UpdateHold(DockPointer p)
    {
        var items = _preview ?? Settings.Items;
        int target = items[_dragFrom].IsGroup ? -1 : DockGroups.TargetAt(Settings.IconSize, items.Count, p.X, _dragFrom);
        if (!_hold.Over(target, Now))
            return;
        _holdTimer?.Stop();
        _holdTimer = null;
        if (_forming)
        {
            _forming = false;
            _surface?.SetGroupForming(-1);
        }
        if (_hold.DueAt is { } due)
        {
            _holdTimer = FrameClock.After((int)Math.Max(1, (due - Now).TotalMilliseconds), () =>
            {
                if (_disposed || _surface is null || !_hold.Ready(Now) || _dragKind != DragKind.Reorder)
                    return;
                _forming = true;
                _surface.SetGroupForming(_hold.Target);
            });
        }
    }

    private void ClearHold()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
        _hold.Clear();
        if (_forming)
        {
            _forming = false;
            _surface?.SetGroupForming(-1);
        }
    }

    /// <summary>The icon the drag ended on was held long enough: the dragged icon joins it. Returns the change to draw, or null when nothing was grouped.</summary>
    private DockChange? TryGroup(List<DockItem> list)
    {
        if (!_forming || _hold.Target < 0 || _dragFrom < 0)
            return null;
        int from = _dragFrom, target = _hold.Target;
        int group = DockGroups.MakeGroup(list, from, target);
        return group < 0 ? null : new DockChange(DockChangeKind.Grouped, group, from);
    }

    // ---------------------------------------------------------------- the panel

    private void TogglePanel(int index)
    {
        if (_surface is null)
            return;
        var group = Settings.Items[index];
        if (_surface.PanelOpen && _openGroup == group)
        {
            HidePanel(animated: true);
            return;
        }
        ShowPanelFor(index, forDrop: false);
    }

    private void ShowPanelFor(int index, bool forDrop)
    {
        if (_surface is null || index < 0 || index >= Settings.Items.Count || !Settings.Items[index].IsGroup)
            return;
        var group = Settings.Items[index];
        CancelLeaveHide();
        SetHover(-1);
        _openGroup = group;
        _surface.ShowPanel(index, ViewOf(group).Name, Views(group.Children!), forDrop);
    }

    /// <summary>Closes the panel. The dock may slide away again once nothing is in use.</summary>
    internal void HidePanel(bool animated)
    {
        if (_openGroup is null)
            return;
        _openGroup = null;
        _surface?.HidePanel(animated);
        ScheduleLeaveHide();
    }

    /// <summary>The panel's content may have changed (a running state, a missing mark, a rename, a new order): show it as it is.</summary>
    private void RefreshPanel()
    {
        if (_surface is not { PanelOpen: true } || _openGroup is null)
            return;
        if (!Settings.Items.Contains(_openGroup))
        {
            HidePanel(animated: false);
            return;
        }
        _surface.SetPanelMembers(ViewOf(_openGroup).Name, Views(_openGroup.Children!));
    }

    private void OnPanelEvent(DockPanelEvent e)
    {
        if (_disposed || _surface is null)
            return;
        var group = _openGroup;
        if (e.Kind == DockPanelEventKind.Dismissed)
        {
            _openGroup = null;
            ScheduleLeaveHide();
            return;
        }
        if (group is null)
            return;
        var members = group.Children!;
        DockItem? member = e.Index >= 0 && e.Index < members.Count ? members[e.Index] : null;
        switch (e.Kind)
        {
            case DockPanelEventKind.Click when member is not null:
                Win32.GetCursorPos(out var at);
                HidePanel(animated: true);
                ClickProgram(member, another: false, at.X, at.Y);
                break;
            case DockPanelEventKind.RightClick when member is not null:
                Win32.GetCursorPos(out var menuAt);
                ShowMenu(AppMenu(member), menuAt.X, menuAt.Y);
                break;
            case DockPanelEventKind.Reorder:
                if (DockGroups.Reorder(group, e.Index, e.To))
                {
                    _host.RequestSave();
                    RefreshPanel();
                }
                break;
            case DockPanelEventKind.DragMove:
                ShowGap(IsOverBar(e.ScreenX, e.ScreenY) ? SlotAtScreenX(e.ScreenX) : -1);
                break;
            case DockPanelEventKind.DragOut:
                ShowGap(-1);
                if (member is not null && IsOverBar(e.ScreenX, e.ScreenY))
                    TakeOut(group, e.Index, SlotAtScreenX(e.ScreenX));
                break;
            case DockPanelEventKind.OpenAll:
                HidePanel(animated: true);
                OpenAll(group);
                break;
            case DockPanelEventKind.Renamed:
                Rename(group, e.Text);
                break;
            case DockPanelEventKind.FileDrop when member is not null:
                OpenDroppedWith(member);
                break;
        }
    }

    /// <summary>A member was dragged out of its group and dropped on the bar at <paramref name="slot"/>; a group left with one program becomes it.</summary>
    private void TakeOut(DockItem group, int child, int slot)
    {
        int groupIndex = Settings.Items.IndexOf(group);
        if (groupIndex < 0)
            return;
        // The slot counts the bar as the pointer sees it, which still has the group at its place.
        int at = DockGroups.TakeOut(Settings.Items, groupIndex, child, slot);
        if (at < 0)
            return;
        HidePanel(animated: true);
        _host.RequestSave();
        Relayout(new DockChange(DockChangeKind.Added, at));
    }

    private void Rename(DockItem group, string? text)
    {
        group.Name = (text ?? "").Trim();
        _host.RequestSave();
        _surface?.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
        RefreshPanel();
    }

    // ---------------------------------------------------------------- the group's menu, Open all

    internal IReadOnlyList<MenuEntry> GroupMenu(DockItem group) =>
    [
        MenuEntry.Item(Strings.DockOpenAll, () => OpenAll(group), glyph: ""),
        MenuEntry.Item(Strings.DockRename, () => BeginRename(group), glyph: "", icon: PixelIcons.Pencil),
        MenuEntry.Item(Strings.DockUngroup, () => Ungroup(group), glyph: ""),
        MenuEntry.Separator(),
        MenuEntry.Item(Strings.DockRemoveGroup, () => FrameClock.After(1, () => _host.AskRemoveGroup(Handle, ViewOf(group).Name, () => Remove(group))), glyph: "", icon: PixelIcons.Remove),
    ];

    /// <summary>Starts every program of the group that is not running or opening, each once (on the worker, like a click). The running ones are left alone.</summary>
    internal void OpenAll(DockItem group)
    {
        Win32.GetCursorPos(out var at);
        foreach (var program in DockGroups.OpenAll(group, key => _runStates.StateOf(key) != DockRunState.Idle))
            Start(program, at.X, at.Y);
    }

    private void BeginRename(DockItem group)
    {
        int index = Settings.Items.IndexOf(group);
        if (index < 0)
            return;
        if (_surface is not { PanelOpen: true } || _openGroup != group)
            ShowPanelFor(index, forDrop: false);
        _surface?.BeginPanelRename();
    }

    private void Ungroup(DockItem group)
    {
        int index = Settings.Items.IndexOf(group);
        if (index < 0)
            return;
        if (_openGroup == group)
            HidePanel(animated: false);
        DockGroups.Ungroup(Settings.Items, index);
        _hover = -1;
        _host.RequestSave();
        Relayout(new DockChange(DockChangeKind.Ungrouped, index));
    }

    /// <summary>Removes one program from the dock wherever it is (a member's own menu in the panel); a group left with one becomes that program.</summary>
    internal void RemoveProgram(DockItem program)
    {
        if (Settings.Items.Contains(program))
        {
            Remove(program);
            return;
        }
        var group = Settings.Items.FirstOrDefault(i => i.IsGroup && i.Children!.Contains(program));
        if (group is null)
            return;
        group.Children!.Remove(program);
        _missing.Remove(program.Key);
        _runStates.Forget(program.Key);
        int index = Settings.Items.IndexOf(group);
        bool collapsed = DockGroups.Normalize(Settings.Items);
        _host.RequestSave();
        if (collapsed && _openGroup == group)
            HidePanel(animated: false);
        Relayout(new DockChange(collapsed ? DockChangeKind.Ungrouped : DockChangeKind.None, index));
        RefreshPanel();
    }

    // ---------------------------------------------------------------- programs dropped from outside

    private int _dropGroup = -1; // the group a dragged program is over (its plate shows)

    private int GroupAtScreen(int screenX, int screenY)
    {
        double x = (screenX - _layout.Bar.X) / PxPerGeometryDip, y = (screenY - _layout.Bar.Y) / PxPerGeometryDip;
        if (y < 0 || y > DockGeometry.BarHeightDip(Settings.IconSize))
            return -1;
        int index = DockGeometry.IconAt(Settings.IconSize, Settings.Items.Count, x);
        return index >= 0 && Settings.Items[index].IsGroup ? index : -1;
    }

    /// <summary>A program is dragged over the bar: over a group it joins it (the group's plate shows), elsewhere a gap opens where it would land.</summary>
    internal void ShowProgramDrop(int screenX, int screenY, DockItem? dragged = null)
    {
        int group = GroupAtScreen(screenX, screenY);
        if (group != _dropGroup)
        {
            _dropGroup = group;
            _surface?.SetGroupForming(group);
            _surface?.SetGroupDropPreview(group, group >= 0 && dragged is not null ? ViewOf(dragged) : null);
        }
        ShowGap(group >= 0 ? -1 : SlotAtScreenX(screenX));
        if (group >= 0)
        {
            _dropping = true;
            CancelLeaveHide();
        }
    }

    /// <summary>
    /// Programs were dropped: on a group they join it; anywhere else on the bar they are added at the slot. The group is read from the drop
    /// point, not from <see cref="_dropGroup"/>: the drop target ends the drag (which clears it) before it hands the programs over.
    /// </summary>
    internal void DropPrograms(IReadOnlyList<DockItem> programs, int screenX, int screenY)
    {
        int group = GroupAtScreen(screenX, screenY);
        if (group >= 0 && group < Settings.Items.Count && Settings.Items[group].IsGroup)
        {
            int added = programs.Count(program => DockGroups.AddToGroup(Settings.Items, group, program));
            if (added > 0)
            {
                _host.RequestSave();
                _surface?.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.Grouped, group, -1));
                RefreshPanel();
                CheckMissing();
                OnWindows(_windows); // a running program joining a group lights its mini now
            }
            return;
        }
        Add(programs, SlotAtScreenX(screenX));
    }

    private void EndProgramDrop()
    {
        if (_dropGroup < 0)
            return;
        _dropGroup = -1;
        _surface?.SetGroupForming(-1);
        _surface?.SetGroupDropPreview(-1, null);
    }

    /// <summary>Files dropped on the open panel: they open with the member under the pointer.</summary>
    internal void DropFilesOnPanel(int screenX, int screenY)
    {
        if (_surface is not { PanelOpen: true } || _openGroup is null)
            return;
        int member = _surface.PanelMemberAt(screenX, screenY);
        if (member >= 0 && member < _openGroup.Children!.Count)
            OpenDroppedWith(_openGroup.Children[member]);
    }

    private void StopGroupTimers()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
    }
}
