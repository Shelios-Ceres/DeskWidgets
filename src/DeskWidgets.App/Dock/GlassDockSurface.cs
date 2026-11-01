using System.Runtime.InteropServices;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinRT.Interop;

namespace DeskWidgets.Dock;

/// <summary>
/// The Glass dock window (HANDOFF 15.1, SPIKE section 1): a borderless, per-pixel transparent WinUI window with live frost, topmost, out of
/// the taskbar and Alt+Tab, never activated by a click. It is wider and taller than the window the controller asks for (see
/// <see cref="GlassDockLayout"/>); a window region keeps all of it click-through except the bar and the hover label. It draws what the
/// <see cref="DockController"/> says and reports the pointer in the controller's bar coordinates; it decides nothing.
/// <para><see cref="WindowRect"/> is the controller's window (as placed and moved), not the larger real one, so the slide's numbers stay its own.</para>
/// </summary>
internal sealed partial class GlassDockSurface : IDockSurface
{
    private const int MA_NOACTIVATE = 3;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const long WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    private static readonly nint HwndTopmost = -1;

    /// <summary>The room WinUI puts between a flyout placed on top and its target, made up for in the menu's anchor.</summary>
    private const double MenuPlacementGapDip = 6;

    private readonly Window _window = new();
    private readonly GlassDockBackdrop _glass = new();
    private readonly OverlappedPresenter _presenter;
    private readonly nint _hwnd;
    private readonly Win32.SubclassProc _subclassProc; // must outlive the subclass
    private readonly DropTargetRegistration _drop = new();
    private IDropTarget? _dropTarget;

    private DockRect _logical; // the controller's window
    private DockRect _bar; // the controller's bar
    private DockRect _actual; // the real window
    private DockRect _work; // the work area the bar is on
    private DockRect _applied; // what the window was last set to
    private bool _windowDirty;
    private bool _shown;
    private bool _parked; // hidden by Hide (the slide-out ended), until Show
    private bool _disposed;
    private bool _captured;
    private MenuFlyout? _menu;

    public GlassDockSurface()
    {
        _window.Title = $"{Strings.AppName} · {Strings.DockName}";
        _window.SystemBackdrop = _glass;
        _hwnd = WindowNative.GetWindowHandle(_window);
        _presenter = OverlappedPresenter.Create();
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsResizable = false;
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsAlwaysOnTop = true;
        _window.AppWindow.SetPresenter(_presenter);
        _window.AppWindow.IsShownInSwitchers = false;
        _window.AppWindow.Closing += OnAppWindowClosing;
        Win32.MakeTransparentToolWindow(_hwnd);
        _glass.Attach(_hwnd);
        _subclassProc = WindowProc;
        Win32.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        BuildScene();
        _window.Content = _root;
        _root.PointerMoved += OnPointerMoved;
        _root.PointerPressed += OnPointerPressed;
        _root.PointerReleased += OnPointerReleased;
        _root.PointerExited += OnPointerExited;
        _root.PointerCaptureLost += OnPointerCaptureLost;
        _root.PointerCanceled += OnPointerCanceled;
        _root.Loaded += OnRootLoaded;
        _root.ActualThemeChanged += OnThemeChanged;
        _window.Closed += OnClosed;
    }

    public nint Handle => _hwnd;

    public double Scale { get; private set; } = 1;

    public DockRect WindowRect => _logical;

    public event Action<DockPointer>? Pointer;

    // ---------------------------------------------------------------- the window

    public void Show()
    {
        if (_disposed)
            return;
        if (_shown)
        {
            if (!_parked)
                return;
            // Back from Hide: where the controller last put it, then shown without the focus and back on top of the topmost band.
            // The styles and the region stayed with the window; no D15 fade (that is the new dock's moment, the slide-in is this one's).
            _parked = false;
            ApplyWindow();
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            Win32.SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            return;
        }
        _parked = false;
        ApplyWindow();
        _window.AppWindow.Show(false); // without taking the focus
        Win32.MakeTransparentToolWindow(_hwnd); // showing may re-apply presenter styles
        SetExStyle(WS_EX_NOACTIVATE | WS_EX_TOPMOST, on: true);
        // The presenter's own topmost flag, toggled, puts the window in the topmost band for good (SPIKE 1).
        _presenter.IsAlwaysOnTop = false;
        _presenter.IsAlwaysOnTop = true;
        _shown = true;
        _regionBar = null; // set again after the presenter's styles have settled
        UpdateRegion();
        // D15: a new Glass dock fades in (Enter, opacity only).
        if (MotionSettings.Enabled)
        {
            GlassMotion.Preset(_root, 0, 1, System.Numerics.Vector2.Zero);
            GlassMotion.FadeTo(_root, 1, GlassMotion.Enter, GlassEase.Out);
            _glass.FadeIn(GlassMotion.Enter);
        }
    }

