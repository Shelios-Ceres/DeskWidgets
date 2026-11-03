using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// The Pixel dock's picture (the Pixel dock design's HANDOFF sections 3 to 5, 7 and 9), with no window: what the bar shows now and how
/// each change moves it, as whole art pixels on a canvas the size of the dock window (headroom, bar, and the bar's hard shadow below).
/// <see cref="PixelDockSurface"/> paints it into its layered window; <see cref="PixelDockPreview"/> paints it into PNG sheets, so the preview
/// is the window's own drawing. A change returns its frames (the Phase 12 vocabulary: MOVE, POP, LEAVE, DISSOLVE); the caller plays them
/// one per <see cref="StepAnimator.FrameMs"/>. Positions are in art pixels from the canvas's left edge.
/// </summary>
internal sealed class PixelDockPainter
{
    public const int Head = DockGeometry.PixelHeadroomArt;
    public const int Pad = DockGeometry.PixelPadArt;
    public const int HaloRoom = 2;
    public const int ShadowOffset = MenuLayout.ShadowOffset;

    /// <summary>D7: a dragged icon rides this far above its resting place.</summary>
    public const int DragRise = 6;

    /// <summary>The name plate's bottom is this far above the bar's top; it is kept this far inside the screen.</summary>
    public const int LabelGap = 4;

    public const int LabelMaxWidth = 120;
    public const int LabelPadding = 5;

    private const int MoveFrames = 3;

    private readonly PixelDockArt _art;
    private readonly Dictionary<(int Width, int Height, bool Dark), PixelCanvas> _bars = [];
    private readonly Dictionary<(string Text, bool Dark, bool Select), PixelCanvas> _labels = [];
    private PixelText? _emptyText;

    public PixelDockPainter(PixelDockArt art) => _art = art;

    // ----- the layout the controller gave -----

    public IReadOnlyList<DockItemView> Items { get; private set; } = [];

    public DockIconSize Size { get; private set; } = DockIconSize.Medium;

    public int IconArt => DockGeometry.PixelIconArt(Size);

    public int SlotArt => DockGeometry.PixelSlotArt(Size);

    /// <summary>Slot to slot: an icon and a gap (26, 30 or 38).</summary>
    public int Pitch => SlotArt + DockGeometry.PixelSlotGapArt;

    public int BarHeight => DockGeometry.PixelBarHeightArt(Size);

    public int BarWidth => DockGeometry.PixelBarLengthArt(Size, Items.Count);

    public int CanvasWidth { get; set; }

    public int CanvasHeight => Head + BarHeight + ShadowOffset;

    /// <summary>The bar's left edge as placed (where the controller's hit tests are).</summary>
    public int BarLeft { get; set; }

    /// <summary>The screen's work area, so the name plate stays <see cref="LabelGap"/> inside it.</summary>
    public int ScreenLeft { get; set; } = int.MinValue / 2;

    public int ScreenRight { get; set; } = int.MaxValue / 2;

    // ----- what is shown now -----

    public int ShownBarLeft { get; private set; }

    public int ShownBarWidth { get; private set; }

    /// <summary>Each icon's slot box, left edge (the icon is <see cref="HaloRoom"/> inside it).</summary>
    private readonly Dictionary<string, int> _shownX = [];

    /// <summary>Hover lift per icon (0 to 2), so the one being left can go down while the next goes up.</summary>
    private readonly Dictionary<string, int> _lift = [];

    private readonly HashSet<string> _hidden = [];

    /// <summary>Each icon's running look (section 1) and where its D3 to D5 frames are.</summary>
    private readonly Dictionary<string, RunLook> _run = [];
    private readonly HashSet<string> _popping = [];
    private int _popFrame;
    private readonly List<LeavingCell> _leaving = [];
    private int _leaveMinBayer;
    private int _dissolve;

    public int Pressed { get; set; } = -1;

    /// <summary>D2: the one FLASH frame after a click.</summary>
    public int Flash { get; set; } = -1;

    /// <summary>D7: the icon being dragged along the bar (-1: none) and its centre, in art pixels from the bar's left.</summary>
    public int DragIndex { get; set; } = -1;

    public int DragCenter { get; set; }

    /// <summary>D10: the slot a dragged program would land in (-1: none), and whether its dotted box is up (after the MOVE).</summary>
    public int Gap { get; private set; } = -1;

    private bool _gapShown;

    public int LabelIndex { get; set; } = -1;

    public string? Label { get; set; }

    public bool LabelShown { get; set; }

    /// <summary>D8: the icon a dragged one has been held over (-1: none): a program shows as a group of one, its own mini.</summary>
    public int Forming { get; set; } = -1;

