using System.Numerics;
using DeskWidgets.Motion;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace DeskWidgets.Dock;

/// <summary>
/// The Glass dock's Build C marks on the bar (HANDOFF 3 and 5): D8's forming plate and the group icon's settle, D12's lit plates, lift and
/// "Open with" pill. Every plate fades on its visual from where it is; with "Animation effects" off each change is one frame.
/// </summary>
internal sealed partial class GlassDockSurface
{
    /// <summary>D8's plate is 6 larger than the tile on each side, D12's 4.</summary>
    private const double FormGrow = 6, LitGrow = 4;

    /// <summary>D8: the group icon settles from 92 %.</summary>
    private const float GroupSettleScale = 0.92f;

    /// <summary>D8: the dragged icon floats over the plate at 85 %.</summary>
    private const float FloatOpacity = 0.85f;

    private string? _formingKey;
    private IReadOnlyList<bool>? _openWithLit;
    private int _openWithHovered = -1;

    // ---------------------------------------------------------------- D8, group forming

    public void SetGroupForming(int index)
    {
        if (_disposed)
            return;
        string? key = index >= 0 && index < _slots.Count ? _slots[index].Key : null;
        if (key == _formingKey)
            return;
        // The plate fades in (Quick) and out (Exit); the dragged icon floats over it at 85 % while it shows.
        if (Find(_formingKey) is { } before && before.FormPlate is { } old)
            GlassMotion.FadeTo(old, 0, Animate ? GlassMotion.Exit : TimeSpan.Zero, GlassEase.In);
        _formingKey = key;
        if (Find(key) is { } target)
        {
            var plate = target.FormPlate ??= MakePlate(target, FormGrow, Palette.Pressed);
            GlassMotion.FadeTo(plate, 1, Animate ? GlassMotion.Quick : TimeSpan.Zero, GlassEase.Out);
        }
        if (Find(_dragKey) is { } dragged)
            FloatDragged(dragged, floating: key is not null);
    }

    /// <summary>The dragged icon at 85 % while a forming plate shows, back to full when it goes (the plate's own timing).</summary>
    private void FloatDragged(Slot slot, bool floating)
        => GlassMotion.FadeTo(slot.Root, floating ? FloatOpacity : 1, Animate ? (floating ? GlassMotion.Quick : GlassMotion.Exit) : TimeSpan.Zero, floating ? GlassEase.Out : GlassEase.In);

