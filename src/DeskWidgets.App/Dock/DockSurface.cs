using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>What the pointer did over a dock window. Positions are in DIPs from the bar's top-left corner; the screen position is in pixels.</summary>
internal enum DockPointerKind
{
    Move,

    /// <summary>The pointer left the window (or the bar).</summary>
    Leave,

    Down,

    Up,

    /// <summary>The capture was lost without a release: whatever the press was starting is cancelled.</summary>
    Cancel,
}

/// <summary>
/// <paramref name="X"/> and <paramref name="Y"/> are in <see cref="DockGeometry"/>'s DIPs from the bar's top-left (Glass: screen DIPs; Pixel: two per
/// art pixel, whatever the scale). The buttons are the ones down; on <see cref="DockPointerKind.Up"/> they include the one that was let go, so a
/// click can be told from a drag.
/// </summary>
internal readonly record struct DockPointer(DockPointerKind Kind, double X, double Y, int ScreenX, int ScreenY, bool Left, bool Right, bool Middle, bool Shift);

/// <summary>
/// One program as the bar shows it: the saved item and what is known about it now. <paramref name="CustomKey"/> is its custom tile's key
/// (<see cref="DockCustomKeys"/>), found off the UI thread; null means none, or not known yet (the surface then draws the real or generic icon
/// and never reads the exe itself).
/// </summary>
internal sealed record DockItemView(DockItem Item, bool Missing, DockRunState State = DockRunState.Idle, string? CustomKey = null, IReadOnlyList<DockItemView>? Children = null)
{
    /// <summary>A group's name is "Group" (分组) until it is renamed.</summary>
    public string Name => Item.IsGroup && string.IsNullOrWhiteSpace(Item.Name) ? Strings.DockGroup : Item.Name;

    /// <summary>A group: <see cref="Children"/> are its programs as they are shown (the first four make its 2 × 2 minis; a running member's mini shows its running art). <see cref="State"/> is the group's own: running while any member is.</summary>
    public bool IsGroup => Children is not null;
}

/// <summary>What changed since the surface last drew, so it can move the icons the way the design's moments say (D7, D10, D11, D13).</summary>
internal enum DockChangeKind
{
    /// <summary>The same icons: a redraw only (a missing mark, a new icon picture).</summary>
    None,

    /// <summary>A new list with no motion (the first draw, a language switch).</summary>
    Reset,

    /// <summary>An icon was added at <see cref="DockChange.Index"/> (D10).</summary>
    Added,

    /// <summary>The icon that was at <see cref="DockChange.Index"/> went away (D11).</summary>
    Removed,

    /// <summary>The icon at <see cref="DockChange.Index"/> moved to <see cref="DockChange.To"/> (D7).</summary>
    Moved,

    /// <summary>The icon size changed (D13).</summary>
    Resized,

    /// <summary>
    /// Two programs became a group (D8): the dragged one, which was at <see cref="DockChange.To"/> in the old list, joined the item at the
    /// group's place <see cref="DockChange.Index"/> in the new list; or a program joined an existing group. The old list had one more icon.
    /// </summary>
    Grouped,

    /// <summary>The group that was at <see cref="DockChange.Index"/> is gone: its programs stand in its place, or one program does (a group of one). No motion is asked for beyond a quick settle.</summary>
    Ungrouped,
}

internal readonly record struct DockChange(DockChangeKind Kind, int Index = -1, int To = -1);

/// <summary>
/// One theme's dock window: it draws the bar and takes the pointer; the <see cref="DockController"/> decides everything else. Glass is a WinUI
/// window with live glass, Pixel a layered window like the Pixel menus. Both are topmost, never activated, and move only through the controller.
/// </summary>
internal interface IDockSurface : IDisposable
{
    nint Handle { get; }

    /// <summary>The DPI scale of the screen the window is on.</summary>
    double Scale { get; }

    /// <summary>The window's top-left and size in screen pixels, as last placed.</summary>
    DockRect WindowRect { get; }

