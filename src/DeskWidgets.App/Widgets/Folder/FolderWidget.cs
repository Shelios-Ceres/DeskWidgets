using DeskWidgets.Core.Diagnostics;
using DeskWidgets.PixelUi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DeskWidgets.Widgets.Folder;

internal sealed class FolderWidgetFactory : IWidgetFactory
{
    public string TypeId => "folder";

    public string DisplayName => Strings.FolderWidget;

    public string Glyph => "";

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large];

    public WidgetSize DefaultSize => WidgetSize.Medium;

    public IWidget Create(WidgetContext context) => new FolderWidget(context);
}

/// <summary>
/// Design QA: the state a capture of the panel shows. <c>--folder-panel-search=text</c>, <c>-select=n</c> (the first n items),
/// <c>-rename</c> (the first item), <c>-hover=n</c> and <c>-focus=n</c> (the keyboard's focus ring), and for the cards
/// <c>--folder-card-drop=Move|Copy|Shortcut|Mixed</c>.
/// </summary>
internal sealed record FolderCapture(string Search = "", int Select = 0, bool Rename = false, int Hover = -1, int Focus = -1, DropVerb? Drop = null);

/// <summary>What the panel (either theme's) offers the widget.</summary>
internal interface IFolderPanel
{
    bool IsOpen { get; }

    /// <summary>Raised once, when the panel has closed.</summary>
    event Action? Closed;

    /// <summary>Shows the panel beside the card.</summary>
    void Open();

    /// <summary>The model changed: show the new items.</summary>
    void Refresh();

    /// <summary>The user changed language: redraw the words.</summary>
    void ApplyLanguage();

    /// <summary>Design QA: puts the panel in a state a user would reach with the mouse and keyboard, so it can be captured.</summary>
    void PrepareForCapture(FolderCapture state);

    /// <summary>Starts renaming one item in place.</summary>
    void BeginRename(string fileName);

    /// <summary>Starts renaming the folder itself in the title.</summary>
    void BeginRenameFolder();

    void Close();
}

/// <summary>
/// The Big Folder: a card that shows a real folder the way a phone's large folder shows apps. A click opens an item, the header or
/// the stack opens the panel, files dropped on the card go into the folder (programs as shortcuts), and a drag anywhere
/// still moves the card. The model and the actions are shared; Glass and Pixel only draw.
/// </summary>
internal sealed partial class FolderWidget : UserControl, IWidget, IPointerWidget, IDropWidget
{
    private readonly WidgetContext _context;
    private readonly WidgetOptions _options;
    private readonly IWidgetSite _site;
    private readonly DropPolicy _policy = DropPolicy.ForThisPc();
    private readonly IFolderCardView _view;
    private WidgetSize _size = WidgetSize.Medium;
    private IFolderPanel? _panel;
    private DateTime _panelClosedAt = DateTime.MinValue;
    private FolderHit _pressedHit = FolderHit.None;

    public FolderWidget(WidgetContext context)
    {
        _context = context;
        _options = context.Options;
        _site = context.Site ?? throw new InvalidOperationException("The Big Folder needs its hosting window.");

        string? path = _options.GetString(FolderOptions.PathKey);
        if (string.IsNullOrWhiteSpace(path))
        {
            // A new card makes its own folder, under the user profile on the Desktop's drive.
            try
            {
                path = FolderActions.CreateDefaultFolder();
            }
            catch (Exception ex)
            {
                Log.Warn("Creating a Big Folder's folder failed", ex);
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Strings.FolderDefaultName);
            }
            _options.SetString(FolderOptions.PathKey, path);
        }
        Model = new FolderModel(path, FolderOptions.ParseSort(_options.GetString(FolderOptions.SortKey)), FolderOptions.ParseOrder(_options.GetString(FolderOptions.OrderKey)),
            context.Services.Dispatcher, SaveOptions);