    /// <summary>A plate behind the tile (inside the lift, so it rises with it), hidden until faded in; the radius is 0.225 of its side.</summary>
    private static Rectangle MakePlate(Slot slot, double grow, Microsoft.UI.Xaml.Media.Brush fill)
    {
        var (rect, radius) = GlassDockLayout.Plate(slot.Size, grow);
        var plate = new Rectangle { Width = rect.Width, Height = rect.Height, RadiusX = radius, RadiusY = radius, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(plate, rect.X);
        Canvas.SetTop(plate, rect.Y);
        GlassMotion.VisualOf(plate).Opacity = 0; // an element whose Opacity property is 0 is not drawn at all, and cannot fade
        slot.Plates.Children.Add(plate);
        return plate;
    }

    /// <summary>
    /// D8, Grouped: a new group icon starts where the target stood (the target's old slot), so it slides with the others into its place
    /// instead of jumping there. A program that joined an existing group needs nothing: the group's slot is the same one.
    /// </summary>
    private void PlaceNewGroup(DockChange change, List<Slot> before, Dictionary<string, Slot> gone, List<Slot> entering)
    {
        if (change.Index < 0 || change.Index >= _slots.Count || !entering.Contains(_slots[change.Index]))
            return;
        // The dragged program was at To in the old list; the target was at the group's index, one further on when the dragged one stood before it.
        int targetOld = change.To >= 0 && change.To <= change.Index ? change.Index + 1 : change.Index;
        if (targetOld < 0 || targetOld >= before.Count || !gone.ContainsValue(before[targetOld]) || double.IsNaN(before[targetOld].X))
            return;
        var target = before[targetOld];
        MoveSlot(_slots[change.Index], target.X, target.Y, TimeSpan.Zero);
    }

    /// <summary>D8: letting go, the group icon settles from 92 % (Release).</summary>
    private static void SettleGroup(Slot slot)
    {
        GlassMotion.Preset(slot.Lift, 1, GroupSettleScale, Centre(slot));
        GlassMotion.ScaleTo(slot.Lift, 1, GlassMotion.Release, GlassEase.Out, Centre(slot));
    }

    /// <summary>Ungrouped: the programs that stand in the group's place settle in quickly (opacity and 92 % to full, Quick) while the rest slide.</summary>
    private static void SettleUngrouped(Slot slot)
    {
        GlassMotion.Preset(slot.Lift, 0, GroupSettleScale, Centre(slot));
        GlassMotion.FadeTo(slot.Lift, 1, GlassMotion.Quick, GlassEase.Out);
        GlassMotion.ScaleTo(slot.Lift, 1, GlassMotion.Quick, GlassEase.Out, Centre(slot));
    }

    // ---------------------------------------------------------------- D12, open with

    public void SetOpenWith(IReadOnlyList<bool>? lit, int hovered)
    {
        if (_disposed)
            return;
        bool ending = lit is null && _openWithLit is not null;
        _openWithLit = lit;
        _openWithHovered = lit is null ? -1 : hovered;
        ApplyOpenWith(animate: true);
        if (ending)
            HideLabel(snap: false); // the controller sends the hover again if the pointer is still over an icon
    }

    /// <summary>
    /// D12: the lit icons get the Drop-tint plate and the lift (Quick in, Exit out); the others do not change. The hovered lit app's label is a
    /// Select pill, "Open with {name}", which moves with the pointer as the label does; a hovered lit group shows its name as the plain label;
    /// over an unlit icon there is no label.
    /// </summary>
    private void ApplyOpenWith(bool animate)
    {
        animate &= Animate;
        double lift = -DockGeometry.LiftDip(_size);
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            bool on = _openWithLit is { } lit && i < lit.Count && lit[i]; // a group is lit when one of its members takes the files
            if (on == slot.Lit)
                continue;
            slot.Lit = on;
            if (on || slot.LitPlate is not null)
            {
                var plate = slot.LitPlate ??= MakePlate(slot, LitGrow, Palette.DropTint);
                GlassMotion.FadeTo(plate, on ? 1 : 0, animate ? (on ? GlassMotion.Quick : GlassMotion.Exit) : TimeSpan.Zero, on ? GlassEase.Out : GlassEase.In);
            }
            if (slot.Key != _dragKey)
            {
                bool lifted = on || slot.Key == _hoverKey;
                LiftTo(slot, lifted ? lift : 0, animate ? (on ? GlassMotion.Quick : GlassMotion.Exit) : TimeSpan.Zero, on ? Bezier.Out : Bezier.In);
            }
        }
        if (_openWithLit is null)
            return;
        if (_openWithHovered >= 0 && _openWithHovered < _slots.Count && _slots[_openWithHovered] is { Lit: true } hovered)
        {
            // A lit group shows its own name as the plain label: the drop goes to one of its members, in its panel.
            if (hovered.View.IsGroup)
                ShowLabel(hovered, hovered.View.Name);
            else
                ShowLabel(hovered, Strings.DockOpenWith(hovered.View.Name), select: true);
        }
        else
            HideLabel(snap: false);
    }

    /// <summary>The label as a Select pill with white text (D12) or as the glass label (D1): the pill covers the tint and the sheen.</summary>
    private void SetLabelSelected(bool select)
    {
        if (select == _labelSelected)
            return;
        _labelSelected = select;
        _labelSelect.Visibility = select ? Visibility.Visible : Visibility.Collapsed;
        _labelSheen.Visibility = select ? Visibility.Collapsed : Visibility.Visible;
        _labelText.Foreground = select ? Palette.OnSelect : Palette.Primary;
    }

    // ---------------------------------------------------------------- the group's panel (D9): the window is GlassDockPanel

    private GlassDockPanel? _panel; // made on the first open and reused (a WinUI window made and closed over and over leaks handles, Phase 6)
    private IDropTarget? _panelDropTarget;
    private string? _panelGroupKey;

    public event Action<DockPanelEvent>? Panel;

    public bool PanelOpen => _panel?.IsOpen == true;

    public DockRect PanelRect => _panel is { IsOpen: true } panel ? panel.Rect : default;