    /// <summary>
    /// A program from outside held over a group's middle (HANDOFF 4): the group at <c>Index</c> shows the program's mini in its next free mini
    /// cell, or lifts 2 px when it holds four already. Null: none.
    /// </summary>
    public (int Index, DockItemView Dragged)? DropPreview { get; set; }

    /// <summary>A member dragged out of a group's panel, drawn where the pointer is (canvas art pixels, its centre) once it has left the panel; null: none.</summary>
    public (DockItemView View, int X, int Y)? Floating { get; set; }

    /// <summary>D12: per icon, whether it takes the files being dragged over the dock (a group: a member does; null: no file drag).</summary>
    public IReadOnlyList<bool>? OpenLit { get; set; }

    /// <summary>D12: the icon under the files (-1: none); when it is lit it lifts 2 and its plate says "Open with {name}" in Select.</summary>
    public int OpenHover { get; set; } = -1;

    /// <summary>D8 on the drop: the new group and where its POP is (1: the target's mini alone while the bar closes up, 2: all minis 1 px up).</summary>
    private string? _groupPop;
    private int _groupPopStage;

    private bool IsLit(int index) => OpenLit is { } lit && index >= 0 && index < lit.Count && lit[index];

    // ----- targets -----

    private bool Widened => Gap >= 0 && Items.Count > 0;

    public int TargetBarLeft => Widened ? BarLeft - Pitch / 2 : BarLeft;

    public int TargetBarWidth => Widened ? BarWidth + Pitch : BarWidth;

    /// <summary>Where icon <paramref name="index"/>'s slot box goes: its place on the bar, or one slot aside of an open gap (half each way, as the bar grows from its centre).</summary>
    public int TargetX(int index)
    {
        int x = BarLeft + Pad + index * Pitch;
        if (Widened)
            x += index < Gap ? -(Pitch / 2) : Pitch - Pitch / 2;
        return x;
    }

    private int SlotTop => Head + Pad;

    private int IconTop => SlotTop + HaloRoom;

    // ----- changes -----

    /// <summary>Text changed with the language: the plates and the empty dock's words are laid out again.</summary>
    public void ForgetText()
    {
        _labels.Clear();
        _emptyText = null;
    }

    /// <summary>Everything at its place at once: no motion left over.</summary>
    public void Snap()
    {
        ShownBarLeft = TargetBarLeft;
        ShownBarWidth = TargetBarWidth;
        _shownX.Clear();
        for (int i = 0; i < Items.Count; i++)
            _shownX[Items[i].Item.Key] = TargetX(i);
        _hidden.Clear();
        _popping.Clear();
        _popFrame = 0;
        _leaving.Clear();
        _leaveMinBayer = 0;
        _dissolve = 0;
        _groupPop = null;
        _groupPopStage = 0;
        _gapShown = Gap >= 0;
        foreach (var key in _lift.Keys.ToList())
        {
            if (!_shownX.ContainsKey(key))
                _lift.Remove(key);
        }
    }

    /// <summary>The window was placed again with the bar elsewhere in it: what is shown keeps its place on the screen.</summary>
    public void Shift(int dx)
    {
        if (dx == 0)
            return;
        ShownBarLeft += dx;
        foreach (var key in _shownX.Keys.ToList())
            _shownX[key] += dx;
        for (int i = 0; i < _leaving.Count; i++)
            _leaving[i] = _leaving[i] with { X = _leaving[i].X + dx };
    }

