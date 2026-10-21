using System.Numerics;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
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
    private static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(120);

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Grid> _cells = [];
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
    private DateTime _openedAt;
    private GlassTileProvider? _tiles;
    private TextBox? _itemBox; // the box of the item being renamed

    public GlassFolderPanel(FolderWidget widget) => _widget = widget;

    public event Action? Closed;

    public bool IsOpen => _shown;

    public bool Reusable => true;

    private FolderModel Model => _widget.Model;

    private IWidgetSite Site => _widget.Site;

    private GlassPalette Palette => GlassPalette.For(_root?.ActualTheme ?? ElementTheme.Default);

    private bool Dark => _root?.ActualTheme == ElementTheme.Dark;

    // ----- opening -----

    public void Open()
    {
        if (_shown)
            return;
        _scale = Site.Scale;
        var card = Site.CardRect;
        var center = new Win32.POINT { X = (card.Left + card.Right) / 2, Y = (card.Top + card.Bottom) / 2 };
        var work = Win32.GetWorkArea(center);
        double workHeight = work.Height / _scale;
        _height = workHeight < 516 ? Math.Max(344, workHeight - 16) : 500;

        bool first = _window is null;
        if (first)
            CreateWindow();
        else
            ResetForReopen();

        int width = (int)Math.Round(PanelWidth * _scale), height = (int)Math.Round(_height * _scale);
        var position = Place(card, work, width, height);
        _window!.AppWindow.Move(position);
        _window.AppWindow.Resize(new SizeInt32(width, height));
        _glass!.SetLayout(new Vector2(width, height), (float)(WidgetLayout.CornerRadius * _scale));

        _openedAt = DateTime.UtcNow;
        _shown = true;
        if (!first)
        {
            _root!.OpacityTransition = new ScalarTransition { Duration = FadeTime };
            _window.AppWindow.Show();
        }
        _window.Activate();
        Win32.MakeTransparentToolWindow(_hwnd); // activation may re-apply presenter styles
        if (first)
        {
            _root!.Loaded += (_, _) =>
            {
                Refresh();
                _root.Opacity = 1;
                _search.Focus(FocusState.Programmatic);
            };
            return;
        }
        // The fade starts once the window is up, as on the first open.
        _root!.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_shown)
                return;
            _root.Opacity = 1;
            _search.Focus(FocusState.Programmatic);
        });
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
        _root = new Grid { Opacity = 0, OpacityTransition = new ScalarTransition { Duration = FadeTime } };
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
        // A click outside deactivates the panel, and closes it. Not while one of its own menus or dialogs is up, nor in its first moments.
        if (_shown && args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated && !_menuOpen && !_widget.PanelPinned && (DateTime.UtcNow - _openedAt).TotalMilliseconds > 400)
            Close();
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
            _root!.OpacityTransition = null; // gone at once, not faded out
            _root.Opacity = 0;
            _window.AppWindow.Hide();
            _content.Children.Clear(); // the tiles go; the next open builds fresh ones
            _cells.Clear();
            _tiles = null;
        }
        catch (Exception ex)
        {
            Log.Warn("Closing the Big Folder panel failed", ex);
        }
        Closed?.Invoke();
    }

    /// <summary>The card is going away: closes the window for good.</summary>
    public void Destroy()
    {
        bool wasShown = _shown;
        _shown = false;
        _destroying = true;
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
        _sortButton.PointerEntered += (_, _) => _sortButton.Background = Palette.Hover;
        _sortButton.PointerExited += (_, _) => _sortButton.Background = null;
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
        _search.GotFocus += (_, _) => _searchBox.Background = Palette.Pressed;
        _search.LostFocus += (_, _) => _searchBox.Background = Palette.Hover;
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

    public void Refresh()
    {
        if (!_shown || _window is null || _root is null)
            return;
        string filter = _search.Text.Trim();
        _view = Model.Items.Where(i => filter.Length == 0 || i.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _selected.IntersectWith(_view.Select(i => i.FileName));
        if (_focus >= _view.Count)
            _focus = _view.Count - 1;
        Paint();
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
        if (_shown)
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
        _noMatches.Visibility = _view.Count == 0 && _search.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateVisible(rebuild: true);
    }

    private int RowCount => (_view.Count + Columns - 1) / Columns;

    private void UpdateVisible(bool rebuild = false)
    {
        if (!_shown || _window is null)
            return;
        double viewport = _scroll.Height;
        _content.Height = RowCount * CellHeight + 8;
        if (rebuild)
        {
            foreach (int index in _cells.Keys.ToList())
            {
                if (index < _view.Count)
                {
                    FillCell(_cells[index], index);
                }
                else
                {
                    _content.Children.Remove(_cells[index]);
                    _cells.Remove(index);
                }
            }
        }
        int firstRow = Math.Max(0, (int)Math.Floor(_scroll.VerticalOffset / CellHeight) - 1);
        int lastRow = Math.Min(RowCount - 1, (int)Math.Ceiling((_scroll.VerticalOffset + viewport) / CellHeight) + 1);
        var wanted = new HashSet<int>();
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = row * Columns + column;
                if (index < _view.Count)
                    wanted.Add(index);
            }
        }
        foreach (int gone in _cells.Keys.Where(i => !wanted.Contains(i)).ToList())
        {
            _content.Children.Remove(_cells[gone]);
            _cells.Remove(gone);
        }
        foreach (int index in wanted.Where(i => !_cells.ContainsKey(i)))
        {
            var cell = BuildCell(index);
            _cells[index] = cell;
            _content.Children.Add(cell);
        }
    }

    private void RestyleCell(int index)
    {
        if (_cells.TryGetValue(index, out var cell) && index < _view.Count && _renaming != _view[index].FileName)
            FillCell(cell, index);
    }

    private void RestyleAll()
    {
        foreach (int index in _cells.Keys.ToList())
            RestyleCell(index);
    }

    /// <summary>
    /// A cell is made once per index and keeps its place in the tree (and so the pointer's target); <see cref="FillCell"/> draws and redraws what is in it.
    /// Replacing the element under the pointer on every hover and press made the real mouse miss its clicks.
    /// </summary>
    private Grid BuildCell(int index)
    {
        var cell = new Grid { Width = CellWidth, Height = CellHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CanDrag = true, Tag = index };
        Canvas.SetLeft(cell, 20 + index % Columns * CellWidth);
        Canvas.SetTop(cell, index / Columns * CellHeight);
        FillCell(cell, index);

        cell.PointerEntered += (_, _) =>
        {
            int old = _hover;
            _hover = index;
            RestyleCell(old);
            RestyleCell(index);
        };
        cell.PointerExited += (_, _) =>
        {
            if (_hover == index)
            {
                _hover = -1;
                RestyleCell(index);
            }
        };
        // CanDrag elements mark the press as handled for the ones that listen the usual way.
        cell.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCellPressed), handledEventsToo: true);
        cell.PointerReleased += OnCellReleased;
        cell.DoubleTapped += (_, e) =>
        {
            OpenSelection();
            e.Handled = true;
        };
        cell.RightTapped += OnCellRightTapped;
        cell.DragStarting += OnDragStarting;
        return cell;
    }

    private void FillCell(Grid cell, int index)
    {
        var palette = Palette;
        var item = _view[index];
        bool selected = _selected.Contains(item.FileName);
        bool hover = index == _hover, pressed = index == _pressed;
        cell.Children.Clear();
        ToolTipService.SetToolTip(cell, null);
        var layer = new Canvas { Width = CellWidth, Height = CellHeight, IsHitTestVisible = false };

        if (hover || pressed)
            layer.Children.Add(Plate(2, 0, 76, 90, 12, pressed ? palette.Pressed : palette.Hover));
        if (selected)
            layer.Children.Add(Plate(12, 2, 56, 56, 14, palette.Pressed));
        if (_keyboardFocus && index == _focus)
            layer.Children.Add(new Rectangle { Width = 76, Height = 90, RadiusX = 12, RadiusY = 12, Stroke = palette.Select, StrokeThickness = 2, Margin = new Thickness(2, 0, 0, 0) });

        var tile = _tiles!.Tile(palette, item, 48);
        if (item.TargetMissing)
            tile.Opacity = 0.4;
        Canvas.SetLeft(tile, 16);
        Canvas.SetTop(tile, 6);
        layer.Children.Add(tile);

        // The name: two lines, 72 wide (64 while selected, so the pill fits), centred.
        const double width = 72;
        var measure = Measurer(12, FontWeights.Normal);
        var name = new StackPanel { Width = 72, Height = 30, IsHitTestVisible = false };
        if (_renaming == item.FileName)
        {
            _rename.Visibility = Visibility.Collapsed;
            // The box is as wide as the name (Text 12) plus 16, 80 to 160, centred on the cell and kept inside the panel's content (x 20 to 420).
            double boxWidth = Math.Clamp(measure(item.DisplayName) + 16, 80, 160);
            double cellLeft = 20 + index % Columns * CellWidth;
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
            layer.Children.Add(box);
        }
        else
        {
            var (first, second) = NameShortener.TwoLines(item.DisplayName, width, measure);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (string line in second.Length == 0 ? [first] : new[] { first, second })
            {
                var block = new TextBlock { Text = line, FontFamily = TextFont, FontSize = 12, LineHeight = 15, TextAlignment = TextAlignment.Center, Foreground = item.TargetMissing ? palette.Secondary : palette.Primary };
                UIElement lineElement = block;
                if (selected)
                {
                    block.Foreground = palette.OnSelect;
                    lineElement = new Border { Background = palette.Select, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), Height = 15, HorizontalAlignment = HorizontalAlignment.Center, Child = block };
                }
                name.Children.Add(lineElement);
            }
            // 80 wide, like the cell: a selected name's pill is its line plus 8 and may be as wide as the cell.
            name.Width = 80;
            Canvas.SetLeft(name, 0);
            Canvas.SetTop(name, 58);
            layer.Children.Add(name);
            if (first != item.DisplayName || second.Length > 0 && first + second != item.DisplayName)
                ToolTipService.SetToolTip(cell, item.DisplayName);
        }
        cell.Children.Add(layer);
        // The cell being renamed draws over its neighbours: its box may be wider than the cell. Cells are refilled, so this resets itself.
        Canvas.SetZIndex(cell, _renaming == item.FileName ? 1 : 0);
        AutomationProperties.SetName(cell, item.DisplayName);
    }

    private static Rectangle Plate(double x, double y, double width, double height, double radius, Brush fill)
    {
        var plate = new Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius, Fill = fill };
        Canvas.SetLeft(plate, x);
        Canvas.SetTop(plate, y);
        return plate;
    }

    // ----- selecting, opening, dragging -----

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    private void OnCellPressed(object sender, PointerRoutedEventArgs e)
    {
        int index = IndexOf(sender);
        if (index < 0 || !e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;
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
        }
        finally
        {
            deferral.Complete();
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
            _openedAt = DateTime.UtcNow; // the click that closed the menu must not close the panel as well
            // "Rename" from the menu: the closing menu took the focus back from the box it had just opened.
            if (_renamingFolder)
                _rename.Focus(FocusState.Programmatic);
            else if (_renaming is not null)
                _itemBox?.Focus(FocusState.Programmatic);
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
                _widget.Report(FolderActions.Recycle(Model, SelectedItems(), Site.WindowHandle));
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
        string? newPath = FolderActions.RenameFolder(Model.Path, typed, Site.WindowHandle, out string? error);
        if (error is not null)
            Site.ShowWarning(Strings.FolderOperationFailed(error));
        else if (newPath is not null && !string.Equals(newPath, Model.Path, StringComparison.OrdinalIgnoreCase))
            Model.FolderRenamed(newPath);
        Paint();
    }

    private void CommitItemRename(string typed)
    {
        if (_renaming is null)
            return;
        var item = _view.FirstOrDefault(i => i.FileName.Equals(_renaming, StringComparison.OrdinalIgnoreCase));
        _renaming = null;
        if (item is not null && typed.Trim() != item.DisplayName)
            _widget.Report(FolderActions.Rename(Model, item, typed, Site.WindowHandle));
        Paint();
    }
}
