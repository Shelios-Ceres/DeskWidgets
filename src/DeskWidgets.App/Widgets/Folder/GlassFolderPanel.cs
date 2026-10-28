using System.Numerics;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text;
using WinRT.Interop;
using FontWeight = Windows.UI.Text.FontWeight;
using Path = System.IO.Path;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The Big Folder's panel in the Glass theme (the Glass design's HANDOFF §3): a glass window beside the card with the folder's name
/// and count, a sort button, a search field, every item with a two-line name, Finder-style selection, and a footer with the path.
/// A click selects (Ctrl and Shift extend), a double-click or Enter opens, Esc clears the search and then closes, typing anywhere
/// searches, and the selection drags out to Explorer and the desktop. It closes on a click outside.
/// The window is made on the first open and only hidden on close, then reset as if new on the next open: a WinUI window that is created
/// and destroyed over and over leaves about four kernel handles behind each time (Phase 6). <see cref="Destroy"/> ends it for good.
/// </summary>
internal sealed class GlassFolderPanel : IFolderPanel
{
    private const double PanelWidth = 440;
    private const double ViewportTop = 96;
    private const double CellWidth = 80, CellHeight = 92;
    private const int Columns = 5;

    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");

    private readonly FolderWidget _widget;

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CellView> _cells = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CellView> _byIndex = [];
    private readonly List<(CellView Cell, DateTime Until)> _leaving = [];
    private HashSet<string>? _knownKeys; // the file names the list had before the last change; null until the panel has shown a list
    private readonly HashSet<string> _arrivals = new(StringComparer.OrdinalIgnoreCase);
    private bool _animateChanges; // the panel is up and showed its list once: a change of the list may animate
    private int _tileRevision;
    private FrameClock.Ticket? _sweeper;
    private bool _noMatchesShown;
    private DateTime _noMatchesCollapseAt = DateTime.MaxValue;
    private readonly Canvas _canvas = new();
    private readonly Canvas _content = new();
    private readonly UserControl _gridFocus = new() { Width = 1, Height = 1, IsTabStop = true, UseSystemFocusVisuals = false, Opacity = 0 };
    private readonly ScrollViewer _scroll = new();
    private readonly TextBlock _title = new() { FontFamily = DisplayFont, FontSize = 17, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _count = new() { FontFamily = TextFont, FontSize = 12 };
    private readonly TextBlock _path = new() { FontFamily = TextFont, FontSize = 11 };
    private readonly TextBlock _selectedText = new() { FontFamily = TextFont, FontSize = 11 };
    private readonly TextBlock _noMatches = new() { FontFamily = TextFont, FontSize = 13, TextAlignment = TextAlignment.Center, Width = PanelWidth, Visibility = Visibility.Collapsed };
    private readonly TextBlock _sortText = new() { FontFamily = TextFont, FontSize = 12 };
    private readonly Border _sortButton = new() { Height = 24, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0) };
    private readonly Border _searchBox = new() { Height = 32, Width = 400, CornerRadius = new CornerRadius(10) };
    private readonly TextBox _search = new();
    private readonly TextBox _rename = new();
    private readonly Border _clear = new() { Width = 24, Height = 24, Visibility = Visibility.Collapsed };
    private GlassBackdrop? _glass;
    private WindowFader? _fader;
    private Action? _hideAfterClose; // set while a close is still fading out: the hide and the clean-up it ends with
    private PanelSide _side = PanelSide.Right;
    private Window? _window;
    private WidgetFrame? _frame;
    private Grid? _root;
    private nint _hwnd;
    private Win32.SubclassProc? _subclass;
    private List<FolderItem> _view = [];
    private double _height = 500;
    private double _scale = 1;
    private string? _renaming; // the file name being renamed in place
    private bool _renamingFolder;
    private int _hover = -1, _pressed = -1, _focus = -1, _anchor = -1;
    private bool _keyboardFocus;
    private bool _collapseOnRelease;
    private bool _menuOpen;
    private bool _shown; // the window is up (it may exist, hidden, while this is false)
    private bool _destroying;
    private double _builtHeight;
    private MenuFlyout? _flyout;
    private readonly PanelFocusGuard _guard;
    private GlassTileProvider? _tiles;
    private TextBox? _itemBox; // the box of the item being renamed

    public GlassFolderPanel(FolderWidget widget)
    {
        _widget = widget;
        _guard = new PanelFocusGuard(() => _shown, () => _hwnd, () => _menuOpen || _widget.PanelPinned, Close, TakeForeground);
    }

    public event Action? Closed;

    public bool IsOpen => _shown;

    public bool Reusable => true;

    public nint Handle => _shown ? _hwnd : 0;

    public IReadOnlyList<string> ViewFileNames => _view.Select(i => i.FileName).ToList();

    public void Reclaim() => _guard.Reclaim(afterOperation: true);

    /// <summary>Our own work is over (or the card's menu started a rename here): the panel takes the activation, and the keyboard focus goes back to where it belongs.</summary>
    private void TakeForeground()
    {
        if (!_shown || _window is null)
            return;
        _window.Activate();
        if (_renamingFolder)
            _rename.Focus(FocusState.Programmatic);
        else if (_renaming is not null)
            _itemBox?.Focus(FocusState.Programmatic);
        else if (_search.FocusState == FocusState.Unfocused)
            _gridFocus.Focus(FocusState.Programmatic);
    }

    private FolderModel Model => _widget.Model;

    private IWidgetSite Site => _widget.Site;

    private GlassPalette Palette => GlassPalette.For(_root?.ActualTheme ?? ElementTheme.Default);

    private bool Dark => _root?.ActualTheme == ElementTheme.Dark;

    // ----- opening -----