    /// <summary>
    /// The controller's new list. Returns the frames that show the change (D7, D10, D11, D13), an empty list when it snaps, or null when the
    /// same icons only need a redraw and whatever is moving keeps moving (<paramref name="keepMotion"/>: no new placement since the last list).
    /// </summary>
    public List<Action>? Change(IReadOnlyList<DockItemView> items, DockIconSize size, DockChange change, PixelPalette palette, bool animate, bool keepMotion)
    {
        bool sameKeys = size == Size && items.Select(v => v.Item.Key).SequenceEqual(Items.Select(v => v.Item.Key));
        var oldItems = Items;
        var leaving = new List<LeavingCell>();
        if (change.Kind == DockChangeKind.Removed && animate)
        {
            var keep = items.Select(v => v.Item.Key).ToHashSet();
            foreach (var view in oldItems)
            {
                if (!keep.Contains(view.Item.Key) && _shownX.TryGetValue(view.Item.Key, out int x) && !_hidden.Contains(view.Item.Key))
                    leaving.Add(Snapshot(palette, view, x));
            }
        }
        // D8: a new group stands where its target was, under a new key: it moves from the target's place, not from nowhere.
        bool newGroup = false;
        // To = -1: a program from outside joined a group that was there; there is no dragged slot and no new key.
        if (change.Kind == DockChangeKind.Grouped && change.Index >= 0 && change.Index < items.Count && change.To >= 0)
        {
            int oldTarget = PixelDockPanelLayout.GroupedOldTarget(change.Index, change.To);
            string groupKey = items[change.Index].Item.Key;
            if (oldTarget >= 0 && oldTarget < oldItems.Count && oldItems[oldTarget].Item.Key != groupKey)
            {
                newGroup = true;
                if (_shownX.TryGetValue(oldItems[oldTarget].Item.Key, out int targetX))
                    _shownX[groupKey] = targetX;
            }
        }
        Items = items;
        Size = size;
        SyncRunLooks();
        if (Gap > items.Count)
            Gap = items.Count;
        if (change.Kind is DockChangeKind.Grouped or DockChangeKind.Ungrouped)
        {
            Forming = -1; // the hold is over, and the indexes changed
            DropPreview = null;
        }

        if (change.Kind == DockChangeKind.None && sameKeys && keepMotion)
            return null;
        if (!animate)
        {
            Snap();
            return [];
        }
        switch (change.Kind)
        {
            case DockChangeKind.Added:
                return Added(oldItems);
            case DockChangeKind.Removed:
                return Removed(leaving);
            case DockChangeKind.Moved when sameKeys || items.Count == oldItems.Count:
                // D7: the dragged icon's box is in its new slot at once (the gap follows the pointer); the one it passed MOVEs over.
                if (change.To >= 0 && change.To < items.Count)
                    _shownX[items[change.To].Item.Key] = TargetX(change.To);
                return Move(lastFrame: null);
            case DockChangeKind.Resized:
                // D13: the window and the bar's frame are new at once; the icons DISSOLVE in.
                Snap();
                return [() => _dissolve = 1, () => _dissolve = 2, () => _dissolve = 3, () => _dissolve = 0];
            case DockChangeKind.Grouped when change.Index >= 0 && change.Index < items.Count:
                return Grouped(items[change.Index].Item.Key, newGroup);
            case DockChangeKind.Ungrouped:
                // The members stand in the group's place: the icons after it MOVE aside as the bar grows, then the members POP in (a quick settle).
                return Added(oldItems);
            default:
                Snap();
                return [];
        }
    }

    /// <summary>D10: the icons after the new one MOVE one slot aside as the bar grows (none move when the gap was already open), then it POPs in.</summary>
    private List<Action> Added(IReadOnlyList<DockItemView> oldItems)
    {
        var old = oldItems.Select(v => v.Item.Key).ToHashSet();
        var added = Items.Select(v => v.Item.Key).Where(k => !old.Contains(k)).ToList();
        _leaving.Clear();
        _gapShown = false;
        for (int i = 0; i < Items.Count; i++)
        {
            string key = Items[i].Item.Key;
            if (!old.Contains(key))
            {
                _shownX[key] = TargetX(i);
                _hidden.Add(key);
            }
        }
        if (oldItems.Count == 0)
        {
            // From the empty bar: its own length is no slot pitch away; the bar takes its new length at once.
            ShownBarLeft = TargetBarLeft;
            ShownBarWidth = TargetBarWidth;
        }
        var frames = Move(lastFrame: null);
        frames.Add(() =>
        {
            _hidden.Clear();
            _popping.Clear();
            _popping.UnionWith(added);
            _popFrame = 1; // a 7 x 7 mini where the icon will be
        });
        frames.Add(() => _popFrame = 2); // the icon, 1 px up
        frames.Add(() =>
        {
            _popping.Clear();
            _popFrame = 0;
        });
        return frames;
    }

    /// <summary>
    /// D8 on the drop: the dragged app's slot closes up (MOVE) with a new group still showing its target's mini alone, then the group POPs with
    /// all its minis (1 px up, then at rest). A program that joined a group that was there already only closes up and POPs.
    /// </summary>
    private List<Action> Grouped(string groupKey, bool newGroup)
    {
        _hidden.Clear();
        _popping.Clear();
        _leaving.Clear();
        _gapShown = false;
        foreach (var key in _shownX.Keys.ToList())
        {
            if (!Items.Any(v => v.Item.Key == key))
                _shownX.Remove(key);
        }
        _groupPop = groupKey;
        _groupPopStage = newGroup ? 1 : 0;
        var frames = Move(lastFrame: null);
        frames.Add(() => _groupPopStage = 2);
        frames.Add(() =>
        {
            _groupPop = null;
            _groupPopStage = 0;
        });
        return frames;
    }