    /// <summary>Pointer input over the window, in bar coordinates. Raised on the UI thread.</summary>
    event Action<DockPointer>? Pointer;

    /// <summary>
    /// Shows the window without activating it: the first time, and again after <see cref="Hide"/> (the controller calls it before the slide-in).
    /// Calling it while shown does nothing.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the window (the controller calls it when the slide-out ends): a parked window at the screen's bottom edge would show on a screen
    /// stacked below. Hover, press and capture are cleared, nothing is drawn or timed while hidden; <see cref="MoveWindowTo"/> and
    /// <see cref="PlaceWindow"/> still work.
    /// </summary>
    void Hide();

    /// <summary>
    /// Puts the window at <paramref name="window"/> and the bar at <paramref name="bar"/> (both screen pixels; the bar lies inside the window),
    /// and draws for the new size. Used for the first placement, a size or length change and a screen change; sliding uses <see cref="MoveWindowTo"/>.
    /// </summary>
    void PlaceWindow(DockRect window, DockRect bar, double scale);

    /// <summary>Moves the window without resizing it, never taking the focus (the slide, the bar's drag).</summary>
    void MoveWindowTo(int x, int y);

    /// <summary>The icons, their size, and what changed (so it can animate it).</summary>
    void SetItems(IReadOnlyList<DockItemView> items, DockIconSize size, DockChange change);

    /// <summary>The hovered icon (-1: none) and the label to show above it.</summary>
    void SetHover(int index, string? label);

    /// <summary>
    /// An icon's run state changed (Build B): <paramref name="how"/> says how, so it can play D3 (opening: the running look wipes in to half-way and
    /// holds), D4 (opened: the rest, with a pop only when started here) or D5 (closed). The items from <see cref="SetItems"/> carry the state too.
    /// </summary>
    void SetRunState(int index, DockRunState state, DockRunTransition how);

    /// <summary>The pressed icon (-1: none).</summary>
    void SetPressed(int index);

    /// <summary>A program is being dragged over the bar: the gap opens at slot <paramref name="index"/> (-1: closed).</summary>
    void SetGap(int index);

    /// <summary>An icon is being dragged along the bar: <paramref name="index"/> is its place in the current list (-1: none), <paramref name="x"/> where its centre is, in DIPs from the bar's left.</summary>
    void SetDrag(int index, double x);

    /// <summary>Shows a menu at a screen point in this theme's own style. <paramref name="closed"/> runs when it has gone.</summary>
    void ShowMenu(IReadOnlyList<MenuEntry> entries, int screenX, int screenY, Action closed);

    /// <summary>Registers the OLE drop target on the window and on every window below it (WinUI makes its child windows late: register again once the content is up).</summary>
    void AttachDropTarget(IDropTarget target);

    /// <summary>The app language changed: redraw any text.</summary>
    void ApplyLanguage();

    // ---- Build C: groups and opening files (PROPOSAL 3.3, 3.6; HANDOFFs: Glass 3, 5 D8/D9/D12, 7, 8; Pixel 2, 6, 8, 9) ----

    /// <summary>
    /// A group is forming (D8): an icon has been held over the icon at <paramref name="index"/> for 500 ms, so its plate shows; -1 takes the plate away.
    /// The dragged icon is the one <see cref="SetDrag"/> already follows.
    /// </summary>
    void SetGroupForming(int index);

    /// <summary>
    /// A program dragged in from outside is over the group at <paramref name="groupIndex"/> (its middle zone): <paramref name="dragged"/> is that program as it
    /// would be shown (its icon), or null when the drag leaves or ends. Pixel draws its mini in the group's next free mini cell (with four minis already, the
    /// group lifts 2 px); the bar opens no gap there. Glass shows the group's plate through <see cref="SetGroupForming"/> and ignores this.
    /// </summary>
    void SetGroupDropPreview(int groupIndex, DockItemView? dragged);