    public void Open()
    {
        if (_shown)
            return;
        bool closing = _hideAfterClose is not null; // reopened while the last close is still fading out
        _scale = Site.Scale;
        var card = Site.CardRect;
        var center = new Win32.POINT { X = (card.Left + card.Right) / 2, Y = (card.Top + card.Bottom) / 2 };
        var work = Win32.GetWorkArea(center);
        double workHeight = work.Height / _scale;
        _height = workHeight < 516 ? Math.Max(344, workHeight - 16) : 500;

        bool first = _window is null;
        if (first)
        {
            CreateWindow();
        }
        else
        {
            if (closing)
                DiscardContent(); // the window stays up, the old close is cancelled, and the alpha goes on from where it is
            ResetForReopen();
        }

        int width = (int)Math.Round(PanelWidth * _scale), height = (int)Math.Round(_height * _scale);
        var position = Place(card, work, width, height);
        _side = position.X >= card.Right ? PanelSide.Right : position.X + width <= card.Left ? PanelSide.Left : PanelSide.Centre;
        _window!.AppWindow.Move(position);
        _window.AppWindow.Resize(new SizeInt32(width, height));
        _glass!.SetLayout(new Vector2(width, height), (float)(WidgetLayout.CornerRadius * _scale));

        // Nothing shows until the content is there: the window comes up fully transparent, then fades in (A1).
        _fader!.SetClickThrough(false);
        if (!closing)
            _fader.Set(0);
        _guard.Touch();
        _shown = true;
        _hideAfterClose = null;
        if (!first)
            _window.AppWindow.Show();
        _window.Activate();
        Win32.MakeTransparentToolWindow(_hwnd); // activation may re-apply presenter styles
        if (first)
        {
            _root!.Loaded += (_, _) =>
            {
                Refresh();
                FocusSearchUnlessRenaming();
                RunPanelMotion(opening: true);
            };
            return;
        }
        // The motion starts once the window is up, as on the first open.
        _root!.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_shown)
                return;
            FocusSearchUnlessRenaming();
            RunPanelMotion(opening: true);
        });
    }

    /// <summary>The search field takes the focus as the panel opens, unless a rename started from the card's menu already has its box up.</summary>
    private void FocusSearchUnlessRenaming()
    {
        if (_renaming is null && !_renamingFolder)
            _search.Focus(FocusState.Programmatic);
    }

    private enum PanelSide
    {
        Right,
        Left,
        Centre,
    }

    /// <summary>
    /// A1. Opening: the whole window fades in (layered alpha, Enter), and the frost and the XAML card travel 8 DIP away from the card
    /// and scale from 97 % about the panel's top corner on the card's side. Closing: to nothing, 6 DIP back toward the card, at 98 % (Exit).
    /// The window is at its final place and size from the first frame; only these animate.
    /// </summary>
    private void RunPanelMotion(bool opening)
    {
        if (_fader is null || _glass?.Visual is not { } frost || _frame is null)
            return;
        double dipToPx = _scale;
        float toward = _side switch { PanelSide.Right => -1f, PanelSide.Left => 1f, _ => 0f };
        float startShift = (opening ? 8f : 6f) * toward, startScale = opening ? GlassMotion.PanelScale : GlassMotion.PanelCloseScale;
        var xaml = GlassMotion.VisualOf(_frame);
        float widthPx = (float)(PanelWidth * dipToPx);
        var cornerPx = _side switch { PanelSide.Right => new Vector3(0, 0, 0), PanelSide.Left => new Vector3(widthPx, 0, 0), _ => new Vector3(widthPx / 2, 0, 0) };
        var cornerDip = new Vector2(cornerPx.X / (float)dipToPx, 0);
        var duration = opening ? GlassMotion.Enter : GlassMotion.Exit;
        var ease = opening ? GlassEase.Out : GlassEase.In;
        var curve = GlassMotion.Curve(ease);

        _animateChanges = opening; // changes of the list animate while the panel is up
        if (opening)
            _fader.Fade(255, duration, curve);
        else
            _fader.Fade(0, duration, curve, FinishClose);

        if (!MotionSettings.Enabled)
        {
            RestPose(frost, _frame);
            return;
        }
        float scaleFrom = opening ? startScale : 1f, scaleTo = opening ? 1f : startScale;
        float shiftFrom = opening ? startShift : 0f, shiftTo = opening ? 0f : startShift;

        // The frost, on the system compositor (pixels).
        var compositor = GlassBackdrop.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
        frost.CenterPoint = cornerPx;
        frost.Scale = new Vector3(scaleFrom, scaleFrom, 1);
        frost.Offset = new Vector3(shiftFrom * (float)dipToPx, 0, 0);
        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(1f, new Vector3(scaleTo, scaleTo, 1), easing);
        scaleAnimation.Duration = duration;
        frost.StartAnimation("Scale", scaleAnimation);
        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.InsertKeyFrame(1f, new Vector3(shiftTo * (float)dipToPx, 0, 0), easing);
        offsetAnimation.Duration = duration;
        frost.StartAnimation("Offset", offsetAnimation);

        // The XAML card, on the WinUI compositor (DIPs): the same two animations, in step within a frame.
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(_frame, true);
        xaml.CenterPoint = new Vector3(cornerDip, 0);
        xaml.Scale = new Vector3(scaleFrom, scaleFrom, 1);
        xaml.Properties.InsertVector3("Translation", new Vector3(shiftFrom, 0, 0));
        var xamlCompositor = xaml.Compositor;
        var xamlEasing = xamlCompositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
        var xamlScale = xamlCompositor.CreateVector3KeyFrameAnimation();
        xamlScale.InsertKeyFrame(1f, new Vector3(scaleTo, scaleTo, 1), xamlEasing);
        xamlScale.Duration = duration;
        xaml.StartAnimation("Scale", xamlScale);
        var xamlShift = xamlCompositor.CreateVector3KeyFrameAnimation();
        xamlShift.InsertKeyFrame(1f, new Vector3(shiftTo, 0, 0), xamlEasing);
        xamlShift.Duration = duration;
        xaml.StartAnimation("Translation", xamlShift);
    }

    /// <summary>The panel at rest: no scale, no offset, on both compositors.</summary>
    private static void RestPose(Windows.UI.Composition.SpriteVisual frost, FrameworkElement frame)
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(frame, true);
        var xaml = GlassMotion.VisualOf(frame);
        frost.StopAnimation("Scale");
        frost.StopAnimation("Offset");
        frost.Scale = Vector3.One;
        frost.Offset = Vector3.Zero;
        xaml.StopAnimation("Scale");
        xaml.StopAnimation("Translation");
        xaml.Scale = Vector3.One;
        xaml.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    /// <summary>The first open: the window, its content, the glass and the hooks.</summary>
    private void CreateWindow()
    {
        _glass = new GlassBackdrop();
        _window = new Window { Title = Strings.AppName + " · " + Model.Name, SystemBackdrop = _glass };
        _frame = new WidgetFrame { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = PanelWidth, Height = _height };
        _frame.SetCard(WidgetTheme.Glass, WidgetSize.Large);
        BuildContent();
        _builtHeight = _height;
        _frame.SetContent(_canvas);
        _root = new Grid();
        _root.Children.Add(_frame);
        _root.KeyDown += OnGridKey;
        _root.CharacterReceived += OnCharacter;
        _window.Content = _root;

        _hwnd = WindowNative.GetWindowHandle(_window);
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        _window.AppWindow.SetPresenter(presenter);
        _window.AppWindow.IsShownInSwitchers = false;
        Win32.MakeTransparentToolWindow(_hwnd);
        _glass.Attach(_hwnd);
        _fader = new WindowFader(_hwnd);
        _fader.Set(0); // up, but see-through, until the content is there
        // The first Chinese filter of a process loads the pinyin table; that is paid here, while the window is still invisible.
        _ = Search.NameSearch.Matches("壁纸", new Search.NameQuery("bz"));
        _subclass = Procedure;
        Win32.SetWindowSubclass(_hwnd, _subclass, 7, 0);

        _window.Activated += OnActivated;
        _window.Closed += OnWindowClosed;
        _tiles = new GlassTileProvider(() => _scale, () => Dark, RepaintSoon);
    }

    /// <summary>The next open of the hidden window: everything as in a new panel (empty search, no selection, the top of the list, fresh items).</summary>
    private void ResetForReopen()
    {
        _window!.Title = Strings.AppName + " · " + Model.Name;
        if (Math.Abs(_height - _builtHeight) > 0.5)
            ApplyHeight();
        _renaming = null;
        _renamingFolder = false;
        _rename.Visibility = Visibility.Collapsed;
        _selected.Clear();
        _hover = _pressed = _focus = _anchor = -1;
        _keyboardFocus = false;
        _collapseOnRelease = false;
        _menuOpen = false;
        _search.Text = "";
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
        _tiles = new GlassTileProvider(() => _scale, () => Dark, RepaintSoon);
        _knownKeys = null;
        _animateChanges = false;
        _noMatchesShown = false;
        _noMatches.Visibility = Visibility.Collapsed;
        GlassMotion.Rest(_noMatches);
        _shown = true; // Refresh does nothing while the panel is hidden
        Refresh();
        _shown = false;
    }

    /// <summary>The work area is shorter or taller than when the window was built: the card, the list and the footer follow.</summary>
    private void ApplyHeight()
    {
        _builtHeight = _height;
        _frame!.Height = _height;
        _canvas.Height = _height;
        _scroll.Height = _height - ViewportTop - 44;
        Canvas.SetTop(_path, _height - 36 + 1);
        Canvas.SetTop(_selectedText, _height - 36 + 1);
    }

    /// <summary>Beside the card, 12 away, tops aligned: on the right if it fits (8 to spare), else on the left; up if it would run off the bottom; centred over the card if neither side fits.</summary>
    private PointInt32 Place(Win32.RECT card, Win32.RECT work, int width, int height)
    {
        int gap = (int)Math.Round(12 * _scale), spare = (int)Math.Round(8 * _scale);
        int x;
        if (card.Right + gap + width <= work.Right - spare)
            x = card.Right + gap;
        else if (card.Left - gap - width >= work.Left + spare)
            x = card.Left - gap - width;
        else
            x = Math.Clamp((card.Left + card.Right) / 2 - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
        int y = card.Top;
        if (y + height > work.Bottom - spare)
            y = work.Bottom - spare - height;
        y = Math.Max(work.Top, y);
        return new PointInt32(x, y);
    }

    private nint Procedure(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == Win32.WM_ERASEBKGND)
        {
            Win32.EraseTransparent(hwnd, wParam);
            return 1;
        }
        if (msg == Win32.WM_NCCALCSIZE && wParam != 0)
            return 0;
        return Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        // Leaving for another program closes the panel; our own menus, dialogs and Shell windows, and the panel's first moments, do not (see PanelFocusPolicy).
        if (_shown && args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
            _guard.OnDeactivated();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_destroying)
        {
            args.Handled = true; // Alt+F4 and the like only hide the panel
            Close();
            return;
        }
        Win32.RemoveWindowSubclass(_hwnd, _subclass!, 7);
        _glass?.Dispose();
        _glass = null;
        _window = null;
    }

    /// <summary>Hides the window, as if it were closed; the next <see cref="Open"/> shows it again, reset.</summary>
    public void Close()
    {
        if (!_shown || _window is null)
            return;
        _shown = false;
        try
        {
            _flyout?.Hide();
            // The window stays up while it fades out (A1) but takes no clicks; the hide and the clean-up come when it has gone.
            _fader!.SetClickThrough(true);
            _hideAfterClose = HideAfterClose;
            RunPanelMotion(opening: false);
        }
        catch (Exception ex)
        {
            Log.Warn("Closing the Big Folder panel failed", ex);
        }
        Closed?.Invoke();
    }

    /// <summary>The close has faded out: the window goes, and the tiles with it (the next open builds fresh ones).</summary>
    private void FinishClose()
    {
        var finish = _hideAfterClose;
        _hideAfterClose = null;
        if (finish is null || _shown || _window is null)
            return; // reopened meanwhile
        try
        {
            finish();
        }
        catch (Exception ex)
        {
            Log.Warn("Hiding the Big Folder panel failed", ex);
        }
    }

    private void HideAfterClose()
    {
        _window!.AppWindow.Hide();
        DiscardContent();
        if (_glass?.Visual is { } frost && _frame is not null)
            RestPose(frost, _frame);
        _fader?.Set(0);
    }

    private void DiscardContent()
    {
        _sweeper?.Stop();
        _content.Children.Clear();
        _cells.Clear();
        _byIndex.Clear();
        _leaving.Clear();
        _arrivals.Clear();
        _knownKeys = null;
        _animateChanges = false;
        _noMatchesCollapseAt = DateTime.MaxValue;
        _tiles = null;
    }

    /// <summary>The card is going away: closes the window for good.</summary>
    public void Destroy()
    {
        bool wasShown = _shown;
        _shown = false;
        _destroying = true;
        _hideAfterClose = null;
        _guard.Stop();
        _fader?.Dispose();
        _fader = null;
        if (_window is not null)
        {
            try
            {
                _window.Close();
            }
            catch (Exception ex)
            {
                Log.Warn("Closing the Big Folder panel failed", ex);
            }
        }
        if (wasShown)
            Closed?.Invoke();
    }

    // ----- the parts -----

    private void BuildContent()
    {
        _canvas.Width = PanelWidth;
        _canvas.Height = _height;

        // Title row: the name and the count, then the sort button at the right.
        Canvas.SetLeft(_title, 20);
        Canvas.SetLeft(_count, 20);
        _canvas.Children.Add(_title);
        _canvas.Children.Add(_count);
        _rename.Visibility = Visibility.Collapsed;
        StyleBareTextBox(_rename);
        _rename.FontSize = 17;
        _rename.FontFamily = DisplayFont;
        _rename.FontWeight = FontWeights.SemiBold;
        _rename.Width = 260;
        Canvas.SetLeft(_rename, 20);
        Canvas.SetTop(_rename, 14);
        _rename.KeyDown += OnRenameKey;
        _rename.LostFocus += (_, _) =>
        {
            if (!_menuOpen)
                CommitRename();
        };
        _canvas.Children.Add(_rename);

        _sortButton.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { _sortText, new FontIcon { Glyph = "", FontSize = 8, FontFamily = IconFont } } };
        // A3: the sort button's plate fades in (Quick) and out (Exit).
        _sortButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _sortButton.PointerEntered += (_, _) =>
        {
            _sortButton.BackgroundTransition = MotionSettings.Enabled ? new BrushTransition { Duration = GlassMotion.Quick } : null;
            _sortButton.Background = Palette.Hover;
        };
        _sortButton.PointerExited += (_, _) =>
        {
            _sortButton.BackgroundTransition = MotionSettings.Enabled ? new BrushTransition { Duration = GlassMotion.Exit } : null;
            _sortButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        };
        _sortButton.Tapped += (_, _) => ShowSortMenu();
        _canvas.Children.Add(_sortButton);

        // Search field.
        var searchGrid = new Grid();
        searchGrid.Children.Add(new FontIcon { Glyph = "", FontSize = 12, FontFamily = IconFont, Margin = new Thickness(11, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        StyleBareTextBox(_search);
        HideDeleteButton(_search); // our own clear button sits at the right
        _search.FontSize = 13;
        _search.Margin = new Thickness(31, 0, 36, 0);
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) => OnSearchChanged();
        _search.KeyDown += OnSearchKey;
        // A3: the field's background goes between Hover and Pressed over 120 ms (the one odd value, for a field).
        _search.GotFocus += (_, _) =>
        {
            _searchBox.BackgroundTransition = MotionSettings.Enabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) } : null;
            _searchBox.Background = Palette.Pressed;
        };
        _search.LostFocus += (_, _) =>
        {
            _searchBox.BackgroundTransition = MotionSettings.Enabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) } : null;
            _searchBox.Background = Palette.Hover;
        };
        searchGrid.Children.Add(_search);
        _clear.Child = new FontIcon { Glyph = "", FontSize = 10, FontFamily = IconFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _clear.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _clear.HorizontalAlignment = HorizontalAlignment.Right;
        _clear.Margin = new Thickness(0, 0, 4, 0);
        _clear.Tapped += (_, _) =>
        {
            _search.Text = "";
            _search.Focus(FocusState.Programmatic);
        };
        searchGrid.Children.Add(_clear);
        _searchBox.Child = searchGrid;
        Canvas.SetLeft(_searchBox, 20);
        Canvas.SetTop(_searchBox, 52);
        _canvas.Children.Add(_searchBox);

        // The grid of items, scrolled by WinUI's own scroll viewer (overlay bar, hides itself).
        _content.Width = PanelWidth;
        _content.HorizontalAlignment = HorizontalAlignment.Left;
        _content.VerticalAlignment = VerticalAlignment.Top; // a short result list sits at the top of the viewport, not in its middle
        _scroll.Content = _content;
        _scroll.Width = PanelWidth;
        _scroll.Height = _height - ViewportTop - 44;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollMode = ScrollMode.Disabled;
        _scroll.ViewChanged += (_, _) => UpdateVisible();
        Canvas.SetTop(_scroll, ViewportTop);
        _canvas.Children.Add(_scroll);

        Canvas.SetLeft(_noMatches, 0);
        Canvas.SetTop(_noMatches, ViewportTop + 14);
        _canvas.Children.Add(_noMatches);

        Canvas.SetLeft(_path, 20);
        Canvas.SetTop(_path, _height - 36 + 1);
        _canvas.Children.Add(_path);
        Canvas.SetTop(_selectedText, _height - 36 + 1);
        _canvas.Children.Add(_selectedText);

        // Typing anywhere searches; keys that move or act on the grid are handled at the root (see CreateWindow): a click on an item
        // leaves no element with the focus, and the keys then go straight to the root.
        _canvas.Children.Add(_gridFocus);
        _canvas.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _canvas.PointerPressed += (_, e) =>
        {
            if (e.OriginalSource is not TextBox)
                _gridFocus.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>A text box with no border, no background and no focus line: the field's own plate does the drawing.</summary>
    private static void StyleBareTextBox(TextBox box)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (string key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
        })
        {
            box.Resources[key] = transparent;
        }
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        box.BorderThickness = new Thickness(0);
        box.Background = transparent;
        box.Padding = new Thickness(0);
        box.MinHeight = 0;
        box.FontFamily = TextFont;
    }

    /// <summary>Hides the TextBox's own delete button (its template part "DeleteButton"), for the fields that bring their own.</summary>
    private static void HideDeleteButton(TextBox box)
    {
        box.Loaded += (_, _) =>
        {
            box.ApplyTemplate(); // a box made while the panel is already up has no template parts until now
            if (FindPart(box, "DeleteButton") is not { } button)
                return;
            button.Visibility = Visibility.Collapsed;
            button.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (sender, _) =>
            {
                if (sender is UIElement element && element.Visibility != Visibility.Collapsed)
                    element.Visibility = Visibility.Collapsed; // the template's states show it again while there is text
            });
        };
    }

    private static UIElement? FindPart(DependencyObject root, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var childName } element && childName == name)
                return element;
            if (FindPart(child, name) is { } found)
                return found;
        }
        return null;
    }

    // ----- the content: filter, selection, painting -----

    /// <summary>Design QA: the longest single <see cref="Refresh"/> (the filter, the cells, the motion) since it was last read, in ms.</summary>
    internal static double LongestRefreshMs { get; set; }

    public void Refresh()
    {
        if (!_shown || _window is null || _root is null)
            return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        RefreshNow();
        LongestRefreshMs = Math.Max(LongestRefreshMs, watch.Elapsed.TotalMilliseconds);
    }

    private void RefreshNow()
    {
        string filter = _search.Text.Trim();
        var rename = _widget.Aftermath.PendingRename;
        var query = new Search.NameQuery(filter);
        _view = Model.Items.Where(i => Search.NameSearch.Matches(i.DisplayName, query)).ToList();
        // A2/A4: what is new in this list (it enters), and which cell an item brings along (a rename keeps its cell: it is a move).
        var keys = new HashSet<string>(_view.Select(i => i.FileName), StringComparer.OrdinalIgnoreCase);
        var previous = _knownKeys;
        _knownKeys = keys;
        _arrivals.Clear();
        if (previous is not null)
        {
            foreach (string key in keys.Where(k => !previous.Contains(k)))
                _arrivals.Add(key);
        }
        if (rename is { } renamed && !keys.Contains(renamed.From) && keys.Contains(renamed.To) && _cells.Remove(renamed.From, out var renamedCell))
        {
            renamedCell.Key = renamed.To;
            _cells[renamed.To] = renamedCell;
            _arrivals.Remove(renamed.To);
        }
        _selected.IntersectWith(_view.Select(i => i.FileName));
        if (_focus >= _view.Count)
            _focus = _view.Count - 1;
        // The result of the panel's own work: the renamed item, or the item after a removed one, gets the selection.
        int pick = _widget.Aftermath.Resolve(_view.Select(i => i.FileName).ToList(), DateTime.UtcNow) is { } name
            ? _view.FindIndex(i => i.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)) : -1;
        if (pick >= 0)
        {
            _selected.Clear();
            _selected.Add(_view[pick].FileName);
            _focus = _anchor = pick;
        }
        Paint();
        if (pick >= 0)
            ScrollTo(pick);
    }

    public void ApplyLanguage()
    {
        if (_shown)
            Paint();
    }

    public void PrepareForCapture(FolderCapture state)
    {
        _search.Text = state.Search; // TextChanged filters the list
        _search.Select(state.Search.Length, 0); // the caret after the text, as when it is typed
        Refresh();
        if (state.Rename && _view.Count > 0)
        {
            BeginRename(_view[0].FileName);
            return;
        }
        for (int i = 0; i < Math.Min(state.Select, _view.Count); i++)
            _selected.Add(_view[i].FileName);
        _hover = state.Hover;
        if (state.Focus >= 0)
        {
            _focus = state.Focus;
            _keyboardFocus = true;
        }
        Paint();
    }

    private void RepaintSoon() => _root?.DispatcherQueue.TryEnqueue(() =>
    {
        if (!_shown)
            return;
        _tileRevision++; // a picture arrived: the tiles that waited for it are made again
        UpdateVisible(rebuild: true);
    });

    private Func<string, double> Measurer(double size, FontWeight weight, FontFamily? family = null)
    {
        var block = new TextBlock { FontFamily = family ?? TextFont, FontSize = size, FontWeight = weight };
        return text =>
        {
            block.Text = text;
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize.Width;
        };
    }

    private void Paint()
    {
        var palette = Palette;
        var glass = palette;
        _title.Foreground = glass.Primary;
        _count.Foreground = glass.Secondary;
        _path.Foreground = glass.Secondary;
        _selectedText.Foreground = glass.Secondary;
        _noMatches.Foreground = glass.Secondary;
        _sortText.Foreground = glass.Secondary;
        ((FontIcon)((StackPanel)_sortButton.Child).Children[1]).Foreground = glass.Secondary;
        _searchBox.Background = _search.FocusState == FocusState.Unfocused ? glass.Hover : glass.Pressed;
        _search.Foreground = glass.Primary;
        _rename.Foreground = glass.Primary;
        _search.PlaceholderText = Strings.FolderSearch;
        ((FontIcon)((Grid)_searchBox.Child).Children[0]).Foreground = glass.Secondary;
        ((FontIcon)_clear.Child!).Foreground = glass.Secondary;

        // Title: the name (middle-shortened to 260) and "23 items" on the same baseline.
        string name = NameShortener.OneLine(Model.Name, 260, Measurer(17, FontWeights.SemiBold, DisplayFont));
        _title.Text = name;
        _title.Visibility = _renamingFolder ? Visibility.Collapsed : Visibility.Visible;
        _title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetTop(_title, 16 + (28 - _title.DesiredSize.Height) / 2);
        _count.Text = Strings.FolderItemCount(Model.Items.Count);
        _count.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_count, 20 + _title.DesiredSize.Width + 8);
        Canvas.SetTop(_count, 16 + (28 - _count.DesiredSize.Height) / 2 + 1);
        _count.Visibility = _renamingFolder ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_title, Model.Name);

        _sortText.Text = Strings.FolderSortButton(Model.Sort);
        _sortButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_sortButton, PanelWidth - 12 - _sortButton.DesiredSize.Width);
        Canvas.SetTop(_sortButton, 16 + 2);

        // Footer: the path (at most 300 wide) and, while something is selected, how many.
        _path.Text = NameShortener.Path(Model.Path, _selected.Count > 0 ? 260 : 300, Measurer(11, FontWeights.Normal));
        _selectedText.Text = Strings.FolderSelected(_selected.Count);
        _selectedText.Visibility = _selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _selectedText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_selectedText, PanelWidth - 20 - _selectedText.DesiredSize.Width);

        _noMatches.Text = Strings.FolderNoMatches;
        SetNoMatches(_view.Count == 0 && _search.Text.Trim().Length > 0);
        _clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateVisible(rebuild: true);
    }

    private int RowCount => (_view.Count + Columns - 1) / Columns;

    /// <summary>
    /// One item's cell (A2, A3, A4). It is made once per item, keyed by file name, and keeps its place in the tree and all its parts for
    /// its whole life: hover, press and selection change what the parts show, never the parts themselves. That is what lets a cell
    /// slide to a new place, fade in or out, and scale on a press with Composition animations on its visuals.
    /// </summary>
    private sealed class CellView
    {
        public Grid Root = null!;
        public Rectangle Hover = null!, Selected = null!, Ring = null!;
        public Grid TileHost = null!;
        public Canvas NameLayer = null!;
        public string Key = "";
        public int Index = -1;
        public double X, Y;
        public bool HoverOn, Dimmed;
        public string TileSignature = "", NameSignature = "";
        public GlassMotion.SlideState Slide = new();
        public DateTime RestoreAt = DateTime.MaxValue;
    }

    /// <summary>The point cells scale about: the centre of the tile (the tile sits at 16, 6 and is 48 square).</summary>
    private static readonly Vector2 TileCentre = new(40, 30);

    private double CellX(int index) => 20 + index % Columns * CellWidth;

    private double CellY(int index) => index / Columns * CellHeight;

    private bool InView(double y) => y + CellHeight > _scroll.VerticalOffset && y < _scroll.VerticalOffset + _scroll.Height;

    private void UpdateVisible(bool rebuild = false)
    {
        if (!_shown || _window is null)
            return;
        double viewport = _scroll.Height;
        _content.Height = RowCount * CellHeight + 8;
        int firstRow = Math.Max(0, (int)Math.Floor(_scroll.VerticalOffset / CellHeight) - 1);
        int lastRow = Math.Min(RowCount - 1, (int)Math.Ceiling((_scroll.VerticalOffset + viewport) / CellHeight) + 1);
        var wanted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = row * Columns + column;
                if (index < _view.Count)
                    wanted[_view[index].FileName] = index;
            }
        }

        // Motion only for a change of the list while the panel is up; a scroll just builds the cells that come into the rows we keep.
        bool animate = _animateChanges && rebuild && MotionSettings.Enabled;
        var leavers = new List<CellView>();
        foreach (var (key, cell) in _cells.ToList())
        {
            if (wanted.ContainsKey(key))
                continue;
            _cells.Remove(key);
            // Gone from the list (not just out of the rows we build), and it was in view: it fades out where it stands.
            bool gone = _knownKeys is not null && !_knownKeys.Contains(key);
            if (animate && gone && InView(cell.Y))
                leavers.Add(cell);
            else
                _content.Children.Remove(cell.Root);
        }

        _byIndex.Clear();
        var enterDelay = leavers.Count > 0 ? TimeSpan.FromMilliseconds(80) : TimeSpan.Zero;
        foreach (var (key, index) in wanted)
        {
            double x = CellX(index), y = CellY(index);
            bool fresh = !_cells.TryGetValue(key, out var cell);
            if (fresh)
            {
                cell = BuildCell(key);
                cell.X = x;
                cell.Y = y;
                Canvas.SetLeft(cell.Root, x);
                Canvas.SetTop(cell.Root, y);
                _cells[key] = cell;
                _content.Children.Add(cell.Root);
            }
            cell!.Index = index;
            _byIndex[index] = cell;
            if (!fresh && (cell.X != x || cell.Y != y))
                MoveCell(cell, x, y, animate);
            if (fresh || rebuild)
                FillCell(cell, index);
            if (fresh && animate && _arrivals.Contains(key) && InView(y))
                EnterCell(cell, enterDelay);
        }
        foreach (var cell in leavers)
            LeaveCell(cell);
        if (rebuild)
            _arrivals.Clear();
    }

    /// <summary>A4: a cell whose item changed place glides from the old place to the new one (Move). Only cells in view animate.</summary>
    private void MoveCell(CellView cell, double x, double y, bool animate)
    {
        var delta = new Vector2((float)(cell.X - x), (float)(cell.Y - y));
        bool visible = InView(cell.Y) || InView(y);
        cell.X = x;
        cell.Y = y;
        Canvas.SetLeft(cell.Root, x);
        Canvas.SetTop(cell.Root, y);
        GlassMotion.Slide(cell.Root, cell.Slide, delta, animate && visible);
    }

    /// <summary>A2b/A4: a new item's cell enters: opacity 0 → 1 and scale 85 % → 100 % about its tile (Enter). It is in place from the start.</summary>
    private static void EnterCell(CellView cell, TimeSpan delay) => GlassMotion.EnterItem(cell.Root, TileCentre, delay);

    /// <summary>A2c/A4: a leaving item's cell goes to opacity 0 and scale 85 % (Exit), takes no input, and is removed when it has gone.</summary>
    private void LeaveCell(CellView cell)
    {
        GlassMotion.LeaveItem(cell.Root, TileCentre);
        _leaving.Add((cell, DateTime.UtcNow + GlassMotion.LeaveDuration));
        StartSweeper();
    }

    /// <summary>One timer that runs only while something waits: leavers to remove, dimmed cells to bring back, the "No matches" line to hide.</summary>
    private void StartSweeper()
    {
        if (_sweeper is not { Active: true })
            _sweeper = FrameClock.Every(60, _ => Sweep());
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            if (_leaving[i].Until > now)
                continue;
            _content.Children.Remove(_leaving[i].Cell.Root);
            _leaving.RemoveAt(i);
        }
        foreach (var cell in _cells.Values)
        {
            if (cell.Dimmed && cell.RestoreAt <= now)
                Undim(cell);
        }
        if (_noMatchesCollapseAt <= now)
        {
            _noMatchesCollapseAt = DateTime.MaxValue;
            if (!_noMatchesShown)
                _noMatches.Visibility = Visibility.Collapsed;
        }
        if (_leaving.Count == 0 && _noMatchesCollapseAt == DateTime.MaxValue && !_cells.Values.Any(c => c.Dimmed))
            _sweeper?.Stop();
    }

    private static void Undim(CellView cell)
    {
        cell.Dimmed = false;
        cell.RestoreAt = DateTime.MaxValue;
        GlassMotion.FadeTo(cell.Root, 1, GlassMotion.Exit, GlassEase.In);
    }

    /// <summary>The "No matches" line fades in (Enter) and out (Exit).</summary>
    private void SetNoMatches(bool show)
    {
        if (show == _noMatchesShown)
            return;
        _noMatchesShown = show;
        if (!_animateChanges || !MotionSettings.Enabled)
        {
            _noMatches.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            GlassMotion.Rest(_noMatches);
            return;
        }
        if (show)
        {
            _noMatchesCollapseAt = DateTime.MaxValue;
            if (_noMatches.Visibility != Visibility.Visible)
            {
                _noMatches.Visibility = Visibility.Visible;
                GlassMotion.Preset(_noMatches, 0, 1, Vector2.Zero);
            }
            GlassMotion.FadeTo(_noMatches, 1, GlassMotion.Enter, GlassEase.Out);
        }
        else
        {
            GlassMotion.FadeTo(_noMatches, 0, GlassMotion.Exit, GlassEase.In);
            _noMatchesCollapseAt = DateTime.UtcNow + GlassMotion.Exit + TimeSpan.FromMilliseconds(40);
            StartSweeper();
        }
    }

    private void RestyleCell(int index)
    {
        if (_byIndex.TryGetValue(index, out var cell) && index < _view.Count && _renaming != _view[index].FileName)
            FillCell(cell, index);
    }

    private void RestyleAll()
    {
        foreach (int index in _byIndex.Keys.ToList())
            RestyleCell(index);
    }

    /// <summary>
    /// A cell is made once per item and keeps its place in the tree (and so the pointer's target); <see cref="FillCell"/> changes what its
    /// parts show. Replacing the element under the pointer on every hover and press made the real mouse miss its clicks.
    /// </summary>
    private CellView BuildCell(string key)
    {
        var cell = new CellView { Key = key };
        cell.Root = new Grid { Width = CellWidth, Height = CellHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CanDrag = true, Tag = cell };
        var layer = new Canvas { Width = CellWidth, Height = CellHeight, IsHitTestVisible = false };
        cell.Hover = Plate(2, 0, 76, 90, 12, Palette.Hover);
        GlassMotion.VisualOf(cell.Hover).Opacity = 0; // hidden and shown by a fade on its visual (an element whose Opacity property is 0 is not drawn at all)
        cell.Selected = Plate(12, 2, 56, 56, 14, Palette.Pressed);
        cell.Selected.Visibility = Visibility.Collapsed;
        cell.Ring = new Rectangle { Width = 76, Height = 90, RadiusX = 12, RadiusY = 12, StrokeThickness = 2, Margin = new Thickness(2, 0, 0, 0), Visibility = Visibility.Collapsed };
        cell.TileHost = new Grid { Width = 48, Height = 48 };
        Canvas.SetLeft(cell.TileHost, 16);
        Canvas.SetTop(cell.TileHost, 6);
        cell.NameLayer = new Canvas { Width = CellWidth, Height = CellHeight };
        layer.Children.Add(cell.Hover);
        layer.Children.Add(cell.Selected);
        layer.Children.Add(cell.Ring);
        layer.Children.Add(cell.TileHost);
        layer.Children.Add(cell.NameLayer);
        cell.Root.Children.Add(layer);

        cell.Root.PointerEntered += (_, _) =>
        {
            int old = _hover;
            _hover = cell.Index;
            RestyleCell(old);
            RestyleCell(cell.Index);
        };
        cell.Root.PointerExited += (_, _) =>
        {
            if (_hover == cell.Index)
            {
                _hover = -1;
                RestyleCell(cell.Index);
            }
            ReleaseTile(cell);
        };
        cell.Root.PointerCanceled += (_, _) => ReleaseTile(cell);
        cell.Root.PointerCaptureLost += (_, _) => ReleaseTile(cell);
        // CanDrag elements mark the press as handled for the ones that listen the usual way.
        cell.Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCellPressed), handledEventsToo: true);
        cell.Root.PointerReleased += OnCellReleased;
        cell.Root.DoubleTapped += (_, e) =>
        {
            OpenSelection();
            e.Handled = true;
        };
        cell.Root.RightTapped += OnCellRightTapped;
        cell.Root.DragStarting += OnDragStarting;
        cell.Root.DropCompleted += OnCellDropCompleted;
        return cell;
    }

    /// <summary>A3: the tile goes down to 94 % on a press (Press), and comes back on release, on leaving, or when the press turns into a drag (Release).</summary>
    private static void PressTile(CellView cell) => GlassMotion.ScaleTo(cell.TileHost, GlassMotion.PressScale, GlassMotion.Press, GlassEase.Out, new Vector2(24, 24));

    private static void ReleaseTile(CellView cell) => GlassMotion.ScaleTo(cell.TileHost, 1, GlassMotion.Release, GlassEase.Out, new Vector2(24, 24));

    private void FillCell(CellView cell, int index)
    {
        var palette = Palette;
        var item = _view[index];
        bool selected = _selected.Contains(item.FileName);
        bool hover = index == _hover, pressed = index == _pressed;

        // The plates: the hover plate fades in (Quick) and out (Exit); pressed turns it to the pressed colour at once.
        cell.Hover.Fill = pressed ? palette.Pressed : palette.Hover;
        bool plateOn = hover || pressed;
        if (plateOn != cell.HoverOn)
        {
            cell.HoverOn = plateOn;
            GlassMotion.FadeTo(cell.Hover, plateOn ? 1 : 0, plateOn ? GlassMotion.Quick : GlassMotion.Exit, plateOn ? GlassEase.Out : GlassEase.In);
        }
        cell.Selected.Fill = palette.Pressed;
        cell.Selected.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        cell.Ring.Stroke = palette.Select;
        cell.Ring.Visibility = _keyboardFocus && index == _focus ? Visibility.Visible : Visibility.Collapsed;

        // The tile: made again only when the item or the colours changed, or a picture arrived.
        string tileSignature = $"{item.FileName}|{item.TargetMissing}|{Dark}|{_tileRevision}";
        if (cell.TileSignature != tileSignature)
        {
            cell.TileSignature = tileSignature;
            cell.TileHost.Children.Clear();
            var tile = _tiles!.Tile(palette, item, 48);
            if (item.TargetMissing)
                tile.Opacity = 0.4;
            cell.TileHost.Children.Add(tile);
        }

        // The name, and the box while it is being renamed.
        bool renaming = _renaming == item.FileName;
        string nameSignature = $"{item.DisplayName}|{selected}|{item.TargetMissing}|{renaming}|{Dark}";
        bool boxShown = renaming && _itemBox is { } box && cell.NameLayer.Children.Contains(box);
        if (PanelRename.NameNeedsBuilding(renaming, boxShown, cell.NameSignature, nameSignature))
        {
            cell.NameSignature = nameSignature;
            BuildName(cell, index, item, selected, renaming);
        }
        // The cell being renamed draws over its neighbours: its box may be wider than the cell.
        Canvas.SetZIndex(cell.Root, renaming ? 1 : 0);
        AutomationProperties.SetName(cell.Root, item.DisplayName);
    }

    private void BuildName(CellView cell, int index, FolderItem item, bool selected, bool renaming)
    {
        var palette = Palette;
        cell.NameLayer.Children.Clear();
        ToolTipService.SetToolTip(cell.Root, null);

        // The name: two lines of Text 11 (line height 14) in a 76 × 28 box at (x + 2, y + 58), centred (Glass HANDOFF 3.3, Phase 8).
        const double width = 76;
        var measure = Measurer(11, FontWeights.Normal);
        var boxMeasure = Measurer(12, FontWeights.Normal); // the rename box keeps Text 12
        var name = new StackPanel { Width = 76, Height = 28, IsHitTestVisible = false };
        if (renaming)
        {
            _rename.Visibility = Visibility.Collapsed;
            // The box is as wide as the name (Text 12) plus 16, 80 to 160, centred on the cell and kept inside the panel's content (x 20 to 420).
            double boxWidth = Math.Clamp(boxMeasure(item.DisplayName) + 16, 80, 160);
            double cellLeft = cell.X;
            double boxLeft = Math.Clamp(cellLeft + CellWidth / 2 - boxWidth / 2, 20, PanelWidth - 20 - boxWidth);
            var box = new TextBox { Text = item.DisplayName, Width = boxWidth, FontSize = 12 };
            StyleBareTextBox(box); // no border, no underline, no opaque focus plate: the search field's look at a smaller size
            // The bare style makes the focused and hovered backgrounds transparent, and those win while the box has the focus.
            foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
                box.Resources[key] = palette.Pressed;
            box.Height = 26;
            box.Background = palette.Pressed;
            box.Foreground = palette.Primary;
            box.CornerRadius = new CornerRadius(6);
            box.Padding = new Thickness(4, 3, 4, 3);
            box.SelectionHighlightColor = palette.Select;
            HideDeleteButton(box);
            box.KeyDown += OnItemRenameKey;
            box.LostFocus += (_, _) =>
            {
                if (!_menuOpen)
                    CommitItemRename(box.Text);
            };
            _itemBox = box;
            box.Loaded += (_, _) =>
            {
                box.Focus(FocusState.Programmatic);
                // With the extension shown, only the base name is selected, as in Explorer.
                int dot = item.DisplayName.LastIndexOf('.');
                if (!item.IsFolder && item.DisplayName == item.FileName && dot > 0)
                    box.Select(0, dot);
                else
                    box.SelectAll();
            };
            box.Tag = item;
            Canvas.SetLeft(box, boxLeft - cellLeft);
            Canvas.SetTop(box, 58);
            cell.NameLayer.Children.Add(box);
            return;
        }
        var (first, second) = NameShortener.TwoLines(item.DisplayName, width, measure);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (string line in second.Length == 0 ? [first] : new[] { first, second })
        {
            var block = new TextBlock { Text = line, FontFamily = TextFont, FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = TextAlignment.Center, Foreground = item.TargetMissing ? palette.Inactive : palette.Primary };
            UIElement lineElement = block;
            if (selected)
            {
                block.Foreground = palette.OnSelect;
                lineElement = new Border { Background = palette.Select, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), Height = 14, HorizontalAlignment = HorizontalAlignment.Center, Child = block };
            }
            name.Children.Add(lineElement);
        }
        // 80 wide, like the cell: a selected name's pill is its line plus 8 and may be as wide as the cell.
        name.Width = 80;
        Canvas.SetLeft(name, 0);
        Canvas.SetTop(name, 58);
        cell.NameLayer.Children.Add(name);
        if (first != item.DisplayName || second.Length > 0 && first + second != item.DisplayName)
            ToolTipService.SetToolTip(cell.Root, item.DisplayName);
    }

    private static Rectangle Plate(double x, double y, double width, double height, double radius, Brush fill)
    {
        var plate = new Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius, Fill = fill };
        Canvas.SetLeft(plate, x);
        Canvas.SetTop(plate, y);
        return plate;
    }

    // ----- selecting, opening, dragging -----

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: CellView cell } ? cell.Index : -1;

    private static CellView? CellOf(object sender) => sender is FrameworkElement { Tag: CellView cell } ? cell : null;

    private void OnCellPressed(object sender, PointerRoutedEventArgs e)
    {
        int index = IndexOf(sender);
        if (index < 0 || !e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;
        if (CellOf(sender) is { } pressedCell)
            PressTile(pressedCell);
        _gridFocus.Focus(FocusState.Pointer);
        _keyboardFocus = false;
        var modifiers = e.KeyModifiers;
        bool ctrl = modifiers.HasFlag(VirtualKeyModifiers.Control), shift = modifiers.HasFlag(VirtualKeyModifiers.Shift);
        string name = _view[index].FileName;
        _collapseOnRelease = false;
        if (shift && _anchor >= 0)
        {
            SelectRange(_anchor, index, add: ctrl);
        }
        else if (ctrl)
        {
            if (!_selected.Remove(name))
                _selected.Add(name);
            _anchor = index;
        }
        else
        {
            // A press on an already selected item keeps the selection (it may be the start of dragging the lot);
            // the release collapses it to this item if nothing was dragged.
            if (!_selected.Contains(name))
            {
                _selected.Clear();
                _selected.Add(name);
            }
            else if (_selected.Count > 1)
            {
                _collapseOnRelease = true;
            }
            _anchor = index;
        }
        _focus = index;
        _pressed = index;
        Paint();
        RestyleAll();
    }

    private void OnCellReleased(object sender, PointerRoutedEventArgs e)
    {
        int index = IndexOf(sender);
        _pressed = -1;
        if (CellOf(sender) is { } releasedCell)
            ReleaseTile(releasedCell);
        if (_collapseOnRelease && index >= 0 && index < _view.Count)
        {
            _selected.Clear();
            _selected.Add(_view[index].FileName);
            _collapseOnRelease = false;
            Paint();
        }
        RestyleAll();
    }

    private void SelectRange(int from, int to, bool add)
    {
        if (!add)
            _selected.Clear();
        for (int i = Math.Min(from, to); i <= Math.Max(from, to) && i < _view.Count; i++)
            _selected.Add(_view[i].FileName);
    }

    private List<FolderItem> SelectedItems() => _view.Where(i => _selected.Contains(i.FileName)).ToList();

    private void OpenSelection()
    {
        foreach (var item in SelectedItems())
            _widget.Open(item);
    }

    private async void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            int index = IndexOf(sender);
            var items = index >= 0 && index < _view.Count && !_selected.Contains(_view[index].FileName) ? [_view[index]] : SelectedItems();
            var storage = new List<IStorageItem>();
            foreach (var item in items)
            {
                try
                {
                    storage.Add(item.IsFolder ? await StorageFolder.GetFolderFromPathAsync(item.Path) : await StorageFile.GetFileFromPathAsync(item.Path));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Dragging '{item.FileName}' out failed", ex);
                }
            }
            if (storage.Count == 0)
            {
                args.Cancel = true;
                return;
            }
            args.Data.SetStorageItems(storage);
            args.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;
            // A2d: the cells being dragged out fade to 40 % (Quick) while the drag lasts, and the press ends.
            if (CellOf(sender) is { } sourceCell)
                ReleaseTile(sourceCell);
            foreach (var item in items)
            {
                if (_cells.TryGetValue(item.FileName, out var dragged))
                {
                    dragged.Dimmed = true;
                    dragged.RestoreAt = DateTime.MaxValue;
                    GlassMotion.FadeTo(dragged.Root, 0.4f, GlassMotion.Quick, GlassEase.Out);
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// A2d: the drag is over. A move that took the items away makes them leave (the list changes); in every other case the cells come back to
    /// full strength (Exit). After a move, any cell that is still there comes back a moment later.
    /// </summary>
    private void OnCellDropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        bool moved = args.DropResult == DataPackageOperation.Move;
        foreach (var cell in _cells.Values.Where(c => c.Dimmed).ToList())
        {
            if (moved)
            {
                cell.RestoreAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
                StartSweeper();
            }
            else
            {
                Undim(cell);
            }
        }
    }

    // ----- menus -----

    private void OnCellRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        int index = IndexOf(sender);
        if (index < 0 || index >= _view.Count)
            return;
        var item = _view[index];
        if (!_selected.Contains(item.FileName))
        {
            _selected.Clear();
            _selected.Add(item.FileName);
            _anchor = _focus = index;
            Paint();
        }
        ShowMenu(_widget.ItemMenu(item, SelectedItems()), (FrameworkElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;
    }

    private void ShowSortMenu()
    {
        var entries = Enum.GetValues<FolderSort>()
            .Select(sort => PixelUi.MenuEntry.Radio(Strings.FolderSortRow(sort), sort == Model.Sort, () => Model.SetSort(sort), group: "panel-sort"))
            .ToList();
        ShowMenu(entries, _sortButton, new Point(0, _sortButton.ActualHeight + 2));
    }

    private void ShowMenu(IReadOnlyList<PixelUi.MenuEntry> entries, FrameworkElement anchor, Point position)
    {
        var flyout = new MenuFlyout { ShouldConstrainToRootBounds = false };
        foreach (var item in FlyoutMenuAdapter.Build(entries))
            flyout.Items.Add(item);
        flyout.Opening += (_, _) => _menuOpen = true;
        flyout.Closed += (_, _) => _root?.DispatcherQueue.TryEnqueue(() =>
        {
            _menuOpen = false;
            _guard.Touch(); // the click that closed the menu must not close the panel as well
            // "Rename" from the menu: the closing menu took the focus back from the box it had just opened.
            if (_renamingFolder)
                _rename.Focus(FocusState.Programmatic);
            else if (_renaming is not null)
                _itemBox?.Focus(FocusState.Programmatic);
            _guard.Reclaim();
        });
        _flyout = flyout;
        flyout.ShowAt(anchor, new FlyoutShowOptions { Position = position });
    }

    // ----- search -----

    private void OnSearchChanged()
    {
        _focus = -1;
        _anchor = -1;
        Refresh();
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void OnCharacter(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || _renaming is not null)
            return;
        char c = e.Character;
        if (char.IsControl(c))
            return;
        e.Handled = true;
        _search.Text += c;
        _search.Focus(FocusState.Programmatic);
        _search.Select(_search.Text.Length, 0);
    }

    private void OnSearchKey(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                EscapePressed();
                e.Handled = true;
                break;
            case VirtualKey.Down when _view.Count > 0:
                _gridFocus.Focus(FocusState.Keyboard);
                MoveFocus(0, extend: false, keepSelection: false, first: true);
                e.Handled = true;
                break;
            case VirtualKey.Enter when _view.Count > 0:
                _selected.Clear();
                _selected.Add(_view[0].FileName);
                OpenSelection();
                e.Handled = true;
                break;
        }
    }

    private void EscapePressed()
    {
        if (_renaming is not null || _renamingFolder)
        {
            CancelRename();
            return;
        }
        if (_search.Text.Length > 0)
        {
            _search.Text = "";
            _search.Focus(FocusState.Programmatic);
            return;
        }
        Close();
    }

    // ----- keyboard -----

    private void OnGridKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        int page = Math.Max(1, (int)(_scroll.Height / CellHeight)) * Columns;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                EscapePressed();
                break;
            case VirtualKey.Left:
                MoveFocus(-1, shift, ctrl);
                break;
            case VirtualKey.Right:
                MoveFocus(1, shift, ctrl);
                break;
            case VirtualKey.Up:
                MoveFocus(-Columns, shift, ctrl);
                break;
            case VirtualKey.Down:
                MoveFocus(Columns, shift, ctrl);
                break;
            case VirtualKey.Home:
                MoveFocus(-_view.Count, shift, ctrl);
                break;
            case VirtualKey.End:
                MoveFocus(_view.Count, shift, ctrl);
                break;
            case VirtualKey.PageUp:
                MoveFocus(-page, shift, ctrl);
                break;
            case VirtualKey.PageDown:
                MoveFocus(page, shift, ctrl);
                break;
            case VirtualKey.Enter:
                if (_selected.Count == 0 && _focus >= 0 && _focus < _view.Count)
                    _selected.Add(_view[_focus].FileName);
                OpenSelection();
                break;
            case VirtualKey.Space when _focus >= 0 && _focus < _view.Count:
                if (!_selected.Remove(_view[_focus].FileName))
                    _selected.Add(_view[_focus].FileName);
                Paint();
                break;
            case VirtualKey.A when ctrl:
                foreach (var item in _view)
                    _selected.Add(item.FileName);
                Paint();
                break;
            case VirtualKey.F2 when _focus >= 0 && _focus < _view.Count:
                BeginRename(_view[_focus].FileName);
                break;
            case VirtualKey.Delete when _selected.Count > 0:
                _widget.RecycleItems(SelectedItems());
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Moves the keyboard focus by <paramref name="delta"/> cells; the selection follows unless Ctrl holds it.</summary>
    private void MoveFocus(int delta, bool extend, bool keepSelection, bool first = false)
    {
        if (_view.Count == 0)
            return;
        int next = first ? 0 : _focus < 0 ? (delta >= 0 ? 0 : _view.Count - 1) : Math.Clamp(_focus + delta, 0, _view.Count - 1);
        _focus = next;
        _keyboardFocus = true;
        if (extend)
        {
            if (_anchor < 0)
                _anchor = next;
            SelectRange(_anchor, next, add: false);
        }
        else if (!keepSelection)
        {
            _selected.Clear();
            _selected.Add(_view[next].FileName);
            _anchor = next;
        }
        ScrollTo(next);
        Paint();
    }

    public void ScrollToItemForBench(int index)
    {
        ScrollTo(index);
        UpdateVisible();
    }

    private void ScrollTo(int index)
    {
        double top = index / Columns * CellHeight, bottom = top + CellHeight;
        if (top < _scroll.VerticalOffset)
            _scroll.ChangeView(null, top, null, disableAnimation: true);
        else if (bottom > _scroll.VerticalOffset + _scroll.Height)
            _scroll.ChangeView(null, bottom - _scroll.Height + 8, null, disableAnimation: true);
    }

    // ----- renaming -----

    public void BeginRename(string fileName)
    {
        if (!_shown || _window is null)
        {
            Refresh();
            return;
        }
        _search.Text = "";
        Refresh();
        int index = _view.FindIndex(i => i.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;
        _renaming = fileName;
        _selected.Clear();
        _selected.Add(fileName);
        _focus = _anchor = index;
        ScrollTo(index);
        Paint();
    }

    public void BeginRenameFolder()
    {
        if (_shown)
            _window?.Activate(); // started from the card's menu while the panel was already open
        _renamingFolder = true;
        _rename.Text = Model.Name;
        _rename.Visibility = Visibility.Visible;
        Paint();
        _rename.Focus(FocusState.Programmatic);
        _rename.SelectAll();
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

    private void OnItemRenameKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox box)
        {
            CommitItemRename(box.Text);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CancelRename()
    {
        _renaming = null;
        _renamingFolder = false;
        _rename.Visibility = Visibility.Collapsed;
        Paint();
        _gridFocus.Focus(FocusState.Programmatic);
    }

    private void CommitRename()
    {
        if (!_renamingFolder)
            return;
        _renamingFolder = false;
        _rename.Visibility = Visibility.Collapsed;
        string typed = _rename.Text;
        string? newPath = FolderActions.RenameFolder(Model.Path, typed, _widget.OwnerHandle, out string? error);
        if (error is null && newPath is not null && !string.Equals(newPath, Model.Path, StringComparison.OrdinalIgnoreCase))
            Model.FolderRenamed(newPath);
        Paint();
        _widget.Report(error); // the warning, if any, and the focus back to the panel
    }

    private void CommitItemRename(string typed)
    {
        if (_renaming is null)
            return;
        var item = _view.FirstOrDefault(i => i.FileName.Equals(_renaming, StringComparison.OrdinalIgnoreCase));
        _renaming = null;
        if (item is not null && typed.Trim() != item.DisplayName)
            _widget.RenameItem(item, typed);
        Paint();
        _guard.Reclaim(); // Enter in the box: it is gone now, and the keys should work on the grid
    }
}