    /// <summary>D11: the icon LEAVEs where it was (Bayer at least 8, at least 12, gone), then the icons after it MOVE in as the bar shrinks.</summary>
    private List<Action> Removed(List<LeavingCell> leaving)
    {
        _hidden.Clear();
        _popping.Clear();
        _leaving.Clear();
        foreach (var key in _shownX.Keys.ToList())
        {
            if (!Items.Any(v => v.Item.Key == key))
                _shownX.Remove(key);
        }
        var frames = new List<Action>();
        if (leaving.Count > 0)
        {
            frames.Add(() =>
            {
                _leaving.Clear();
                _leaving.AddRange(leaving);
                _leaveMinBayer = 8;
            });
            frames.Add(() => _leaveMinBayer = 12);
        }
        var moves = Move(lastFrame: null);
        if (moves.Count > 0)
        {
            var first = moves[0];
            moves[0] = () =>
            {
                _leaving.Clear();
                first();
            };
            frames.AddRange(moves);
        }
        else
        {
            frames.Add(() => _leaving.Clear());
        }
        return frames;
    }

    /// <summary>
    /// A program dragged over the bar: the gap opens at <paramref name="slot"/> (-1: closes). The icons MOVE aside (3 frames) and the bar
    /// grows with them; the dotted box shows with the last frame (D10).
    /// </summary>
    public List<Action> SetGap(int slot, bool animate)
    {
        Gap = slot < 0 ? -1 : Math.Min(slot, Items.Count);
        _gapShown = false;
        if (!animate || Items.Count == 0)
        {
            ShownBarLeft = TargetBarLeft;
            ShownBarWidth = TargetBarWidth;
            for (int i = 0; i < Items.Count; i++)
                _shownX[Items[i].Item.Key] = TargetX(i);
            _gapShown = Gap >= 0;
            return [];
        }
        var frames = Move(lastFrame: () => _gapShown = Gap >= 0);
        if (frames.Count == 0)
            _gapShown = Gap >= 0;
        return frames;
    }

    /// <summary>The gap is over, with the icons left where they are (a drop: the new list that follows moves them on).</summary>
    public void ClearGap()
    {
        Gap = -1;
        _gapShown = false;
    }

    /// <summary>MOVE: from what is shown now to the targets, at 1/3, 2/3 and all of the way. Empty when nothing has to move.</summary>
    private List<Action> Move(Action? lastFrame)
    {
        int fromLeft = ShownBarLeft, fromWidth = ShownBarWidth;
        int toLeft = TargetBarLeft, toWidth = TargetBarWidth;
        var paths = new List<(string Key, int From, int To)>();
        bool any = fromLeft != toLeft || fromWidth != toWidth;
        for (int i = 0; i < Items.Count; i++)
        {
            string key = Items[i].Item.Key;
            int to = TargetX(i);
            int from = _shownX.TryGetValue(key, out int shown) ? shown : to;
            paths.Add((key, from, to));
            any |= from != to;
        }
        if (!any)
        {
            lastFrame?.Invoke();
            return [];
        }
        static int At(int from, int to, int frame) => to + (int)Math.Round((from - to) * (MoveFrames - frame) / (double)MoveFrames);
        var frames = new List<Action>();
        for (int k = 1; k <= MoveFrames; k++)
        {
            int frame = k;
            frames.Add(() =>
            {
                ShownBarLeft = At(fromLeft, toLeft, frame);
                ShownBarWidth = At(fromWidth, toWidth, frame);
                foreach (var (key, from, to) in paths)
                    _shownX[key] = At(from, to, frame);
                if (frame == MoveFrames)
                    lastFrame?.Invoke();
            });
        }
        return frames;
    }

    /// <summary>
    /// D1: one step of every icon's lift towards its target (2 for <paramref name="hoverKey"/>, 0 for the rest), or all of the way with
    /// <paramref name="snap"/>. The name plate comes on when the hovered icon is all the way up. Returns whether another step is due.
    /// </summary>
    public bool StepLifts(string? hoverKey, bool snap)
    {
        if (hoverKey is not null && !_lift.ContainsKey(hoverKey))
            _lift[hoverKey] = 0;
        bool more = false;
        foreach (var key in _lift.Keys.ToList())
        {
            int target = key == hoverKey ? 2 : 0;
            int now = _lift[key];
            int next = snap ? target : now + Math.Sign(target - now);
            if (next == 0 && target == 0)
                _lift.Remove(key);
            else
                _lift[key] = next;
            more |= next != target;
        }
        if (hoverKey is not null && _lift.GetValueOrDefault(hoverKey) == 2)
            LabelShown = true;
        return more;
    }

    // ----- drawing -----

