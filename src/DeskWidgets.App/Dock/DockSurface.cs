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

/// <summary>One program as the bar shows it: the saved item and what is known about it now.</summary>
internal sealed record DockItemView(DockItem Item, bool Missing)
{
    public string Name => Item.Name;
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

    /// <summary>The pointer is over a label the surface drew or over the headroom: only the controller's own state decides what the window is "in use" for.</summary>
    void Show();

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
}
