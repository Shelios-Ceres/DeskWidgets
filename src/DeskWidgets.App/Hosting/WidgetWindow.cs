using System.Runtime.InteropServices;
using DeskWidgets.Interop;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinRT.Interop;

namespace DeskWidgets.Hosting;

/// <summary>
/// One widget on the desktop: a borderless, per-pixel transparent window the
/// size of its card, with live glass (<see cref="GlassBackdrop"/>) behind a
/// <see cref="WidgetFrame"/>. It stays at the bottom of the z-order (on the
/// desktop, under app windows) unless "always on top" is set, is dragged by
/// its body, snaps to screen edges and neighbouring widgets, and keeps out of
/// the taskbar and Alt+Tab.
/// </summary>
internal sealed class WidgetWindow : Window, IWidgetSite
{
    private const double DragThreshold = 4;
    private const double EdgeGap = 16;
    private const double SnapDistance = 24;

    private readonly WidgetHost _host;
    private readonly IWidgetFactory _factory;
    private readonly GlassBackdrop _glass = new();
    private readonly WidgetFrame _frame;
    private readonly OverlappedPresenter _presenter;
    private readonly nint _hwnd;
    private readonly Win32.SubclassProc _subclassProc; // must outlive the subclass
    private WidgetTheme _theme;
    private double _scale = 1;
    private bool _menuOpen;
    private bool _overDesktop; // Show desktop has the desktop on top: stay above it, not at the bottom
    private bool _closingByHost;
    private bool _pointerDown;
    private bool _pressed; // a press on the card that may still turn out to be a click
    private bool _dragging;
    private Win32.POINT _dragCursorStart;
    private PointInt32 _dragWindowStart;