    /// <summary>Files are being dragged over the dock (D12): <paramref name="lit"/> says per icon whether it takes the files (a group: true when at least one member does; its label is then the group's name as a normal label, not the "Open with" pill, because the drop goes to a member), <paramref name="hovered"/> is the icon under the pointer (-1: none) whose label becomes "Open with {name}" when it is lit. A null list ends it.</summary>
    void SetOpenWith(IReadOnlyList<bool>? lit, int hovered);

    /// <summary>Raised for what happens in the group's panel; see <see cref="DockPanelEvent"/>.</summary>
    event Action<DockPanelEvent>? Panel;

    /// <summary>Whether the group's panel is up.</summary>
    bool PanelOpen { get; }

    /// <summary>
    /// Opens the group's panel above the group icon at <paramref name="groupIndex"/> (D9): its own window above the dock, never taking the
    /// focus away from the user's program for its look; <paramref name="members"/> are the group's programs. With <paramref name="forDrop"/> it
    /// opened because files are being dragged over the group, so its apps light up (D12) and a click-away does not close it while the drag lasts.
    /// </summary>
    void ShowPanel(int groupIndex, string title, IReadOnlyList<DockItemView> members, bool forDrop);

    /// <summary>The panel's content changed while it is up (running states, a new order, the title).</summary>
    void SetPanelMembers(string title, IReadOnlyList<DockItemView> members);

    /// <summary>The panel's apps light up per member for files being dragged over it (D12), and the one under the pointer shows "Open with {name}" in its label row; null ends it.</summary>
    void SetPanelOpenWith(IReadOnlyList<bool>? lit, int hovered);

    /// <summary>Starts renaming the group in the panel: its title becomes an edit field (Pixel's needs the TSF text input of Phase 8.3). The result comes as <see cref="DockPanelEventKind.Renamed"/>.</summary>
    void BeginPanelRename();

    /// <summary>Closes the panel (D9: fades and sinks; with <paramref name="animated"/> false, at once).</summary>
    void HidePanel(bool animated);

    /// <summary>The screen rectangle (pixels) of the panel while it is up: the controller keeps the dock up and tells a drop on the panel from one on the bar.</summary>
    DockRect PanelRect { get; }

    /// <summary>The member under a screen pixel in the open panel, or -1 (a file drag over the panel: OLE gives only screen points).</summary>
    int PanelMemberAt(int screenX, int screenY);

    /// <summary>Registers the controller's drop target on the panel's window and its child windows, again whenever the panel window is made or shown; called once.</summary>
    void AttachPanelDropTarget(IDropTarget target);
}

internal enum DockPanelEventKind
{
    /// <summary>A click on the member at <see cref="DockPanelEvent.Index"/>: the controller starts it and closes the panel.</summary>
    Click,

    /// <summary>A right-click on the member: the controller shows its app menu.</summary>
    RightClick,

    /// <summary>The member at <see cref="DockPanelEvent.Index"/> was reordered to <see cref="DockPanelEvent.To"/> (the drag ended there).</summary>
    Reorder,

    /// <summary>The member was dragged out and let go at the screen point (<see cref="DockPanelEvent.ScreenX"/>, <see cref="DockPanelEvent.ScreenY"/>) outside the panel: the controller takes it out of the group and onto the bar.</summary>
    DragOut,

    /// <summary>The "Open all" button.</summary>
    OpenAll,

    /// <summary>The title was renamed to <see cref="DockPanelEvent.Text"/> (an empty text means the default name).</summary>
    Renamed,

    /// <summary>Esc, or a click anywhere else: the panel closed itself (after its animation starts); the controller only needs to know.</summary>
    Dismissed,

    /// <summary>A drag of the member left the panel while still held over the bar (the controller may show where it would land): only the screen point is valid.</summary>
    DragMove,

    /// <summary>A file drop on the member at <see cref="DockPanelEvent.Index"/>: only raised in a panel opened for a drop (the controller opens the files with that member).</summary>
    FileDrop,
}

internal readonly record struct DockPanelEvent(DockPanelEventKind Kind, int Index = -1, int To = -1, int ScreenX = 0, int ScreenY = 0, string? Text = null);
