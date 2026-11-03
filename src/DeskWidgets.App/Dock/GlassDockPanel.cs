using System.Numerics;
using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinRT.Interop;

namespace DeskWidgets.Dock;

/// <summary>
/// A Glass dock group's panel (HANDOFF 1, 5 D9 and 8): its own topmost glass window above the dock with the group's name, "Open all" and the
/// members in cells of 80 x 92. It draws and reports; the controller decides (<see cref="DockPanelEvent"/>). The members are hit-tested here
/// from the geometry (<see cref="GlassDockPanelLayout"/>), as the dock does, so a cell that slides never loses the pointer.
/// <para>
/// Focus: an ordinary panel is activated, as the dock's menus are (they take the foreground while open and hand it back when they close), so
/// Esc, the rename field and "a click anywhere else" (the deactivation, judged by <see cref="PanelFocusGuard"/> like the Big Folder's panel)
/// all work; when it closes while it still has the foreground, the window the user was in gets it back, except after a member was started
/// (that program is taking the foreground). A panel opened for a file drag is shown without activation (an OLE drag must not lose its
/// target's foreground) and only the controller closes it.
/// </para>
/// The window is made once and only hidden on close.
/// </summary>
internal sealed class GlassDockPanel
{
    private const int MA_NOACTIVATE = 3;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly nint HwndTopmost = -1;

    /// <summary>A press that moves this far is a drag (D7), not a click.</summary>
    private const double DragThresholdDip = 4;

    /// <summary>A lit (D12) or dragged (D7) member's tile rises as a Medium icon does: the cells' tiles are 48.</summary>
    private const double CellLiftDip = 5;

    private const double TileSize = 48;
    private const float RiseDip = 8, SinkDip = 6;
    private static readonly Vector2 TileCentre = new(24, 24);

    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display");

    private sealed class Cell
    {
        public string Key = "";
        public DockItemView View = null!;
        public Canvas Root = null!;
        public Rectangle Hover = null!, Ring = null!, Lit = null!;
        public Canvas Lift = null!;
        public Grid TileHost = null!;
        public StackPanel Name = null!;
        public Border Pill = null!;
        public TextBlock PillText = null!;
        public string? TileState, NameState;
        public bool HoverOn, LitOn, Lifted;
        public double X = double.NaN, Y = double.NaN;
    }

    private readonly GlassDockTiles _tiles;
    private readonly Action<DockPanelEvent> _raise;
    private readonly PanelFocusGuard _guard;
    private readonly DropTargetRegistration _drop = new();
    private readonly HashSet<FrameClock.Ticket> _timers = [];

    private Window? _window;
    private GlassBackdrop? _glass;
    private WindowFader? _fader;
    private OverlappedPresenter? _presenter;
    private nint _hwnd;
    private Win32.SubclassProc? _subclass;
    private IDropTarget? _dropTarget;

    private readonly Grid _root = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Grid _frame = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _tint = new() { CornerRadius = new CornerRadius(GlassDockPanelLayout.Radius) };
    private readonly Border _sheen = new() { CornerRadius = new CornerRadius(GlassDockPanelLayout.Radius) };
    private readonly Canvas _content = new();
    private readonly Grid _titleRow = new() { Height = 28 };
    private readonly TextBlock _title = new()
    {
        FontFamily = DisplayFont, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(GlassDockPanelLayout.Side, 0, 0, 0),
    };
    private readonly TextBox _rename = new() { Visibility = Visibility.Collapsed };
    private readonly Button _openAll = new();
    private readonly TextBlock _openAllText = new() { FontFamily = TextFont, FontSize = 12 };
    // The button's lightweight-styling brushes: the template reads them once, so a theme change recolours these same objects.
    private readonly SolidColorBrush _buttonPlate = new(), _buttonPressed = new(), _buttonInk = new(), _clear = new(Colors.Transparent);
    private readonly ScrollViewer _scroll = new();
    private readonly Canvas _cells = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly UserControl _focusSink = new() { Width = 1, Height = 1, IsTabStop = true, UseSystemFocusVisuals = false, Opacity = 0 };
    private readonly TextBlock _measure = new() { TextWrapping = TextWrapping.NoWrap };

    private readonly List<Cell> _cellList = []; // by member index
    private List<DockItemView> _members = [];
    private string _titleText = "";
    private DockRect _rect;
    private double _scale = 1, _width, _height, _originX;
    private (int CentrePx, int BarTopPx, DockRect Work) _anchor;
    private bool _shown, _forDrop, _destroying, _closing, _launching, _renaming, _built;
    private nint _previous;
    private int _hover = -1, _pressed = -1, _focus = -1;
    private bool _keyboard;
    private bool _captured, _dragging, _outside;
    private int _dragFrom = -1, _dragTo = -1;
    private Point _pressPoint;
    private Vector2 _grab;
    private IReadOnlyList<bool>? _lit;
    private int _litHovered = -1;

    private readonly GlassDockDragImage _image;
    private bool _imageOn; // the dragged member is drawn by the drag picture (its cell is hidden)
    private string? _hiddenKey; // the member whose cell is hidden for the picture, until the picture has gone

    public GlassDockPanel(GlassDockTiles tiles, GlassDockDragImage image, Action<DockPanelEvent> raise)
    {
        _tiles = tiles;
        _image = image;
        _raise = raise;
        // A drag of a member out to the bar keeps the panel up whatever the foreground does meanwhile.
        _guard = new PanelFocusGuard(() => _shown && !_forDrop, () => _hwnd, () => _dragging, Dismiss, TakeForeground);
    }

    public bool IsOpen => _shown;

    public bool ForDrop => _forDrop;

    /// <summary>The window in screen pixels, as last placed.</summary>
    public DockRect Rect => _rect;

    private GlassPalette Palette => GlassPalette.For(_root.ActualTheme);

    private bool Dark => _root.ActualTheme == ElementTheme.Dark;

    private int Columns => GlassDockPanelLayout.Columns(_members.Count);

    // ---------------------------------------------------------------- opening and closing