    public WidgetWindow(WidgetHost host, IWidgetFactory factory, WidgetPlacement placement)
    {
        _host = host;
        _factory = factory;
        Placement = placement;
        Title = $"{Strings.AppName} · {factory.DisplayName}";
        SystemBackdrop = _glass;

        _theme = host.Theme;
        Widget = CreateWidget();
        // Top-left, never centred: a fractional offset would blur the pixel theme's bitmaps.
        _frame = new WidgetFrame { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _frame.SetCard(_theme, placement.Size);
        _frame.SetContent(Widget.View);
        var root = new Grid();
        root.Children.Add(_frame);
        Content = root;

        _hwnd = WindowNative.GetWindowHandle(this);
        _presenter = OverlappedPresenter.Create();
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsResizable = false;
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsAlwaysOnTop = placement.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += OnAppWindowClosing;
        Win32.MakeTransparentToolWindow(_hwnd);
        _glass.Attach(_hwnd);
        _glass.SetVisible(_theme == WidgetTheme.Glass);

        _subclassProc = WindowProc;
        Win32.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        _frame.PointerPressed += OnPointerPressed;
        _frame.PointerMoved += OnPointerMoved;
        _frame.PointerReleased += OnPointerReleased;
        _frame.PointerExited += (_, _) => (Widget as IPointerWidget)?.CardPointerMoved(null);
        _frame.PointerCaptureLost += (_, _) => EndDrag();
        _frame.RightTapped += OnRightTapped;
        Closed += OnClosed;

        Widget.ApplySize(placement.Size);
        PlaceAt(placement.X, placement.Y);
    }

    public WidgetPlacement Placement { get; }

    public string TypeId => _factory.TypeId;

    public IWidget Widget { get; private set; }

    /// <summary>Design QA: opens a Big Folder's panel (see <see cref="WidgetHost.OpenForCapture"/>).</summary>
    internal Widgets.Folder.FolderWidget? FolderWidget => Widget as Widgets.Folder.FolderWidget;

    /// <summary>The card (= the window) in physical screen pixels.</summary>
    public Win32.RECT CardRect
    {
        get
        {
            var position = AppWindow.Position;
            var card = WidgetLayout.GetCardPixels(Placement.Size, _theme, _scale);
            return new Win32.RECT
            {
                Left = position.X,
                Top = position.Y,
                Right = position.X + card.Width,
                Bottom = position.Y + card.Height,
            };
        }
    }

    public void Show()
    {
        Activate();
        Win32.MakeTransparentToolWindow(_hwnd); // activation may re-apply presenter styles
        EnsureOnScreen();
        ApplyZOrder();
        RegisterDropTarget();
        // WinUI creates its child windows lazily: register again once the content is up.
        _frame.Loaded += (_, _) => RegisterDropTarget();
    }

    private CardDropTarget? _dropTarget; // must outlive the registrations

    /// <summary>
    /// A widget that takes drops gets our own OLE drop target on the card's window and on every window below it (OLE only asks the
    /// window under the cursor). The target hands everything to whichever widget the card holds now, so it survives a theme switch.
    /// </summary>
    private void RegisterDropTarget()
    {
        if (Widget is not IDropWidget)
            return;
        _dropTarget ??= new CardDropTarget(_hwnd, () => Widget as IDropWidget);
        _dropTarget.RegisterOn(_hwnd);
    }

    /// <summary>Re-applies UI text after the app language changed.</summary>
    public void ApplyLanguage()
    {
        Title = $"{Strings.AppName} · {_factory.DisplayName}";
        Widget.ApplyLanguage();
    }

    /// <summary>
    /// Redraws the widget in another theme: a new widget instance (same
    /// options) on the matching card, then a re-snap in case the card's size
    /// changed (pixel cards are whole screen pixels per art pixel).
    /// </summary>
    public void ApplyTheme(WidgetTheme theme)
    {
        if (theme == _theme)
            return;
        _theme = theme;
        var previous = Widget;
        Widget = CreateWidget();
        _frame.SetCard(theme, Placement.Size);
        _frame.SetContent(Widget.View);
        previous.Dispose();
        _glass.SetVisible(theme == WidgetTheme.Glass);
        Widget.ApplySize(Placement.Size);
        ResizeToCard();
        EnsureOnScreen();
    }

    /// <summary>Closes the window for good (removal or app exit); user close gestures are ignored.</summary>
    public void CloseFromHost()
    {
        _closingByHost = true;
        Close();
    }

    /// <summary>
    /// Brings back a widget whose monitor disappeared, tucks one that sticks out
    /// of its work area back inside, and re-aligns it to the desktop grid
    /// (whose cell size follows the user's icon size).
    /// </summary>
    public void EnsureOnScreen()
    {
        var card = CardRect;
        var center = new Win32.POINT { X = (card.Left + card.Right) / 2, Y = (card.Top + card.Bottom) / 2 };
        if (Win32.MonitorFromPoint(center, Win32.MONITOR_DEFAULTTONULL) == 0)
        {
            var position = _host.GetDefaultPosition(Placement.Size, except: this);
            PlaceAt(position.X, position.Y);
            SavePosition();
            return;
        }
        var work = Win32.GetWorkArea(center);
        bool outside = card.Left < work.Left || card.Top < work.Top || card.Right > work.Right || card.Bottom > work.Bottom;
        if (outside || _host.SnapToDesktopGrid)
            SnapAndSave();
    }

    private int Px(double dips) => (int)Math.Round(dips * _scale);

    private IWidget CreateWidget() => _factory.Create(new WidgetContext
    {
        Services = _host.Services,
        Options = new WidgetOptions(Placement.Options, _host.RequestSave),
        Theme = _theme,
        Site = this,
    });

    nint IWidgetSite.WindowHandle => _hwnd;

    double IWidgetSite.Scale => _scale;

    void IWidgetSite.ShowWarning(string text) => _host.ShowWarning(text);

    void IWidgetSite.RequestSave() => _host.RequestSave();

    private void PlaceAt(int x, int y)
    {
        AppWindow.Move(new PointInt32(x, y));
        _scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        ResizeToCard();
    }

    private void ResizeToCard()
    {
        var size = WidgetLayout.GetCardPixels(Placement.Size, _theme, _scale);
        _frame.Width = size.Width / _scale;
        _frame.Height = size.Height / _scale;
        AppWindow.Resize(size);
        _glass.SetLayout(new System.Numerics.Vector2(size.Width, size.Height), (float)(WidgetLayout.CornerRadius * _scale));
    }

    public nint Handle => _hwnd;

    /// <summary>
    /// While Win+D has lifted the desktop over everything, keeps the widget above
    /// the desktop (never activating it); false puts it back at the bottom, under
    /// the application windows.
    /// </summary>
    public void SetOverDesktop(bool over)
    {
        if (_overDesktop == over)
            return;
        _overDesktop = over;
        ApplyZOrder();
    }

    private void ApplyZOrder()
    {
        if (Placement.AlwaysOnTop)
            return; // the presenter keeps it topmost
        Win32.SetWindowPos(_hwnd, _overDesktop ? Win32.HWND_TOP : Win32.HWND_BOTTOM, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint idSubclass, nuint refData)
    {
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                Win32.EraseTransparent(hwnd, wParam);
                return 1;
            case Win32.WM_NCCALCSIZE when wParam != 0:
                // The presenter re-adds a dialog frame on activation; claiming the
                // whole window as client area keeps the content at (0,0) regardless.
                return 0;
            case Win32.WM_WINDOWPOSCHANGING when !Placement.AlwaysOnTop && !_menuOpen && !_overDesktop:
            {
                // Clicking a widget must not raise it above app windows: keep it on the desktop layer.
                var position = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                if ((position.flags & Win32.SWP_NOZORDER) == 0)
                {
                    position.hwndInsertAfter = Win32.HWND_BOTTOM;
                    Marshal.StructureToPtr(position, lParam, false);
                }
                break;
            }
            case Win32.WM_DPICHANGED:
            {
                nint result = Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
                _scale = (wParam.ToInt64() & 0xFFFF) / 96.0;
                ResizeToCard();
                return result;
            }
            case Win32.WM_DISPLAYCHANGE:
                _host.OnDisplayChanged();
                break;
        }
        return Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_frame);
        // Cards never take the activation, so a press on one does not deactivate a Big Folder's open panel by itself: close it here.
        Widgets.Folder.FolderPanels.CardPressed(Widget);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        // A widget that takes clicks hears the press even on a locked card; only the drag is locked.
        _pressed = Widget is IPointerWidget;
        if (_pressed)
            ((IPointerWidget)Widget).CardPointerPressed(point.Position);
        if (!Placement.Locked)
        {
            _pointerDown = true;
            _dragging = false;
            Win32.GetCursorPos(out _dragCursorStart);
            _dragWindowStart = AppWindow.Position;
        }
        if (_pressed || _pointerDown)
        {
            _frame.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown)
        {
            if (!_pressed)
                (Widget as IPointerWidget)?.CardPointerMoved(e.GetCurrentPoint(_frame).Position);
            return;
        }
        if (!Win32.GetCursorPos(out var cursor))
            return;
        int dx = cursor.X - _dragCursorStart.X;
        int dy = cursor.Y - _dragCursorStart.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < Px(DragThreshold))
            return;
        _dragging = true;
        AppWindow.Move(new PointInt32(_dragWindowStart.X + dx, _dragWindowStart.Y + dy));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_frame).Position;
        bool dragged = _dragging;
        // First the click, then the capture: releasing the capture raises PointerCaptureLost, and a press still open then counts as lost.
        FinishPress(position, dragged);
        _frame.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void FinishPress(Windows.Foundation.Point position, bool dragged)
    {
        if (!_pressed)
            return;
        _pressed = false;
        (Widget as IPointerWidget)?.CardPointerReleased(position, dragged);
    }

    private void EndDrag()
    {
        if (!_pointerDown)
        {
            if (_pressed) // capture was lost without a release: not a click
                FinishPress(default, dragged: true);
            return;
        }
        _pointerDown = false;
        if (_dragging)
        {
            _dragging = false;
            SnapAndSave();
        }
    }

    /// <summary>Snaps the card to the desktop grid, or to edges and neighbours when grid snapping is off, and saves.</summary>
    private void SnapAndSave()
    {
        var card = CardRect;
        var target = _host.SnapToDesktopGrid ? SnapToGrid(card) : SnapToEdges(card);
        AppWindow.Move(target);
        SavePosition();
    }

    private PointInt32 SnapToGrid(Win32.RECT card)
    {
        var grid = _host.GridAt(new Win32.POINT { X = (card.Left + card.Right) / 2, Y = (card.Top + card.Bottom) / 2 });
        return grid.Snap(card.Left, card.Top, card.Width, card.Height);
    }

    /// <summary>Keeps the card on screen and snaps it to work-area edges and neighbouring widgets.</summary>
    private PointInt32 SnapToEdges(Win32.RECT card)
    {
        int width = card.Width, height = card.Height;
        int left = card.Left, top = card.Top;
        var work = Win32.GetWorkArea(new Win32.POINT { X = left + width / 2, Y = top + height / 2 });
        int gap = Px(EdgeGap), snap = Px(SnapDistance);

        left = Math.Max(work.Left, Math.Min(left, work.Right - width));
        top = Math.Max(work.Top, Math.Min(top, work.Bottom - height));

        if (left - work.Left < snap)
            left = work.Left + gap;
        else if (work.Right - (left + width) < snap)
            left = work.Right - width - gap;
        if (top - work.Top < snap)
            top = work.Top + gap;
        else if (work.Bottom - (top + height) < snap)
            top = work.Bottom - height - gap;

        foreach (var other in _host.GetCardRects(except: this))
        {
            bool besideRow = top < other.Bottom + snap && top + height > other.Top - snap;
            bool besideColumn = left < other.Right + snap && left + width > other.Left - snap;
            if (besideRow)
            {
                if (Math.Abs(left - (other.Right + gap)) < snap)
                    left = other.Right + gap;
                else if (Math.Abs(left + width + gap - other.Left) < snap)
                    left = other.Left - gap - width;
                if (Math.Abs(top - other.Top) < snap)
                    top = other.Top;
            }
            if (besideColumn)
            {
                if (Math.Abs(top - (other.Bottom + gap)) < snap)
                    top = other.Bottom + gap;
                else if (Math.Abs(top + height + gap - other.Top) < snap)
                    top = other.Top - gap - height;
                if (Math.Abs(left - other.Left) < snap)
                    left = other.Left;
            }
        }
        return new PointInt32(left, top);
    }

    private void SavePosition()
    {
        var position = AppWindow.Position;
        Placement.X = position.X;
        Placement.Y = position.Y;
        _host.RequestSave();
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        ShowContextMenu(e.GetPosition(_frame));
        e.Handled = true;
    }

    private void ShowContextMenu(Windows.Foundation.Point position)
    {
        IReadOnlyList<MenuEntry> entries = (Widget as IPointerWidget)?.CardContextMenu(position) ?? BuildMenu();
        if (_theme == WidgetTheme.Pixel && Win32.GetCursorPos(out var cursor))
        {
            // Our own topmost popups: nothing to lift the widget for.
            PixelMenu.Show(entries, cursor.X, cursor.Y);
            return;
        }

        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        foreach (var item in FlyoutMenuAdapter.Build(entries))
            menu.Items.Add(item);

        // While the menu is open, lift the widget so the menu is not hidden under app windows.
        menu.Opening += (_, _) =>
        {
            _menuOpen = true;
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            ApplyZOrder();
        };
        menu.ShowAt(_frame, position);
    }

    /// <summary>The right-click menu as a model: every theme shows these rows, in this order.</summary>
    internal IReadOnlyList<MenuEntry> BuildMenu()
    {
        var entries = new List<MenuEntry>();
        if (_factory.SupportedSizes.Count > 1)
        {
            entries.Add(MenuEntry.Sub(Strings.Size,
                [.. _factory.SupportedSizes.Select(size => MenuEntry.Radio(SizeName(size), size == Placement.Size, () => SetSize(size), group: "size"))],
                glyph: "", icon: PixelIcons.Size));
        }
        entries.AddRange(Widget.CreateMenuItems());
        entries.Add(MenuEntry.Separator());
        entries.Add(MenuEntry.Toggle(Strings.AlwaysOnTop, Placement.AlwaysOnTop, () => SetAlwaysOnTop(!Placement.AlwaysOnTop)));
        entries.Add(MenuEntry.Toggle(Strings.LockPosition, Placement.Locked, () =>
        {
            Placement.Locked = !Placement.Locked;
            _host.RequestSave();
        }));
        entries.Add(MenuEntry.Separator());
        // After the menu has closed: switching replaces this widget, menu items included.
        entries.Add(MenuEntry.Sub(Strings.ThemeMenu,
            [.. Enum.GetValues<WidgetTheme>().Select(theme => MenuEntry.Radio(Strings.ThemeName(theme), theme == _theme, () => DispatcherQueue.TryEnqueue(() => _host.SetTheme(theme)), group: "theme"))],
            glyph: "", icon: PixelIcons.Theme));
        entries.Add(MenuEntry.Sub(Strings.AddWidget,
            [.. WidgetCatalog.All.Select(factory => MenuEntry.Item(factory.DisplayName, () => _host.AddWidget(factory), glyph: factory.Glyph, icon: PixelIcons.ForWidget(factory.TypeId)))],
            glyph: "", icon: PixelIcons.Add));
        entries.Add(MenuEntry.Item(Strings.RemoveWidget, () => _host.RemoveWidget(this), glyph: "", icon: PixelIcons.Remove));
        entries.Add(MenuEntry.Separator());
        // After the menu has closed, so the question isn't stacked on it.
        entries.Add(MenuEntry.Item(Strings.Exit, () => DispatcherQueue.TryEnqueue(() => _host.ConfirmExit(_hwnd))));
        return entries;
    }

    private static string SizeName(WidgetSize size) => size switch
    {
        WidgetSize.Small => Strings.SizeSmall,
        WidgetSize.Medium => Strings.SizeMedium,
        _ => Strings.SizeLarge,
    };

    private void SetSize(WidgetSize size)
    {
        if (size == Placement.Size)
            return;
        Placement.Size = size;
        _frame.SetCard(_theme, size);
        Widget.ApplySize(size);
        ResizeToCard();
        SnapAndSave();
    }

    private void SetAlwaysOnTop(bool onTop)
    {
        Placement.AlwaysOnTop = onTop;
        _presenter.IsAlwaysOnTop = onTop;
        ApplyZOrder();
        _host.RequestSave();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4 on a focused widget: ignore. Widgets are removed from their menu.
        if (!_closingByHost)
            args.Cancel = true;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _dropTarget?.Revoke();
        Win32.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        Widget.Dispose();
        _glass.Dispose();
    }
}