    public void Paint(PixelCanvas canvas, PixelPalette p)
    {
        canvas.Clear();
        canvas.Blit(Bar(p, ShownBarWidth, BarHeight), ShownBarLeft, Head);
        int icon = IconArt;
        if (Items.Count == 0)
        {
            if (ShownBarWidth == TargetBarWidth)
                DrawEmpty(canvas, p);
            return;
        }
        if (_leaveMinBayer > 0)
        {
            foreach (var cell in _leaving)
                PixelMotion.BlitMasked(cell.Pixels, canvas, cell.X, cell.Y, _leaveMinBayer);
        }
        for (int i = 0; i < Items.Count; i++)
        {
            var view = Items[i];
            string key = view.Item.Key;
            if (_hidden.Contains(key) || !_shownX.TryGetValue(key, out int sx))
                continue;
            if (i == DragIndex)
            {
                DottedBox(canvas, sx + HaloRoom, IconTop, icon, icon, p.Faint);
                continue;
            }
            if (IsLit(i))
            {
                // D12: a Select plate behind it at once; the one under the files lifts 2. The plate carries the icon alone (no halo), as the FLASH does.
                Plate(canvas, sx, SlotTop, SlotArt, p.Select);
                var litLook = view.Missing ? null : _run.GetValueOrDefault(key);
                BlitDissolved(_art.Icon(p, view, icon, awake: litLook?.IsRunningAtRest == true), canvas, sx + HaloRoom, IconTop - (i == OpenHover ? 2 : 0));
                continue;
            }
            if (i == Forming && !view.IsGroup)
            {
                // D8: held over a program, it shows as a group of one under the dragged icon.
                BlitDissolved(_art.FormingTile(p, view, icon), canvas, sx + HaloRoom, IconTop);
                continue;
            }
            if (key == _groupPop && _groupPopStage == 1 && view.Children is { Count: > 0 } members)
            {
                BlitDissolved(_art.GroupTile(p, [members[0]], icon), canvas, sx + HaloRoom, IconTop);
                continue;
            }
            bool flash = i == Flash, pressed = i == Pressed && !flash;
            if (flash)
                Plate(canvas, sx, SlotTop, SlotArt, p.Select);
            else if (pressed)
                Plate(canvas, sx, SlotTop, SlotArt, p.Shade);
            var look = view.Missing ? null : _run.GetValueOrDefault(key);
            int lift = flash ? 0 : pressed ? -1 : Math.Max(_lift.GetValueOrDefault(key), look?.HopLift ?? 0);
            if (_popping.Contains(key))
            {
                if (_popFrame == 1)
                {
                    var mini = _art.Mini(p, view);
                    BlitDissolved(mini, canvas, sx + HaloRoom + (icon - mini.Width) / 2, IconTop + (icon - mini.Height) / 2);
                    continue;
                }
                lift = 1;
            }
            if (key == _groupPop && _groupPopStage == 2)
                lift = 1;
            if (DropPreview is { } preview && preview.Index == i && view.Children is { } children)
            {
                // The program's mini in the next free cell; a full group lifts instead.
                if (children.Count >= 4)
                    lift = Math.Max(lift, 2);
                else
                    view = view with { Children = [.. children, preview.Dragged] };
            }
            if (flash)
            {
                // The FLASH plate carries the icon alone: a running one keeps its awake drawing, without its halo (the mock's rule).
                BlitDissolved(_art.Icon(p, view, icon, awake: look?.IsRunningAtRest == true), canvas, sx + HaloRoom, IconTop);
                continue;
            }
            DrawLook(canvas, p, view, look, sx, SlotTop - lift);
        }
        if (_gapShown && Gap >= 0)
            DottedBox(canvas, TargetBarLeft + Pad + Gap * Pitch + HaloRoom, IconTop, icon, icon, p.Soft);
        if (DragIndex >= 0 && DragIndex < Items.Count)
            canvas.Blit(_art.Icon(p, Items[DragIndex], icon), BarLeft + DragCenter - icon / 2, IconTop - DragRise);
        if (Floating is { } floating)
        {
            // Undimmed, at the bar's icon size: it is headed for the bar.
            var picture = _art.Icon(p, floating.View, icon);
            canvas.Blit(picture, floating.X - icon / 2, floating.Y - icon / 2);
        }
        if (OpenLit is not null)
        {
            // D12: only the lit icon under the files has a plate, "Open with {name}" in Select; the hover label waits for the drag to end. A lit
            // group (a member takes the files) shows its own name on a plain plate: the drop goes to a member, in its panel.
            if (IsLit(OpenHover) && OpenHover < Items.Count && _shownX.TryGetValue(Items[OpenHover].Item.Key, out int ox))
            {
                var target = Items[OpenHover];
                DrawPlate(canvas, target.IsGroup ? LabelPlate(p, target.Name) : LabelPlate(p, Strings.DockOpenWith(target.Name), select: true), ox);
            }
        }
        else if (LabelShown && Label is { Length: > 0 } text && LabelIndex >= 0 && LabelIndex < Items.Count && DragIndex < 0
            && _shownX.TryGetValue(Items[LabelIndex].Item.Key, out int lx))
        {
            DrawPlate(canvas, LabelPlate(p, text), lx);
        }
    }