    public void Hide()
    {
        if (_disposed || _parked)
            return;
        _parked = true;
        // No Cancel or Leave to the controller: it is the one hiding the dock, and its own state goes with it.
        if (_captured)
        {
            _captured = false; // first, so the capture-lost event this raises is not a cancel
            _root.ReleasePointerCaptures();
        }
        ClearPointerLooks();
        if (_shown)
            ShowWindow(_hwnd, SW_HIDE);
    }

    public void PlaceWindow(DockRect window, DockRect bar, double scale)
    {
        if (_disposed)
            return;
        var center = new Win32.POINT { X = bar.X + bar.Width / 2, Y = bar.Y + bar.Height / 2 };
        var work = Win32.GetWorkArea(center);
        _work = new DockRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        var actual = GlassDockLayout.WindowFor(_work, window, bar, scale);
        // Everything is drawn in the real window's DIPs: a new width, height or scale moves every drawn thing, so the next draw snaps.
        if (actual.X != _actual.X || actual.Width != _actual.Width || actual.Height != _actual.Height || scale != Scale)
            _snapNext = true;
        _logical = window;
        _bar = bar;
        _actual = actual;
        Scale = scale;
        SizeScene();
        // The OS window follows at the end of this turn: a hidden dock is placed and then moved down at once (MoveWindowTo), never shown between.
        if (!_windowDirty)
        {
            _windowDirty = true;
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (_windowDirty && !_disposed)
                    ApplyWindow();
            });
        }
    }

    public void MoveWindowTo(int x, int y)
    {
        if (_disposed)
            return;
        int dx = x - _logical.X, dy = y - _logical.Y;
        _logical = _logical with { X = x, Y = y };
        _bar = _bar with { X = _bar.X + dx, Y = _bar.Y + dy };
        _actual = _actual with { X = _actual.X + dx, Y = _actual.Y + dy };
        ApplyWindow();
    }

    private void ApplyWindow()
    {
        _windowDirty = false;
        if (_actual == _applied)
            return;
        bool sized = _actual.Width != _applied.Width || _actual.Height != _applied.Height;
        uint flags = Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | (sized ? 0 : Win32.SWP_NOSIZE);
        Win32.SetWindowPos(_hwnd, 0, _actual.X, _actual.Y, _actual.Width, _actual.Height, flags);
        _applied = _actual;
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint idSubclass, nuint refData)
    {
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                Win32.EraseTransparent(hwnd, wParam);
                return 1;
            case Win32.WM_NCCALCSIZE when wParam != 0:
                return 0; // the presenter re-adds a dialog frame on activation: the whole window is client area
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE; // a click on the dock never takes the focus
            case Win32.WM_DPICHANGED:
            {
                // WinUI takes the new scale; the size is ours (the controller places the window again for the new screen).
                nint result = Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
                _applied = default;
                ApplyWindow();
                return result;
            }
        }
        return Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void SetExStyle(long bits, bool on)
    {
        long style = (long)GetWindowLongPtr(_hwnd, -20 /* GWL_EXSTYLE */);
        SetWindowLongPtr(_hwnd, -20, (nint)(on ? style | bits : style & ~bits));
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4 or a stray close: ignore. The dock goes from its own menu.
        if (!_disposed)
            args.Cancel = true;
    }

    // ---------------------------------------------------------------- the region

    private (int, int, int, int)? _regionBar, _regionLabel;

    /// <summary>The window's region: the drawn bar, and the label while it shows; the rest of the window lets clicks and drags through (SPIKE 1).</summary>
    private void UpdateRegion()
    {
        if (_disposed)
            return;
        var bar = GlassDockLayout.RegionPixels(_barRegion, Scale);
        (int, int, int, int)? label = _labelRegion is { } rect ? GlassDockLayout.RegionPixels(rect, Scale) : null;
        if (bar == _regionBar && label == _regionLabel)
            return;
        _regionBar = bar;
        _regionLabel = label;
        nint region = CreateRectRgn(bar.Left, bar.Top, bar.Right, bar.Bottom);
        if (label is { } l)
        {
            nint extra = CreateRectRgn(l.Item1, l.Item2, l.Item3, l.Item4);
            CombineRgn(region, region, extra, 2 /* RGN_OR */);
            DeleteObject(extra);
        }
        if (SetWindowRgn(_hwnd, region, true) == 0)
            DeleteObject(region); // on success the window owns the region
    }

    /// <summary>Whether a screen point is inside the window's region.</summary>
    private bool InRegion(int screenX, int screenY)
    {
        int x = screenX - _actual.X, y = screenY - _actual.Y;
        static bool Inside((int L, int T, int R, int B)? r, int x, int y) => r is { } v && x >= v.L && x < v.R && y >= v.T && y < v.B;
        return Inside(_regionBar, x, y) || Inside(_regionLabel, x, y);
    }

    // ---------------------------------------------------------------- the pointer

    /// <summary>Raises <see cref="Pointer"/> at the cursor, in the controller's bar coordinates (DIPs; negative Y is the headroom).</summary>
    private void Raise(DockPointerKind kind, bool left, bool right, bool middle, bool shift)
    {
        if (_disposed || _parked || !Win32.GetCursorPos(out var cursor))
            return;
        double x = (cursor.X - _bar.X) / Scale, y = (cursor.Y - _bar.Y) / Scale;
        Pointer?.Invoke(new DockPointer(kind, x, y, cursor.X, cursor.Y, left, right, middle, shift));
    }

    private static bool Shift(PointerRoutedEventArgs e) => (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_root).Properties;
        Raise(DockPointerKind.Move, p.IsLeftButtonPressed, p.IsRightButtonPressed, p.IsMiddleButtonPressed, Shift(e));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_root).Properties;
        // A left or middle press is captured, so the Moves go on through a drag that leaves the bar.
        if ((p.IsLeftButtonPressed || p.IsMiddleButtonPressed) && _root.CapturePointer(e.Pointer))
            _captured = true;
        Raise(DockPointerKind.Down, p.IsLeftButtonPressed, p.IsRightButtonPressed, p.IsMiddleButtonPressed, Shift(e));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_root).Properties;
        // Up reports the button that was let go (it is no longer "pressed"), plus any still held.
        var kind = p.PointerUpdateKind;
        Raise(DockPointerKind.Up,
            p.IsLeftButtonPressed || kind == PointerUpdateKind.LeftButtonReleased,
            p.IsRightButtonPressed || kind == PointerUpdateKind.RightButtonReleased,
            p.IsMiddleButtonPressed || kind == PointerUpdateKind.MiddleButtonReleased,
            Shift(e));
        // First the Up, then the capture: releasing it raises PointerCaptureLost, which must not count as a cancel.
        if (_captured)
        {
            _captured = false;
            _root.ReleasePointerCapture(e.Pointer);
        }
        LeaveIfOutside();
        e.Handled = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_captured)
            Raise(DockPointerKind.Leave, false, false, false, Shift(e));
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => LoseCapture();

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e) => LoseCapture();

    private void LoseCapture()
    {
        if (!_captured)
            return;
        _captured = false;
        Raise(DockPointerKind.Cancel, false, false, false, false);
        LeaveIfOutside();
    }

    /// <summary>A press that ended outside the bar gets no PointerExited (the capture held it): say Leave, so the controller's leave timer runs.</summary>
    private void LeaveIfOutside()
    {
        if (Win32.GetCursorPos(out var cursor) && !InRegion(cursor.X, cursor.Y))
            Raise(DockPointerKind.Leave, false, false, false, false);
    }

    // ---------------------------------------------------------------- menus

    public void ShowMenu(IReadOnlyList<MenuEntry> entries, int screenX, int screenY, Action closed)
    {
        if (_disposed)
        {
            closed();
            return;
        }
        _menu?.Hide();
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        foreach (var item in FlyoutMenuAdapter.Build(entries))
            menu.Items.Add(item);
        nint previous = Win32.ForegroundWindow();
        menu.Opened += (_, _) =>
        {
            // The dock is topmost: the menu's own windows go above it.
            FlyoutRaiser.Raise(_hwnd);
            _window.DispatcherQueue.TryEnqueue(() => FlyoutRaiser.Raise(_hwnd)); // the popup's window may come up a moment after the event
        };
        menu.Closed += (_, _) =>
        {
            if (_menu == menu)
                _menu = null;
            if (!_disposed)
            {
                SetExStyle(WS_EX_NOACTIVATE, on: true);
                // Dismissed or chosen ("Icon size", "Remove from dock"...): a tool window must not stay the foreground window, so the window
                // the user was in gets the focus back whenever the dock still has it. A program a row started has already taken the
                // foreground (the Open rows run before Closed, and it was started by the foreground process): then the dock is not it.
                if (previous != 0 && previous != _hwnd && Win32.ForegroundWindow() == _hwnd && IsWindow(previous))
                    Win32.SetForegroundWindow(previous);
            }
            closed();
        };
        _menu = menu;
        // A menu needs the keyboard and closes when the user clicks elsewhere, which takes an active window: the dock may be activated
        // while its menu is open (as a taskbar menu is), and goes back to "never activated" when it closes.
        SetExStyle(WS_EX_NOACTIVATE, on: false);
        Win32.SetForegroundWindow(_hwnd);
        // Where a menu opens (Glass HANDOFF section 7): above the bar, its bottom edge 8 DIP above the bar's top; an icon's menu is centred
        // on the icon, the bar's on the pointer; kept 8 DIP inside the screen. A MenuFlyout with Placement Top does the keeping and the rest.
        double barLeftDip = (_bar.X - _actual.X) / Scale;
        int icon = DockGeometry.IconAt(_size, _views.Count, (screenX - _bar.X) / Scale);
        // A Position puts the menu's LEFT edge at the point; a target element is centred over, so the menu opens over a one-DIP-high anchor that is
        // as wide as the icon (an icon's menu) or one DIP wide at the pointer (the bar's), 8 DIP above the bar.
        double left = icon >= 0 ? barLeftDip + DockGeometry.IconLeftDip(_size, icon) : (screenX - _actual.X) / Scale - 0.5;
        double width = icon >= 0 ? DockGeometry.IconDip(_size) : 1;
        // WinUI leaves about 6 DIP between a Top-placed flyout and its target (measured live at 150 %: the menu's bottom was 14 DIP above the bar
        // with the anchor at 8), so the anchor sits that much lower for the menu's bottom to land 8 DIP above the bar.
        double y = (_bar.Y - _actual.Y) / Scale - DockGeometry.LabelGapDip + MenuPlacementGapDip;
        var anchor = new Border
        {
            Width = width, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(left, y, 0, 0), IsHitTestVisible = false, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _root.Children.Add(anchor);
        menu.Closed += (_, _) => _root.Children.Remove(anchor);
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
    }

    // ---------------------------------------------------------------- drops, language, the end

    public void AttachDropTarget(IDropTarget target)
    {
        if (_disposed)
            return;
        _dropTarget = target;
        _drop.RegisterOn(_hwnd, target);
        // WinUI makes its child windows late: register again once the content is up, and once more a moment later.
        After(1000, () =>
        {
            if (_dropTarget is { } later)
                _drop.RegisterOn(_hwnd, later);
        });
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_dropTarget is { } target)
            _drop.RegisterOn(_hwnd, target);
    }

    public void ApplyLanguage()
    {
        if (_disposed)
            return;
        _window.Title = $"{Strings.AppName} · {Strings.DockName}";
        _emptyText.Text = Strings.DockEmpty;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopTimers();
        _menu?.Hide();
        _drop.Revoke();
        _dropTarget = null;
        _window.SystemBackdrop = null;
        _window.Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Win32.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        _glass.Dispose();
        DisposeScene();
        // WinUI keeps its side of a closed window alive, and every handler hung on it keeps this object and its tree alive: unhook them all.
        _window.AppWindow.Closing -= OnAppWindowClosing;
        _window.Closed -= OnClosed;
        _root.PointerMoved -= OnPointerMoved;
        _root.PointerPressed -= OnPointerPressed;
        _root.PointerReleased -= OnPointerReleased;
        _root.PointerExited -= OnPointerExited;
        _root.PointerCaptureLost -= OnPointerCaptureLost;
        _root.PointerCanceled -= OnPointerCanceled;
        _root.Loaded -= OnRootLoaded;
        _root.ActualThemeChanged -= OnThemeChanged;
        _window.Content = null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);
}