    /// <summary>
    /// Opens (or re-aims) the panel above the group icon whose centre is at <paramref name="centrePx"/> (screen x), the bar's top at
    /// <paramref name="barTopPx"/>, on the work area <paramref name="work"/>. D9: it fades in, rises 8 and grows from 97 % about its bottom
    /// centre (Enter); shown again while closing it turns back from where it is; shown while up it changes in place.
    /// </summary>
    public void Show(int centrePx, int barTopPx, DockRect work, double scale, string title, IReadOnlyList<DockItemView> members, bool forDrop)
    {
        if (_destroying)
            return;
        bool wasShown = _shown, closing = _closing;
        bool first = _window is null;
        if (first)
            CreateWindow();
        if (!wasShown)
        {
            ResetPointer();
            _scroll.ChangeView(null, 0, null, disableAnimation: true);
            _lit = null;
            _litHovered = -1;
            _launching = false;
            if (_renaming)
                CancelRename();
        }
        _scale = scale;
        _anchor = (centrePx, barTopPx, work);
        _members = [.. members];
        if (!_renaming)
            _titleText = title;
        PlaceWindow();
        SyncCells(animate: false);
        UpdateTitle();

        _closing = false;
        _shown = true;
        _forDrop = forDrop;
        _guard.Touch();
        if (!wasShown && !closing)
            _fader!.Set(0); // nothing shows until the motion starts
        _fader!.SetClickThrough(false);
        if (forDrop)
        {
            SetExStyle(WS_EX_NOACTIVATE, on: true);
            if (first)
                _window!.AppWindow.Show(false);
            else
                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        }
        else
        {
            SetExStyle(WS_EX_NOACTIVATE, on: false);
            if (!wasShown)
            {
                nint foreground = Win32.ForegroundWindow();
                _previous = IsOwnWindow(foreground) ? _previous : foreground;
            }
            _window!.AppWindow.Show(true);
            _window.Activate();
            Win32.SetForegroundWindow(_hwnd);
        }
        Win32.MakeTransparentToolWindow(_hwnd); // showing may re-apply presenter styles
        SetExStyle(WS_EX_NOACTIVATE, on: forDrop);
        if (first)
        {
            // The presenter's own topmost flag, toggled, puts the window in the topmost band for good (as the dock's).
            _presenter!.IsAlwaysOnTop = false;
            _presenter.IsAlwaysOnTop = true;
        }
        Win32.SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE); // above the dock
        RegisterDrop();
        if (!forDrop)
            _focusSink.Focus(FocusState.Programmatic);
        if (wasShown && !closing)
            return;
        if (first)
        {
            _root.Loaded += OnFirstLoaded;
            return;
        }
        _root.DispatcherQueue.TryEnqueue(() =>
        {
            if (_shown)
                RunMotion(opening: true, fresh: !closing);
        });
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        _root.Loaded -= OnFirstLoaded;
        RegisterDrop();
        if (_shown)
            RunMotion(opening: true, fresh: true);
    }

    /// <summary>The controller closes it (D9's closing, or at once). No event: the controller knows.</summary>
    public void Hide(bool animated)
    {
        if (_renaming)
            CancelRename();
        Close(animated);
    }

    /// <summary>Esc or a click anywhere else: a rename in progress is kept (as the Big Folder's), the panel closes and says so.</summary>
    public void Dismiss()
    {
        if (!_shown)
            return;
        if (_renaming)
            CommitRename();
        Close(animated: true);
        _raise(new DockPanelEvent(DockPanelEventKind.Dismissed));
    }

    private void Close(bool animated)
    {
        if (!_shown || _window is null)
            return;
        _shown = false;
        _guard.Stop();
        if (_dragging || _captured)
        {
            _captured = false;
            _cells.ReleasePointerCaptures();
            EndDrag(commit: false);
        }
        // The window the user was in gets the foreground back while the panel still holds it; not after a member was started (that program
        // is taking it) and not for a drop panel (it never had it).
        bool launching = _launching;
        if (!launching && !_forDrop && _previous != 0 && Win32.ForegroundWindow() == _hwnd && IsWindow(_previous))
            Win32.SetForegroundWindow(_previous);
        _launching = false;
        _fader!.SetClickThrough(true); // a closing panel takes no clicks
        _closing = true;
        if (animated && MotionSettings.Enabled)
            RunMotion(opening: false, fresh: false);
        else if (launching)
            _root.DispatcherQueue.TryEnqueue(FinishClose); // the controller hides first and starts the program next: the window keeps the foreground until it has
        else
            FinishClose();
    }

    private void FinishClose()
    {
        if (!_closing || _shown || _window is null)
            return; // reopened meanwhile
        _closing = false;
        try
        {
            _fader?.Set(0);
            _window.AppWindow.Hide();
            RestPose();
            // The tiles go with the window (the next open builds fresh ones); the cells' tree is small and is made again too.
            _cells.Children.Clear();
            _cellList.Clear();
            _hiddenKey = null;
        }
        catch (Exception ex)
        {
            Log.Warn("Hiding the dock's group panel failed", ex);
        }
    }

    /// <summary>
    /// D9. Opening: the window fades in (layered alpha, Enter) while the frost and the XAML rise 8 and grow from 97 % about the bottom centre
    /// (the group icon's x). Closing: out, 6 down, 98 % (Exit), then hidden. <paramref name="fresh"/>: from the start pose; else from where it is.
    /// </summary>
    private void RunMotion(bool opening, bool fresh)
    {
        if (_fader is null || _glass is null)
            return;
        var duration = opening ? GlassMotion.Enter : GlassMotion.Exit;
        var ease = opening ? GlassEase.Out : GlassEase.In;
        var curve = GlassMotion.Curve(ease);
        if (opening)
            _fader.Fade(255, duration, curve);
        else
            _fader.Fade(0, duration, curve, FinishClose);
        if (!MotionSettings.Enabled)
        {
            RestPose();
            return;
        }
        float scaleTo = opening ? 1f : GlassMotion.PanelCloseScale, shiftTo = opening ? 0f : SinkDip;
        float px = (float)_scale;
        var centreDip = new Vector3((float)_originX, (float)_height, 0);
        ElementCompositionPreview.SetIsTranslationEnabled(_frame, true);
        var xaml = GlassMotion.VisualOf(_frame);
        xaml.CenterPoint = centreDip;
        var frost = _glass.Visual;
        if (frost is not null)
            frost.CenterPoint = centreDip * px;
        if (opening && fresh)
        {
            xaml.Scale = new Vector3(GlassMotion.PanelScale, GlassMotion.PanelScale, 1);
            xaml.Properties.InsertVector3("Translation", new Vector3(0, RiseDip, 0));
            if (frost is not null)
            {
                frost.Scale = xaml.Scale;
                frost.Offset = new Vector3(0, RiseDip * px, 0);
            }
        }
        var xamlCompositor = xaml.Compositor;
        var xamlEase = xamlCompositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
        var xamlScale = xamlCompositor.CreateVector3KeyFrameAnimation();
        xamlScale.InsertKeyFrame(1f, new Vector3(scaleTo, scaleTo, 1), xamlEase);
        xamlScale.Duration = duration;
        xaml.StartAnimation("Scale", xamlScale);
        var xamlShift = xamlCompositor.CreateVector3KeyFrameAnimation();
        xamlShift.InsertKeyFrame(1f, new Vector3(0, shiftTo, 0), xamlEase);
        xamlShift.Duration = duration;
        xaml.StartAnimation("Translation", xamlShift);
        if (frost is null)
            return;
        // The frost, on the system compositor (pixels): the same two animations, in step within a frame.
        var compositor = GlassBackdrop.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(1f, new Vector3(scaleTo, scaleTo, 1), easing);
        scaleAnimation.Duration = duration;
        frost.StartAnimation("Scale", scaleAnimation);
        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.InsertKeyFrame(1f, new Vector3(0, shiftTo * px, 0), easing);
        offsetAnimation.Duration = duration;
        frost.StartAnimation("Offset", offsetAnimation);
    }

    private void RestPose()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(_frame, true);
        var xaml = GlassMotion.VisualOf(_frame);
        xaml.StopAnimation("Scale");
        xaml.StopAnimation("Translation");
        xaml.Scale = Vector3.One;
        xaml.Properties.InsertVector3("Translation", Vector3.Zero);
        if (_glass?.Visual is { } frost)
        {
            frost.StopAnimation("Scale");
            frost.StopAnimation("Offset");
            frost.Scale = Vector3.One;
            frost.Offset = Vector3.Zero;
        }
    }

    /// <summary>The window's size for the members and its place above the group icon (all in the dock screen's DIPs, then pixels).</summary>
    private void PlaceWindow()
    {
        int count = _members.Count;
        _width = GlassDockPanelLayout.Width(count);
        _height = GlassDockPanelLayout.Height(count);
        var (centrePx, barTopPx, work) = _anchor;
        double centre = centrePx / _scale;
        var (x, y) = GlassDockPanelLayout.Place(centre, barTopPx / _scale, work.X / _scale, work.Right / _scale, work.Y / _scale, _width, _height);
        _originX = GlassDockPanelLayout.OriginX(centre, x, _width);
        int left = (int)Math.Round(x * _scale), top = (int)Math.Round(y * _scale);
        int width = (int)Math.Round(_width * _scale), height = (int)Math.Round(_height * _scale);
        _rect = new DockRect(left, top, width, height);
        Win32.SetWindowPos(_hwnd, 0, left, top, width, height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        _glass!.SetLayout(new Vector2(width, height), (float)(GlassDockPanelLayout.Radius * _scale));

        _frame.Width = _content.Width = _width;
        _frame.Height = _content.Height = _height;
        _titleRow.Width = _width;
        _scroll.Width = _width;
        _scroll.Height = GlassDockPanelLayout.ViewportHeight(count);
        _scroll.VerticalScrollMode = GlassDockPanelLayout.Rows(count) > GlassDockPanelLayout.MaxRows ? ScrollMode.Enabled : ScrollMode.Disabled;
        _cells.Width = _width;
        _cells.Height = GlassDockPanelLayout.ContentHeight(count);
    }

    // ---------------------------------------------------------------- the window

    private void CreateWindow()
    {
        _glass = new GlassBackdrop();
        _window = new Window { Title = $"{Strings.AppName} · {Strings.DockName}", SystemBackdrop = _glass };
        BuildContent();
        _window.Content = _root;
        _hwnd = WindowNative.GetWindowHandle(_window);
        _presenter = OverlappedPresenter.Create();
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsResizable = false;
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsAlwaysOnTop = true;
        _window.AppWindow.SetPresenter(_presenter);
        _window.AppWindow.IsShownInSwitchers = false;
        Win32.MakeTransparentToolWindow(_hwnd);
        _glass.Attach(_hwnd);
        _fader = new WindowFader(_hwnd);
        _fader.Set(0); // up, but see-through, until the motion starts
        _subclass = Procedure;
        Win32.SetWindowSubclass(_hwnd, _subclass, 9, 0);
        _window.Activated += OnActivated;
        _window.Closed += OnWindowClosed;
        _root.ActualThemeChanged += OnThemeChanged;
        _built = true;
        ApplyTheme();
    }

    private nint Procedure(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                Win32.EraseTransparent(hwnd, wParam);
                return 1;
            case Win32.WM_NCCALCSIZE when wParam != 0:
                return 0;
            case WM_MOUSEACTIVATE when _forDrop:
                return MA_NOACTIVATE; // a drop panel never takes the focus
            case Win32.WM_DPICHANGED:
            {
                // WinUI takes the new scale; the place and size are ours.
                nint result = Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
                Win32.SetWindowPos(_hwnd, 0, _rect.X, _rect.Y, _rect.Width, _rect.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return result;
            }
        }
        return Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Leaving for another program closes it; our own menus (a member's app menu) and the first moments do not (PanelFocusPolicy).
        if (_shown && !_forDrop && args.WindowActivationState == WindowActivationState.Deactivated)
            _guard.OnDeactivated();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_destroying)
        {
            args.Handled = true; // Alt+F4 and the like only close the panel
            Dismiss();
            return;
        }
        Win32.RemoveWindowSubclass(_hwnd, _subclass!, 9);
        _window!.Activated -= OnActivated;
        _window.Closed -= OnWindowClosed;
        _root.ActualThemeChanged -= OnThemeChanged;
        _glass?.Dispose();
        _glass = null;
        _window.Content = null;
        _window = null;
    }

    /// <summary>The panel takes the activation back (our own window that had it went away) and the keyboard focus goes where it was.</summary>
    private void TakeForeground()
    {
        if (!_shown || _forDrop || _window is null)
            return;
        _window.Activate();
        Win32.SetForegroundWindow(_hwnd);
        if (_renaming)
            _rename.Focus(FocusState.Programmatic);
        else
            _focusSink.Focus(FocusState.Programmatic);
    }

    private static bool IsOwnWindow(nint window)
    {
        if (window == 0)
            return false;
        Win32.GetWindowThreadProcessId(window, out uint process);
        return process == (uint)Environment.ProcessId;
    }

    public void AttachDropTarget(IDropTarget target)
    {
        _dropTarget = target;
        RegisterDrop();
    }

    /// <summary>The controller's drop target on the window and every window below it; WinUI makes its child windows late, so again a moment later.</summary>
    private void RegisterDrop()
    {
        if (_dropTarget is not { } target || _window is null || _destroying)
            return;
        _drop.RegisterOn(_hwnd, target);
        After(1000, () =>
        {
            if (_dropTarget is { } later && _window is not null && !_destroying)
                _drop.RegisterOn(_hwnd, later);
        });
    }

    /// <summary>The dock is going away: the window goes for good.</summary>
    public void Destroy()
    {
        _shown = false;
        _closing = false;
        _destroying = true;
        _guard.Stop();
        foreach (var ticket in _timers.ToList())
            ticket.Stop();
        _timers.Clear();
        _fader?.Dispose();
        _fader = null;
        _drop.Revoke();
        _dropTarget = null;
        if (_window is null)
            return;
        try
        {
            _window.SystemBackdrop = null;
            _window.Close();
        }
        catch (Exception ex)
        {
            Log.Warn("Closing the dock's group panel failed", ex);
        }
    }

    private FrameClock.Ticket After(int delayMs, Action action)
    {
        FrameClock.Ticket? ticket = null;
        ticket = FrameClock.After(delayMs, () =>
        {
            _timers.Remove(ticket!);
            if (!_destroying)
                action();
        });
        _timers.Add(ticket);
        return ticket;
    }

    private void SetExStyle(long bits, bool on)
    {
        long style = (long)GetWindowLongPtr(_hwnd, -20 /* GWL_EXSTYLE */);
        long wanted = on ? style | bits : style & ~bits;
        if (wanted != style)
            SetWindowLongPtr(_hwnd, -20, (nint)wanted);
    }

    // ---------------------------------------------------------------- the parts

    private void BuildContent()
    {
        _frame.Children.Add(_tint);
        _frame.Children.Add(_sheen);
        _frame.Children.Add(_content);
        _root.Children.Add(_frame);

        // The title row: the name at (20, 16) in a 28-tall row, "Open all" right-aligned 12 from the edge (HANDOFF 8).
        _titleRow.Children.Add(_title);
        StyleRenameBox();
        _titleRow.Children.Add(_rename);
        _openAll.Content = _openAllText;
        _openAll.Height = 24;
        _openAll.MinHeight = 0;
        _openAll.MinWidth = 0;
        _openAll.Padding = new Thickness(10, 0, 10, 0);
        _openAll.CornerRadius = new CornerRadius(8);
        _openAll.BorderThickness = new Thickness(0);
        _openAll.HorizontalAlignment = HorizontalAlignment.Right;
        _openAll.VerticalAlignment = VerticalAlignment.Center;
        _openAll.Margin = new Thickness(0, 0, 12, 0);
        _openAll.Background = _buttonPlate;
        _openAll.Foreground = _buttonInk;
        foreach (string key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver" })
            _openAll.Resources[key] = _buttonPlate;
        _openAll.Resources["ButtonBackgroundPressed"] = _buttonPressed;
        foreach (string key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            _openAll.Resources[key] = _buttonInk;
        foreach (string key in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            _openAll.Resources[key] = _clear;
        _openAll.Click += OnOpenAll;
        _titleRow.Children.Add(_openAll);
        Canvas.SetTop(_titleRow, 16);
        _content.Children.Add(_titleRow);

        // The cells, scrolled by WinUI's own scroll viewer past three rows (overlay bar, hides itself).
        _scroll.Content = _cells;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollMode = ScrollMode.Disabled;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Canvas.SetTop(_scroll, GlassDockPanelLayout.GridTop);
        _content.Children.Add(_scroll);
        _content.Children.Add(_focusSink);
        AutomationProperties.SetName(_focusSink, Strings.DockGroup);

        _cells.PointerPressed += OnCellsPressed;
        _cells.PointerMoved += OnCellsMoved;
        _cells.PointerReleased += OnCellsReleased;
        _cells.PointerExited += OnCellsExited;
        _cells.PointerCaptureLost += OnCellsCaptureLost;
        _cells.PointerCanceled += OnCellsCaptureLost;
        _root.KeyDown += OnKey;
        ApplyLanguage();
    }

    private void StyleRenameBox()
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        foreach (string key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled" })
            _rename.Resources[key] = transparent;
        _rename.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _rename.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _rename.BorderThickness = new Thickness(0);
        _rename.MinHeight = 0;
        _rename.Height = 28;
        _rename.Padding = new Thickness(4, 3, 4, 3);
        _rename.CornerRadius = new CornerRadius(6);
        _rename.FontFamily = DisplayFont;
        _rename.FontSize = 15;
        _rename.FontWeight = FontWeights.SemiBold;
        _rename.HorizontalAlignment = HorizontalAlignment.Left;
        _rename.VerticalAlignment = VerticalAlignment.Center;
        _rename.Margin = new Thickness(GlassDockPanelLayout.Side - 4, 0, 0, 0); // the text stays where the title's is
        _rename.KeyDown += OnRenameKey;
        _rename.LostFocus += (_, _) =>
        {
            if (_renaming && _shown)
                CommitRename();
        };
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => ApplyTheme();

    /// <summary>The cards' glass (24 % tint and the diagonal sheen, no outline, no shadow) and every colour of the theme.</summary>
    public void ApplyTheme()
    {
        if (!_built)
            return;
        bool dark = Dark;
        var palette = Palette;
        _tint.Background = new SolidColorBrush(dark ? Colors.Black : Colors.White) { Opacity = 0.24 };
        _sheen.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.7, 1),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(dark ? (byte)0x1F : (byte)0x38, 255, 255, 255), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 0.6 },
            },
        };
        _title.Foreground = palette.Primary;
        _openAllText.Foreground = palette.Primary;
        _buttonPlate.Color = palette.Hover.Color;
        _buttonPressed.Color = palette.Pressed.Color;
        _buttonInk.Color = palette.Primary.Color;
        foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            _rename.Resources[key] = palette.Pressed;
        _rename.Background = palette.Pressed;
        _rename.Foreground = palette.Primary;
        _rename.SelectionHighlightColor = palette.Select;
        foreach (var cell in _cellList)
        {
            cell.TileState = cell.NameState = null;
            cell.Hover.Fill = palette.Hover;
            cell.Lit.Fill = palette.DropTint;
            cell.Ring.Stroke = palette.Select;
            cell.Pill.Background = palette.Select;
            cell.PillText.Foreground = palette.OnSelect;
        }
        RefreshCells();
    }

    public void ApplyLanguage()
    {
        _openAllText.Text = Strings.DockOpenAll;
        AutomationProperties.SetName(_openAll, Strings.DockOpenAll);
        if (_window is not null)
            _window.Title = $"{Strings.AppName} · {Strings.DockName}";
        if (_shown)
            UpdateTitle();
    }

    private double Measure(string text, double size, FontFamily font, Windows.UI.Text.FontWeight weight)
    {
        _measure.FontFamily = font;
        _measure.FontSize = size;
        _measure.FontWeight = weight;
        _measure.Text = text;
        _measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _measure.DesiredSize.Width;
    }

    /// <summary>The title, middle-shortened to the width less 140 (HANDOFF 8).</summary>
    private void UpdateTitle()
    {
        string name = string.IsNullOrWhiteSpace(_titleText) ? Strings.DockGroup : _titleText;
        _title.Text = NameShortener.OneLine(name, _width - GlassDockPanelLayout.TitleReserve, text => Measure(text, 15, DisplayFont, FontWeights.SemiBold));
        ToolTipService.SetToolTip(_title, _title.Text == name ? null : name);
        AutomationProperties.SetName(_focusSink, name);
    }

    private void OnOpenAll(object sender, RoutedEventArgs e)
    {
        if (!_shown)
            return;
        _launching = true; // the programs are taking the foreground
        _raise(new DockPanelEvent(DockPanelEventKind.OpenAll));
    }

    // ---------------------------------------------------------------- the members

    /// <summary>The controller's new content while the panel is up: cells are kept by member, slide to their places (Move) and redraw what changed.</summary>
    public void SetMembers(string title, IReadOnlyList<DockItemView> members)
    {
        if (!_shown)
            return;
        bool resized = GlassDockPanelLayout.Width(members.Count) != _width || GlassDockPanelLayout.Height(members.Count) != _height;
        if (_dragging || _captured)
        {
            _captured = false;
            _cells.ReleasePointerCaptures();
            EndDrag(commit: false);
        }
        _members = [.. members];
        if (!_renaming)
            _titleText = title;
        if (resized)
            PlaceWindow();
        SyncCells(animate: !resized);
        UpdateTitle();
        if (_lit is not null)
            ApplyOpenWith(animate: false);
    }

    /// <summary>Matches the cells to <see cref="_members"/> by key (a new order slides), makes the new ones and drops the gone ones.</summary>
    private void SyncCells(bool animate)
    {
        var old = new Dictionary<string, Cell>();
        foreach (var cell in _cellList)
            old.TryAdd(cell.Key, cell);
        _cellList.Clear();
        foreach (var member in _members)
        {
            if (!old.Remove(member.Item.Key, out var cell))
            {
                cell = BuildCell(member);
                _cells.Children.Add(cell.Root);
            }
            cell.View = member;
            _cellList.Add(cell);
        }
        foreach (var gone in old.Values)
            _cells.Children.Remove(gone.Root);
        if (_hover >= _cellList.Count)
            _hover = -1;
        if (_focus >= _cellList.Count)
            _focus = _cellList.Count - 1;
        RefreshCells();
        Layout(animate && MotionSettings.Enabled ? GlassMotion.Move : TimeSpan.Zero);
    }

    private Cell BuildCell(DockItemView view)
    {
        var palette = Palette;
        var cell = new Cell { Key = view.Item.Key, View = view };
        cell.Root = new Canvas { Width = GlassDockPanelLayout.CellWidth, Height = GlassDockPanelLayout.CellHeight, IsHitTestVisible = false };
        cell.Hover = Plate(2, 0, 76, 90, 12, palette.Hover);
        GlassMotion.VisualOf(cell.Hover).Opacity = 0; // shown by a fade on its visual
        cell.Ring = new Rectangle { Width = 76, Height = 90, RadiusX = 12, RadiusY = 12, StrokeThickness = 2, Stroke = palette.Select, Visibility = Visibility.Collapsed };
        Canvas.SetLeft(cell.Ring, 2);
        var (litRect, litRadius) = GlassDockLayout.Plate(TileSize, 4);
        cell.Lit = Plate(litRect.X, litRect.Y, litRect.Width, litRect.Height, litRadius, palette.DropTint);
        GlassMotion.VisualOf(cell.Lit).Opacity = 0;
        cell.TileHost = new Grid { Width = TileSize, Height = TileSize };
        cell.Lift = new Canvas { Width = TileSize, Height = TileSize, Children = { cell.Lit, cell.TileHost } }; // a Canvas: the lit plate reaches past the tile
        Canvas.SetLeft(cell.Lift, 16);
        Canvas.SetTop(cell.Lift, 6);
        cell.Name = new StackPanel { Width = GlassDockPanelLayout.CellWidth, Height = 28 };
        Canvas.SetTop(cell.Name, 58);
        cell.PillText = new TextBlock { FontFamily = TextFont, FontSize = 11, TextWrapping = TextWrapping.NoWrap, Foreground = palette.OnSelect, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        cell.Pill = new Border { Height = 20, CornerRadius = new CornerRadius(10), Background = palette.Select, Padding = new Thickness(8, 0, 8, 0), Child = cell.PillText, Visibility = Visibility.Collapsed };
        Canvas.SetTop(cell.Pill, 58);
        cell.Root.Children.Add(cell.Hover);
        cell.Root.Children.Add(cell.Ring);
        cell.Root.Children.Add(cell.Lift);
        cell.Root.Children.Add(cell.Name);
        cell.Root.Children.Add(cell.Pill);
        ElementCompositionPreview.SetIsTranslationEnabled(cell.Lift, true);
        return cell;
    }

    private static Rectangle Plate(double x, double y, double width, double height, double radius, Brush fill)
    {
        var plate = new Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius, Fill = fill };
        Canvas.SetLeft(plate, x);
        Canvas.SetTop(plate, y);
        return plate;
    }

    /// <summary>Icons arrived: the tiles that changed are made again.</summary>
    public void Repaint()
    {
        if (_shown)
            RefreshCells();
    }

    private void RefreshCells()
    {
        for (int i = 0; i < _cellList.Count; i++)
            FillCell(_cellList[i], i);
    }

    /// <summary>A cell's plates, tile (a running member shows its running art, a missing one is at 40 %) and two-line name.</summary>
    private void FillCell(Cell cell, int index)
    {
        var palette = Palette;
        var view = cell.View;
        bool pressed = index == _pressed && !_dragging, hover = (index == _hover || pressed) && !_dragging;
        cell.Hover.Fill = pressed ? palette.Pressed : palette.Hover;
        if (hover != cell.HoverOn)
        {
            cell.HoverOn = hover;
            GlassMotion.FadeTo(cell.Hover, hover ? 1 : 0, hover ? GlassMotion.Quick : GlassMotion.Exit, hover ? GlassEase.Out : GlassEase.In);
        }
        cell.Ring.Visibility = _keyboard && index == _focus ? Visibility.Visible : Visibility.Collapsed;

        var (state, build, buildRunning, _) = _tiles.Describe(view, palette, Dark, TileSize, _scale);
        bool running = view.State == DockRunState.Running && buildRunning is not null;
        string tileState = $"{state}|{running}|{view.Missing}";
        if (tileState != cell.TileState)
        {
            cell.TileState = tileState;
            cell.TileHost.Children.Clear();
            var tile = running ? buildRunning!() : build();
            tile.Opacity = view.Missing ? 0.4 : 1;
            cell.TileHost.Children.Add(tile);
        }

        string nameState = $"{view.Name}|{view.Missing}|{Dark}";
        if (nameState != cell.NameState)
        {
            cell.NameState = nameState;
            cell.Name.Children.Clear();
            // Two lines of Text 11 (line 14) in a 76 x 28 box at (2, 58), centred, as the Big Folder's cells.
            var (first, second) = NameShortener.TwoLines(view.Name, 76, text => Measure(text, 11, TextFont, FontWeights.Normal));
            foreach (string line in second.Length == 0 ? [first] : new[] { first, second })
            {
                cell.Name.Children.Add(new TextBlock
                {
                    Text = line, FontFamily = TextFont, FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = view.Missing ? palette.Inactive : palette.Primary,
                });
            }
        }
        AutomationProperties.SetName(cell.Root, view.Name);
    }

    /// <summary>Every cell to its place (the dragged one excepted: it follows the pointer), over <paramref name="duration"/> from where it is.</summary>
    private void Layout(TimeSpan duration)
    {
        int columns = Columns;
        var places = GlassDockPanelLayout.Places(_cellList.Count, _dragging ? _dragFrom : -1, _dragging ? _dragTo : -1);
        for (int i = 0; i < _cellList.Count; i++)
        {
            if (_dragging && i == _dragFrom)
                continue;
            var (x, y) = GlassDockPanelLayout.CellOrigin(places[i], columns);
            MoveCell(_cellList[i], x, y, duration);
        }
    }

    private static void MoveCell(Cell cell, double x, double y, TimeSpan duration)
    {
        if (cell.X == x && cell.Y == y)
            return;
        bool first = double.IsNaN(cell.X);
        cell.X = x;
        cell.Y = y;
        Translate(cell.Root, new Vector2((float)x, (float)y), first ? TimeSpan.Zero : duration);
    }

    private static void Translate(UIElement element, Vector2 to, TimeSpan duration)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = GlassMotion.VisualOf(element);
        var target = new Vector3(to, 0);
        if (duration <= TimeSpan.Zero || !MotionSettings.Enabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", target);
            return;
        }
        var curve = Bezier.Move;
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, target, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2)));
        animation.Duration = duration;
        visual.StartAnimation("Translation", animation);
    }

    /// <summary>A tile's rise (D12's lit members, D7's dragged one): Quick up, Exit down.</summary>
    private static void LiftCell(Cell cell, bool up)
    {
        if (up == cell.Lifted)
            return;
        cell.Lifted = up;
        var visual = GlassMotion.VisualOf(cell.Lift);
        var target = new Vector3(0, up ? -(float)CellLiftDip : 0, 0);
        if (!MotionSettings.Enabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", target);
            return;
        }
        var curve = up ? Bezier.Out : Bezier.In;
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, target, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2)));
        animation.Duration = up ? GlassMotion.Quick : GlassMotion.Exit;
        visual.StartAnimation("Translation", animation);
    }

    // ---------------------------------------------------------------- D12, open with

    public void SetOpenWith(IReadOnlyList<bool>? lit, int hovered)
    {
        if (!_shown)
            return;
        _lit = lit;
        _litHovered = lit is null ? -1 : hovered;
        ApplyOpenWith(animate: true);
    }

    /// <summary>
    /// D12 in the panel: a member that takes the files gets the Drop-tint plate (4 larger than its tile) and the lift (Quick in, Exit out); the
    /// lit member under the pointer shows "Open with {name}" as a Select pill in its name row, over its neighbours and kept 8 inside the panel.
    /// </summary>
    private void ApplyOpenWith(bool animate)
    {
        for (int i = 0; i < _cellList.Count; i++)
        {
            var cell = _cellList[i];
            bool on = _lit is { } lit && i < lit.Count && lit[i];
            if (on != cell.LitOn)
            {
                cell.LitOn = on;
                var duration = animate ? (on ? GlassMotion.Quick : GlassMotion.Exit) : TimeSpan.Zero;
                GlassMotion.FadeTo(cell.Lit, on ? 1 : 0, duration, on ? GlassEase.Out : GlassEase.In);
                LiftCell(cell, on || _dragging && i == _dragFrom);
            }
            bool pill = on && i == _litHovered;
            if (pill)
            {
                double max = _width - 2 * GlassDockPanelLayout.ScreenMargin - 16;
                cell.PillText.Text = NameShortener.OneLine(Strings.DockOpenWith(cell.View.Name), max, text => Measure(text, 11, TextFont, FontWeights.Normal));
                double width = Math.Ceiling(Measure(cell.PillText.Text, 11, TextFont, FontWeights.Normal)) + 16;
                double left = Math.Clamp(cell.X + GlassDockPanelLayout.CellWidth / 2 - width / 2, GlassDockPanelLayout.ScreenMargin, _width - GlassDockPanelLayout.ScreenMargin - width);
                cell.Pill.Width = width;
                Canvas.SetLeft(cell.Pill, left - cell.X);
            }
            cell.Pill.Visibility = pill ? Visibility.Visible : Visibility.Collapsed;
            cell.Name.Visibility = pill ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetZIndex(cell.Root, pill ? 2 : 0);
        }
    }

    /// <summary>The member under a screen pixel, or -1 (the title row, the margins, out of the scrolled view).</summary>
    public int MemberAt(int screenX, int screenY)
    {
        if (!_shown || !_rect.Contains(screenX, screenY))
            return -1;
        double x = (screenX - _rect.X) / _scale, y = (screenY - _rect.Y) / _scale - GlassDockPanelLayout.GridTop;
        if (y < 0 || y >= _scroll.Height)
            return -1;
        return GlassDockPanelLayout.CellAt(x, y + _scroll.VerticalOffset, _cellList.Count, Columns);
    }

    // ---------------------------------------------------------------- rename

    /// <summary>The title becomes an edit field (as the Big Folder's rename): Enter keeps it (Renamed), Esc cancels, leaving the field keeps it.</summary>
    public void BeginRename()
    {
        if (!_shown || _window is null)
            return;
        if (_forDrop)
        {
            // Typing needs the keyboard: a drop panel becomes an ordinary one.
            _forDrop = false;
            SetExStyle(WS_EX_NOACTIVATE, on: false);
            nint foreground = Win32.ForegroundWindow();
            if (!IsOwnWindow(foreground))
                _previous = foreground;
        }
        _renaming = true;
        _rename.Text = string.IsNullOrWhiteSpace(_titleText) ? Strings.DockGroup : _titleText;
        double buttonWidth = Math.Ceiling(Measure(_openAllText.Text, 12, TextFont, FontWeights.Normal)) + 20;
        _rename.Width = Math.Max(80, _width - GlassDockPanelLayout.Side - 12 - buttonWidth - 8 + 4);
        _rename.Visibility = Visibility.Visible;
        _title.Visibility = Visibility.Collapsed;
        _guard.Touch();
        TakeForeground();
        _rename.SelectAll();
        // Started from the dock's menu: the closing menu may take the activation back a moment later.
        After(PanelFocusPolicy.RecheckMilliseconds, () =>
        {
            if (_renaming && _shown && Win32.ForegroundWindow() != _hwnd)
                TakeForeground();
        });
    }

    private void OnRenameKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CommitRename()
    {
        if (!_renaming)
            return;
        string typed = _rename.Text.Trim();
        string before = string.IsNullOrWhiteSpace(_titleText) ? Strings.DockGroup : _titleText;
        EndRename();
        if (typed == before)
            return;
        _titleText = typed;
        UpdateTitle();
        _raise(new DockPanelEvent(DockPanelEventKind.Renamed, Text: typed));
    }

    private void CancelRename()
    {
        if (_renaming)
            EndRename();
    }

    private void EndRename()
    {
        _renaming = false;
        _rename.Visibility = Visibility.Collapsed;
        _title.Visibility = Visibility.Visible;
        if (_shown && !_forDrop)
            _focusSink.Focus(FocusState.Programmatic);
    }

    // ---------------------------------------------------------------- the pointer: click, right-click, reorder, drag out (D7)

    private void ResetPointer()
    {
        _hover = _pressed = _focus = -1;
        _keyboard = false;
        _captured = _dragging = _outside = _imageOn = false;
        _dragFrom = _dragTo = -1;
    }

    private int CellAt(Point p) => GlassDockPanelLayout.CellAt(p.X, p.Y, _cellList.Count, Columns);

    private void SetHover(int index)
    {
        if (index == _hover)
            return;
        int old = _hover;
        _hover = index;
        RestyleCell(old);
        RestyleCell(index);
    }

    private void RestyleCell(int index)
    {
        if (index >= 0 && index < _cellList.Count)
            FillCell(_cellList[index], index);
    }

    private void OnCellsPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_cells);
        e.Handled = true;
        if (!_shown || !point.Properties.IsLeftButtonPressed || _captured)
            return;
        if (!_forDrop)
            _focusSink.Focus(FocusState.Pointer);
        if (_keyboard)
        {
            _keyboard = false;
            RestyleCell(_focus);
        }
        int index = CellAt(point.Position);
        if (index < 0)
            return;
        _pressed = index;
        _pressPoint = point.Position;
        _captured = _cells.CapturePointer(e.Pointer);
        // A3: the tile goes down to 94 % on the press (Press) and back on the release (Release).
        GlassMotion.ScaleTo(_cellList[index].TileHost, GlassMotion.PressScale, GlassMotion.Press, GlassEase.Out, TileCentre);
        RestyleCell(index);
    }

    private void OnCellsMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_shown)
            return;
        var point = e.GetCurrentPoint(_cells);
        var p = point.Position;
        if (_captured && _pressed >= 0 && point.Properties.IsLeftButtonPressed)
        {
            if (!_dragging && Math.Max(Math.Abs(p.X - _pressPoint.X), Math.Abs(p.Y - _pressPoint.Y)) >= DragThresholdDip)
                BeginDrag();
            if (_dragging)
            {
                DragTo(p);
                return;
            }
        }
        if (!_captured)
            SetHover(CellAt(p));
    }

    private void OnCellsReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_cells);
        var kind = point.Properties.PointerUpdateKind;
        e.Handled = true;
        if (!_shown)
            return;
        if (kind == PointerUpdateKind.LeftButtonReleased && _pressed >= 0)
        {
            int pressed = _pressed;
            bool dragging = _dragging;
            _pressed = -1;
            if (_captured)
            {
                _captured = false; // first, so the capture-lost event this raises is not a cancel
                _cells.ReleasePointerCapture(e.Pointer);
            }
            if (dragging)
            {
                EndDrag(commit: true);
                return;
            }
            GlassMotion.ScaleTo(_cellList[pressed].TileHost, 1, GlassMotion.Release, GlassEase.Out, TileCentre);
            int over = CellAt(point.Position);
            SetHover(over);
            RestyleCell(pressed);
            if (over == pressed)
                Click(pressed);
        }
        else if (kind == PointerUpdateKind.RightButtonReleased && !_dragging)
        {
            int index = CellAt(point.Position);
            if (index >= 0 && Win32.GetCursorPos(out var at))
                _raise(new DockPanelEvent(DockPanelEventKind.RightClick, index, ScreenX: at.X, ScreenY: at.Y));
        }
    }

    private void Click(int index)
    {
        _launching = true; // the program is taking the foreground: closing must not hand it to the old window
        _raise(new DockPanelEvent(DockPanelEventKind.Click, index));
    }

    private void OnCellsExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_captured)
            SetHover(-1);
    }

    private void OnCellsCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_captured)
            return;
        _captured = false;
        int pressed = _pressed;
        _pressed = -1;
        if (_dragging)
            EndDrag(commit: false);
        else if (pressed >= 0 && pressed < _cellList.Count)
        {
            GlassMotion.ScaleTo(_cellList[pressed].TileHost, 1, GlassMotion.Release, GlassEase.Out, TileCentre);
            RestyleCell(pressed);
        }
    }

    /// <summary>D7: the pressed member lifts and follows the pointer at full opacity, over the others.</summary>
    private void BeginDrag()
    {
        _dragging = true;
        _dragFrom = _dragTo = _pressed;
        _outside = false;
        var cell = _cellList[_dragFrom];
        _grab = new Vector2((float)(_pressPoint.X - cell.X), (float)(_pressPoint.Y - cell.Y));
        GlassMotion.ScaleTo(cell.TileHost, 1, GlassMotion.Release, GlassEase.Out, TileCentre);
        LiftCell(cell, true);
        Canvas.SetZIndex(cell.Root, 3);
        SetHover(-1);
        RefreshCells();
        // The picture moves to a window of its own as soon as it is rendered, so it stays whole past the panel's edge and over the bar;
        // until then (a frame or two) the cell itself follows the pointer.
        if (cell.TileHost.Children.FirstOrDefault() is FrameworkElement tile)
        {
            string key = cell.Key;
            _image.Capture(tile, () =>
            {
                if (!_dragging || _dragFrom < 0 || _cellList[_dragFrom].Key != key)
                    return;
                _imageOn = true;
                _hiddenKey = key;
                SetCellShown(_cellList[_dragFrom], false);
                PlaceImage(_cellList[_dragFrom]);
            });
        }
    }

    /// <summary>The drag picture over the cell's tile, lifted (D7), in screen pixels: the cell's place is the pointer's, in content coordinates.</summary>
    private void PlaceImage(Cell cell)
    {
        double x = cell.X + 16, y = GlassDockPanelLayout.GridTop + cell.Y - _scroll.VerticalOffset + 6 - CellLiftDip;
        _image.MoveTo(_rect.X + (int)Math.Round(x * _scale), _rect.Y + (int)Math.Round(y * _scale));
    }

    private static void SetCellShown(Cell cell, bool shown)
    {
        var visual = GlassMotion.VisualOf(cell.Root);
        visual.StopAnimation("Opacity");
        visual.Opacity = shown ? 1 : 0;
    }

    /// <summary>The drag picture has gone (landed on the bar, or the drag ended here): the member's cell shows again, if it is still here.</summary>
    public void RestoreDragged()
    {
        if (_hiddenKey is null)
            return;
        if (_cellList.FirstOrDefault(c => c.Key == _hiddenKey) is { } cell)
            SetCellShown(cell, true);
        _hiddenKey = null;
    }

    /// <summary>
    /// Inside the panel the others slide to show where it would land (Move); outside it they go back, and the controller hears every move
    /// (DragMove, the screen point) so it can show where it would land on the bar.
    /// </summary>
    private void DragTo(Point p)
    {
        var cell = _cellList[_dragFrom];
        var at = new Vector2((float)p.X, (float)p.Y) - _grab;
        cell.X = at.X;
        cell.Y = at.Y;
        Translate(cell.Root, at, TimeSpan.Zero); // hidden while the picture draws it, but kept under the pointer: the drop slides it from there
        if (_imageOn)
            PlaceImage(cell);
        if (!Win32.GetCursorPos(out var cursor))
            return;
        bool outside = !_rect.Contains(cursor.X, cursor.Y);
        int to = outside ? _dragFrom : GlassDockPanelLayout.SlotAt(p.X, p.Y, _cellList.Count, Columns);
        _outside = outside;
        if (to != _dragTo)
        {
            _dragTo = to;
            Layout(GlassMotion.Move);
        }
        if (outside)
            _raise(new DockPanelEvent(DockPanelEventKind.DragMove, _dragFrom, ScreenX: cursor.X, ScreenY: cursor.Y));
    }

    /// <summary>
    /// The drag ends. Let go outside the panel: DragOut (the member, the screen point), and the cell goes back until the controller's new list
    /// comes. Let go inside on another place: the cell settles there (Move), the order is kept here, and Reorder (from, to) is raised. Else,
    /// or cancelled: it settles back.
    /// </summary>
    private void EndDrag(bool commit)
    {
        if (!_dragging)
            return;
        int from = _dragFrom, to = _dragTo;
        bool outside = _outside;
        var cell = _cellList[from];
        _dragging = false;
        _outside = false;
        _dragFrom = _dragTo = -1;
        LiftCell(cell, cell.LitOn);
        Canvas.SetZIndex(cell.Root, 0);
        bool reorder = commit && !outside && to >= 0 && to != from;
        if (reorder)
        {
            var member = _members[from];
            _members.RemoveAt(from);
            _members.Insert(to, member);
            _cellList.RemoveAt(from);
            _cellList.Insert(to, cell);
        }
        bool imageOn = _imageOn;
        _imageOn = false;
        // Every cell to its place, the dropped one from where the pointer left it (its X and Y are the pointer's: it slides).
        Layout(GlassMotion.Move);
        RefreshCells();
        if (commit && outside && Win32.GetCursorPos(out var at))
        {
            // The dock may take the picture over (it flies to the gap with Move, the cell stays hidden until it has landed); if not, the
            // picture goes and the cell slides back from the pointer.
            _raise(new DockPanelEvent(DockPanelEventKind.DragOut, from, ScreenX: at.X, ScreenY: at.Y));
            if (imageOn && _image.Claimed)
                return;
        }
        else if (reorder)
        {
            _raise(new DockPanelEvent(DockPanelEventKind.Reorder, from, to));
        }
        _image.Hide();
        RestoreDragged();
    }

    // ---------------------------------------------------------------- keys

    /// <summary>Esc closes; the arrows move a focus ring over the members, Enter or Space starts one, the menu key (or Shift+F10) opens its menu.</summary>
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (!_shown || e.OriginalSource is TextBox || _renaming)
            return;
        if (e.OriginalSource is Button && e.Key is VirtualKey.Enter or VirtualKey.Space)
            return; // the button's own
        int count = _cellList.Count, columns = Columns;
        bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Dismiss();
                break;
            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down when count > 0:
            {
                int step = e.Key switch { VirtualKey.Left => -1, VirtualKey.Right => 1, VirtualKey.Up => -columns, _ => columns };
                int next = _focus < 0 ? 0 : Math.Clamp(_focus + step, 0, count - 1);
                int old = _focus;
                _focus = next;
                _keyboard = true;
                RestyleCell(old);
                RestyleCell(next);
                var (_, y) = GlassDockPanelLayout.CellOrigin(next, columns);
                if (y < _scroll.VerticalOffset || y + GlassDockPanelLayout.CellHeight > _scroll.VerticalOffset + _scroll.Height)
                    _scroll.ChangeView(null, y, null);
                break;
            }
            case VirtualKey.Enter or VirtualKey.Space when _focus >= 0 && _focus < count:
                Click(_focus);
                break;
            case VirtualKey.Application when _focus >= 0 && _focus < count:
            case VirtualKey.F10 when shift && _focus >= 0 && _focus < count:
            {
                var (x, y) = GlassDockPanelLayout.CellOrigin(_focus, columns);
                int sx = _rect.X + (int)Math.Round((x + GlassDockPanelLayout.CellWidth / 2) * _scale);
                int sy = _rect.Y + (int)Math.Round((GlassDockPanelLayout.GridTop + y - _scroll.VerticalOffset + 30) * _scale);
                _raise(new DockPanelEvent(DockPanelEventKind.RightClick, _focus, ScreenX: sx, ScreenY: sy));
                break;
            }
            default:
                return;
        }
        e.Handled = true;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);
}