    /// <summary>A name plate over the slot whose box is at <paramref name="slotX"/>: its bottom <see cref="LabelGap"/> above the bar, centred, kept inside the screen.</summary>
    private void DrawPlate(PixelCanvas canvas, PixelCanvas plate, int slotX)
    {
        int width = plate.Width - ShadowOffset, height = plate.Height - ShadowOffset;
        int left = Math.Max(0, ScreenLeft + LabelGap);
        int right = Math.Min(CanvasWidth - ShadowOffset, ScreenRight - LabelGap);
        int x = Math.Clamp(slotX + SlotArt / 2 - width / 2, left, Math.Max(left, right - width));
        canvas.Blit(plate, x, Math.Max(0, Head - LabelGap - height));
    }

    /// <summary>
    /// One icon in its run look, its slot box's top-left at (x, y): the idle drawing, the running one (awake inside its halo), or a DISSOLVE
    /// between them (D3 to D5) on the 4 × 4 Bayer mask laid from the bar's left edge, as the design's frames are.
    /// </summary>
    private void DrawLook(PixelCanvas canvas, PixelPalette p, DockItemView view, RunLook? look, int x, int y)
    {
        int size = IconArt;
        if (look is null || look.IsIdleAtRest)
        {
            BlitDissolved(_art.Icon(p, view, size), canvas, x + HaloRoom, y + HaloRoom);
            return;
        }
        var running = _art.Running(p, view, size);
        if (look.IsRunningAtRest)
        {
            BlitDissolved(running, canvas, x, y);
            return;
        }
        var idle = _art.Icon(p, view, size);
        for (int sy = 0; sy < running.Height; sy++)
        {
            for (int sx = 0; sx < running.Width; sx++)
            {
                if (_dissolve > 0 && PixelMotion.BayerAt(x + sx, y + sy) >= 4 * _dissolve)
                    continue;
                // The design's frames lay the mask from the bar's left edge and the window's top.
                uint value = look.ShowsRunning(PixelMotion.BayerAt(x + sx - BarLeft, y + sy)) ? running.Get(sx, sy) : idle.Get(sx - HaloRoom, sy - HaloRoom);
                if (value != 0)
                    canvas.Set(x + sx, y + sy, value);
            }
        }
    }

    /// <summary>What a leaving icon looked like at rest (D11 LEAVEs that picture): the running look's halo goes with it.</summary>
    private LeavingCell Snapshot(PixelPalette p, DockItemView view, int slotX)
    {
        var look = view.Missing ? null : _run.GetValueOrDefault(view.Item.Key);
        if (look is not null && !look.IsIdleAtRest)
            return new LeavingCell(_art.Running(p, view, IconArt), slotX, SlotTop);
        return new LeavingCell(_art.Icon(p, view, IconArt), slotX + HaloRoom, IconTop);
    }

    // ----- the running look (D3 to D5) -----

    /// <summary>
    /// An icon's run state changed. With <paramref name="animate"/> it plays its moment: <see cref="DockRunTransition.Opening"/> the hop and
    /// DISSOLVE frames 1 and 2, then holds (a still frame: nothing ticks); OpenedHere and OpenedElsewhere the rest of the DISSOLVE (D4; from
    /// idle all four frames); Closed DISSOLVEs to idle (D5); TimedOut goes back the way it came. Frame 0 is shown at once. Returns whether
    /// it still has frames to play (step them with <see cref="StepRuns"/>).
    /// </summary>
    public bool SetRun(string key, DockRunState state, DockRunTransition how, bool animate)
    {
        if (!_run.TryGetValue(key, out var look))
            _run[key] = look = RunLook.AtRest(state);
        if (!animate || how == DockRunTransition.None)
        {
            look.Snap(state);
            return false;
        }
        look.Begin(state, hop: how == DockRunTransition.Opening);
        return look.Animating;
    }

    /// <summary>One Pixel frame of every icon whose run look is moving. Returns whether any still is.</summary>
    public bool StepRuns()
    {
        bool more = false;
        foreach (var look in _run.Values)
        {
            if (look.Animating)
            {
                look.Step();
                more |= look.Animating;
            }
        }
        return more;
    }

    public bool RunsAnimating => _run.Values.Any(l => l.Animating);

    /// <summary>Every run look at the state it is heading for, at once (the dock is being hidden: no frames left to step).</summary>
    public void SnapRuns()
    {
        foreach (var look in _run.Values)
        {
            if (look.Animating)
                look.Snap(look.Target);
        }
    }