        _view = context.Theme == WidgetTheme.Pixel ? new PixelFolderCard(_site) : new GlassFolderCard(_site);
        Content = _view.Element;
        Model.Changed += OnModelChanged;
        Model.Start();
    }

    public FolderModel Model { get; }

    public FrameworkElement View => this;

    internal IWidgetSite Site => _site;

    private void SaveOptions()
    {
        _options.SetString(FolderOptions.PathKey, Model.Path);
        _options.SetString(FolderOptions.SortKey, FolderOptions.SortText(Model.Sort));
        _options.SetString(FolderOptions.OrderKey, Model.Sort == FolderSort.Manual ? FolderOptions.OrderText(Model.Order) : null);
    }

    public void ApplySize(WidgetSize size)
    {
        _size = size;
        _view.Show(Model, size);
    }

    private void OnModelChanged()
    {
        _view.Show(Model, _size);
        _panel?.Refresh();
    }

    public void ApplyLanguage()
    {
        _view.Show(Model, _size);
        _panel?.ApplyLanguage();
    }

    public void Dispose()
    {
        _panel?.Close();
        Model.Changed -= OnModelChanged;
        Model.Dispose();
    }

    // ----- the pointer: hover, press, click -----

    public void CardPointerMoved(Point? position)
    {
        _view.SetHover(position is { } p ? _view.HitTest(p) : FolderHit.None);
    }

    public void CardPointerPressed(Point position)
    {
        _pressedHit = _view.HitTest(position);
        _view.SetPressed(_pressedHit);
    }

    public void CardPointerReleased(Point position, bool dragged)
    {
        var pressed = _pressedHit;
        _pressedHit = FolderHit.None;
        _view.SetPressed(FolderHit.None);
        if (!dragged && pressed.Kind != FolderHitKind.None && _view.HitTest(position) == pressed)
            Activate(pressed);
    }

    /// <summary>A click on the card: an item opens, the header or the stack opens the panel.</summary>
    private void Activate(FolderHit hit)
    {
        switch (hit.Kind)
        {
            case FolderHitKind.Header or FolderHitKind.Stack:
                TogglePanel();
                break;
            case FolderHitKind.Item when ItemAt(hit.Index) is { } item:
                Open(item);
                break;
        }
    }

    /// <summary>The item shown in cell <paramref name="index"/> of the card.</summary>
    private FolderItem? ItemAt(int index) => index >= 0 && index < Model.Items.Count ? Model.Items[index] : null;

    public IReadOnlyList<MenuEntry>? CardContextMenu(Point position)
    {
        var hit = _view.HitTest(position);
        return hit.Kind == FolderHitKind.Item && ItemAt(hit.Index) is { } item ? ItemMenu(item) : null;
    }

    // ----- the drop -----

    public bool AcceptsDrops => Model.Exists;

    public string DropFolderPath => Model.Path;

    public string DropFolderName => Model.Name;

    public DropPolicy DropPolicy => _policy;

    public void ShowDropHint(DropVerb? verb) => _view.ShowDropHint(verb);

    public string? DropItems(IReadOnlyList<Dropped> items, DropKeys keys)
    {
        string? error = FolderActions.ExecuteDrop(Model, _policy, items, keys, _site.WindowHandle);
        if (error is not null)
            _site.ShowWarning(Strings.FolderOperationFailed(error));
        return error;
    }

    // ----- the panel -----

    private void TogglePanel()
    {
        if (_panel is { IsOpen: true })
        {
            _panel.Close();
            return;
        }
        // The press that closes the panel (it deactivates) and the click that would reopen it are one gesture.
        if ((DateTime.UtcNow - _panelClosedAt).TotalMilliseconds < 350)
            return;
        OpenPanel();
    }

    /// <summary>Design QA (<c>--pixel-ui-open=folder-panel</c>): the panel stays open when the window loses the focus, so it can be captured.</summary>
    internal bool PanelPinned { get; private set; }

    internal void OpenPanelForCapture(FolderCapture state)
    {
        PanelPinned = true;
        OpenPanel().PrepareForCapture(state);
    }

    /// <summary>Design QA (<c>--pixel-ui-open=folder-cards</c>): shows the drop hint on the card, as if a drag were over it.</summary>
    internal void ShowDropHintForCapture(DropVerb? verb)
    {
        if (AcceptsDrops) // a missing folder refuses drops: no tint, no label
            ShowDropHint(verb);
    }

    /// <summary>Design QA (<c>--pixel-ui-open=folder-item-menu</c>): the first item's right-click menu.</summary>
    internal IReadOnlyList<MenuEntry>? FirstItemMenuForCapture() => Model.Items.Count > 0 ? ItemMenu(Model.Items[0]) : null;

    internal IFolderPanel OpenPanel()
    {
        if (_panel is { IsOpen: true } open)
            return open;
        IFolderPanel panel = _context.Theme == WidgetTheme.Pixel ? new PixelFolderPanel(this) : new GlassFolderPanel(this);
        panel.Closed += () => _panelClosedAt = DateTime.UtcNow;
        _panel = panel;
        try
        {
            panel.Open();
        }
        catch (Exception ex)
        {
            Log.Warn("Opening the Big Folder panel failed", ex);
            _panel = null;
            panel.Close();
        }
        return panel;
    }

    // ----- actions the card and the panel share -----

    internal void Open(FolderItem item) => Report(FolderActions.Open(item, _site.WindowHandle));

    internal void Report(string? error)
    {
        if (error is not null)
            _site.ShowWarning(Strings.FolderOperationFailed(error));
    }
}