    public void ShowPanel(int groupIndex, string title, IReadOnlyList<DockItemView> members, bool forDrop)
    {
        if (_disposed || groupIndex < 0 || groupIndex >= _slots.Count)
            return;
        var slot = _slots[groupIndex];
        _panelGroupKey = slot.Key;
        _panel ??= new GlassDockPanel(_tiles!, _dragImage, RaisePanel);
        if (_panelDropTarget is { } target)
            _panel.AttachDropTarget(target);
        // Centred on the group icon where it is drawn (a gap or a slide may have moved it from the controller's slot).
        double centre = double.IsNaN(slot.X) ? BarLeftDip + DockGeometry.IconLeftDip(_size, groupIndex) + IconDip / 2 : slot.X + slot.Size / 2;
        int centrePx = _actual.X + (int)Math.Round(centre * Scale);
        _panel.Show(centrePx, _bar.Y, _work, Scale, title, members, forDrop);
    }

    public void SetPanelMembers(string title, IReadOnlyList<DockItemView> members)
    {
        if (!_disposed)
            _panel?.SetMembers(title, members);
    }

    public void SetPanelOpenWith(IReadOnlyList<bool>? lit, int hovered)
    {
        if (!_disposed)
            _panel?.SetOpenWith(lit, hovered);
    }

    public void BeginPanelRename()
    {
        if (!_disposed)
            _panel?.BeginRename();
    }

    public void HidePanel(bool animated)
    {
        if (!_disposed)
            _panel?.Hide(animated);
    }

    public int PanelMemberAt(int screenX, int screenY) => _panel?.MemberAt(screenX, screenY) ?? -1;

    public void AttachPanelDropTarget(IDropTarget target)
    {
        if (_disposed)
            return;
        _panelDropTarget = target;
        _panel?.AttachDropTarget(target);
    }

    private void RaisePanel(DockPanelEvent e)
    {
        if (_disposed)
            return;
        // A member let go over the bar's gap: its picture flies there (Move) before the controller takes it out of the group, and the new
        // icon (DockChangeKind.Added, next) waits hidden under it until it has landed.
        if (e.Kind == DockPanelEventKind.DragOut && _gap >= 0 && _dragImage.Visible)
        {
            _landing = true;
            _landingKey = null;
            var gap = GlassDockLayout.Gap(_size, _barTarget, _gap);
            _dragImage.FlyTo(_actual.X + (int)Math.Round(gap.X * Scale), _actual.Y + (int)Math.Round(gap.Y * Scale), Landed);
        }
        Panel?.Invoke(e);
    }

    // ---------------------------------------------------------------- D7 out of the panel: the dragged picture lands

    private readonly GlassDockDragImage _dragImage = new();
    private bool _landing;
    private string? _landingKey;

    /// <summary>
    /// D10's new icon is the member landing from the panel: it stays hidden (no Enter) and the picture's flight is aimed at its real place, from
    /// where the picture is now (the bar may have moved since the gap). Called for the one icon an Added change brings.
    /// </summary>
    private bool TakeLanding(Slot slot)
    {
        if (!_landing || _landingKey is not null)
            return false;
        _landingKey = slot.Key;
        GlassMotion.Preset(slot.Lift, 0, 1, Centre(slot));
        _dragImage.FlyTo(_actual.X + (int)Math.Round(slot.X * Scale), _actual.Y + (int)Math.Round(slot.Y * Scale), Landed);
        return true;
    }

    /// <summary>The picture has landed: the icon takes its place at once and the picture goes; the panel's cell (if the panel is still up) shows again.</summary>
    private void Landed()
    {
        _landing = false;
        if (Find(_landingKey) is { } slot)
            GlassMotion.Preset(slot.Lift, 1, 1, Centre(slot));
        _landingKey = null;
        _dragImage.Hide();
        _panel?.RestoreDragged();
    }

    /// <summary>
    /// A press on the dock is a click outside the panel, but the dock never takes the activation, so the panel's own focus rule cannot see it:
    /// the panel is dismissed here, before the controller hears of the press. A press on the panel's own group icon is left to the controller
    /// (it toggles the panel). A panel opened for a file drag is the controller's to close.
    /// </summary>
    private void DismissPanelForPress(double barX)
    {
        if (_panel is not { IsOpen: true, ForDrop: false } panel)
            return;
        int icon = DockGeometry.IconAt(_size, _views.Count, barX);
        if (icon >= 0 && icon < _slots.Count && _slots[icon].Key == _panelGroupKey)
            return;
        panel.Dismiss();
    }
}