    /// <summary>The list's states are the controller's: a look that shows (or is heading for) another state takes the list's at once.</summary>
    private void SyncRunLooks()
    {
        var keys = new HashSet<string>();
        foreach (var view in Items)
        {
            string key = view.Item.Key;
            keys.Add(key);
            if (!_run.TryGetValue(key, out var look))
                _run[key] = RunLook.AtRest(view.State);
            else if (look.Target != view.State)
                look.Snap(view.State);
        }
        foreach (var key in _run.Keys.ToList())
        {
            if (!keys.Contains(key))
                _run.Remove(key);
        }
    }

    /// <summary>
    /// The DISSOLVE between an icon's idle and running pictures as quarters: going forward, the running picture shows where the Bayer value is
    /// below 4 × <see cref="K"/>; going back (D5 from running), the idle one does. At rest a look is idle, running, or D3's hold (forward, K = 2).
    /// </summary>
    private sealed class RunLook
    {
        private static readonly int[] HopLifts = [1, 2, 3, 3, 2, 1, 0];

        private bool _forward = true;
        private int _targetK;
        private int _hop = -1;

        public int K { get; private set; }

        public DockRunState Target { get; private set; }

        public bool Animating => _hop >= 0 || K != _targetK;

        public int HopLift => _hop >= 0 ? HopLifts[_hop] : 0;

        public bool IsIdleAtRest => !Animating && (_forward ? K == 0 : K == 4);

        public bool IsRunningAtRest => !Animating && (_forward ? K == 4 : K == 0);

        public bool ShowsRunning(int bayer) => _forward ? bayer < 4 * K : bayer >= 4 * K;

        public static RunLook AtRest(DockRunState state)
        {
            var look = new RunLook();
            look.Snap(state);
            return look;
        }

        public void Snap(DockRunState state)
        {
            Target = state;
            _forward = true;
            _hop = -1;
            K = _targetK = state switch { DockRunState.Running => 4, DockRunState.Opening => 2, _ => 0 };
        }

        public void Begin(DockRunState state, bool hop)
        {
            Target = state;
            if (state == DockRunState.Idle)
            {
                // D5 from running uses the closing mask (idle comes in where Bayer is low); from D3's hold it goes back the way it came.
                if (_forward && K == 4 && _hop < 0)
                    (_forward, K) = (false, 0);
                _hop = -1;
                _targetK = _forward ? 0 : 4;
            }
            else
            {
                // Opening and running go forward; one interrupted half-way back keeps its mask and turns round.
                if (!_forward && K == 4)
                    (_forward, K) = (true, 0);
                if (!_forward && state == DockRunState.Opening)
                    (_forward, K) = (true, 4 - K);
                int full = state == DockRunState.Opening ? 2 : 4;
                _targetK = _forward ? full : 4 - full;
                if (hop && _forward && K == 0)
                    _hop = 0; // frame 0 of the hop, shown at once
            }
            if (_hop < 0)
                StepK(); // frame 0 of a DISSOLVE is its first quarter, shown at once
        }

        public void Step()
        {
            if (_hop >= 0)
            {
                if (++_hop < HopLifts.Length)
                    return;
                _hop = -1;
            }
            StepK();
        }

        private void StepK()
        {
            K += Math.Sign(_targetK - K);
            if (!Animating && !_forward && (K == 0 || K == 4))
            {
                // Back at rest: keep it in the forward form.
                _forward = true;
                K = 4 - K;
                _targetK = K;
            }
        }
    }

    /// <summary>Where the dissolve leaves an icon out (D13), the bare bar shows: Bayer below 4 per frame comes in first.</summary>
    private void BlitDissolved(PixelCanvas source, PixelCanvas target, int x, int y)
    {
        if (_dissolve == 0)
        {
            target.Blit(source, x, y);
            return;
        }
        for (int sy = 0; sy < source.Height; sy++)
        {
            for (int sx = 0; sx < source.Width; sx++)
            {
                uint value = source.Get(sx, sy);
                if (value != 0 && PixelMotion.BayerAt(x + sx, y + sy) < 4 * _dissolve)
                    target.Set(x + sx, y + sy, value);
            }
        }
    }

    /// <summary>The empty dock (section 7): a dotted <c>Faint</c> box inset by the halo room, "DROP APPS HERE" in <c>Soft</c> in its middle. A program dragged over it turns the box <c>Soft</c>.</summary>
    private void DrawEmpty(PixelCanvas canvas, PixelPalette p)
    {
        int inset = Pad + HaloRoom;
        int left = ShownBarLeft + inset, top = Head + inset, width = ShownBarWidth - 2 * inset, height = IconArt;
        DottedBox(canvas, left, top, width, height, Gap >= 0 ? p.Soft : p.Faint);
        _emptyText ??= FolderPixelScene.Layout(Strings.DockEmpty, PixelFonts.Regular);
        _emptyText.Draw(canvas, ShownBarLeft + (ShownBarWidth - _emptyText.Width) / 2, top + (height - _emptyText.Height) / 2, p.Soft);
    }

    /// <summary>The bar: the card's frame (outline, Panel, Highlight, two Shade rows, corners [4, 2, 1, 1]) over the menus' hard shadow.</summary>
    private PixelCanvas Bar(PixelPalette p, int width, int height)
    {
        bool dark = p.Panel == PixelPalette.For(true).Panel;
        if (_bars.TryGetValue((width, height, dark), out var bar))
            return bar;
        var panel = new PixelCanvas(width, height);
        PixelFrame.Draw(panel, 0, 0, width, height, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);
        if (_bars.Count > 64)
            _bars.Clear(); // a bar that grows and shrinks leaves a few lengths behind
        return _bars[(width, height, dark)] = PixelFrame.WithShadow(panel, p.Shadow, ShadowOffset);
    }

    /// <summary>
    /// The name plate (section 5): a menu-style plate (outline, Panel, Highlight, one Shade row, corners [2, 1]) with the hard shadow;
    /// 13 tall for the pixel font, 15 for Fusion; the text 5 from each side, at most 120 wide, middle-shortened. <paramref name="select"/>: the
    /// "Open with" plate (D12), in the Select colours with OnSelect text.
    /// </summary>
    public PixelCanvas LabelPlate(PixelPalette p, string text, bool select = false)
    {
        bool dark = p.Panel == PixelPalette.For(true).Panel;
        if (_labels.TryGetValue((text, dark, select), out var cached))
            return cached;
        if (_labels.Count > 64)
            _labels.Clear();
        return _labels[(text, dark, select)] = BuildLabelPlate(p, text, select, LabelMaxWidth);
    }

    /// <summary>
    /// <see cref="LabelPlate"/>'s drawing, at most <paramref name="maxWidth"/> wide with the text middle-shortened to fit (the group panel's
    /// title row draws its "Open with" plate with it, in the room left of Open all).
    /// </summary>
    public static PixelCanvas BuildLabelPlate(PixelPalette p, string text, bool select, int maxWidth)
    {
        var line = FolderPixelScene.OneLine(text, PixelFonts.Regular, maxWidth - 2 * LabelPadding);
        // SimSun is 12 tall: its plate keeps 2 above the text so that, with the 4-px gap, it still fits the 21 of headroom.
        int top = line.Rank == 2 ? 2 : 3;
        int width = Math.Min(maxWidth, line.Width + 2 * LabelPadding), height = line.Height + top + 3;
        var plate = new PixelCanvas(width, height);
        if (select)
            PixelFrame.Draw(plate, 0, 0, width, height, PixelFrame.MenuCorner, p.SelectPressed, p.Select, p.SelectLight, p.SelectPressed, 1);
        else
            PixelFrame.Draw(plate, 0, 0, width, height, PixelFrame.MenuCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 1);
        line.Draw(plate, LabelPadding, top, select ? p.OnSelect : p.Ink);
        return PixelFrame.WithShadow(plate, p.Shadow, ShadowOffset);
    }

    /// <summary>A plate behind a slot (pressed: <c>Shade</c>; the click's FLASH: <c>Select</c>), corners [2, 1].</summary>
    public static void Plate(PixelCanvas canvas, int x, int y, int size, uint color)
    {
        for (int j = 0; j < size; j++)
        {
            int edge = Math.Min(j, size - 1 - j);
            int inset = edge == 0 ? 2 : edge == 1 ? 1 : 0;
            canvas.Fill(x + inset, y + j, size - 2 * inset, 1, color);
        }
    }

    /// <summary>A dotted box: every other pixel of its edge, the corners left out (the Big Folder's empty-state dots).</summary>
    public static void DottedBox(PixelCanvas canvas, int x, int y, int width, int height, uint color)
    {
        for (int i = 1; i < width - 1; i += 2)
        {
            canvas.Set(x + i, y, color);
            canvas.Set(x + i, y + height - 1, color);
        }
        for (int j = 1; j < height - 1; j += 2)
        {
            canvas.Set(x, y + j, color);
            canvas.Set(x + width - 1, y + j, color);
        }
    }
}

internal static class PixelDockCanvas
{
    /// <summary>Copies the non-transparent pixels of <paramref name="source"/> with its top-left at (x, y).</summary>
    public static void Blit(this PixelCanvas target, PixelCanvas source, int x, int y) => FolderPixelScene.Blit(source, target, x, y);
}
