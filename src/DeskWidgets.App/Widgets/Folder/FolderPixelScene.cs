using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Widgets.Folder;

/// <summary>What the Pixel card shows: the folder's state and the pointer's.</summary>
internal sealed record FolderPixelView(
    string Name,
    string Path,
    IReadOnlyList<FolderItem> Items,
    bool Exists,
    WidgetSize Size,
    FolderHit Hover,
    FolderHit Pressed,
    DropVerb? Drop,
    PixelFolderIcons Icons,
    int DropPhase = 2,
    ListMotion? Motion = null,
    FolderHit Flash = default);

/// <summary>
/// The Big Folder card in the Pixel theme (the Pixel design's HANDOFF, "Card"): metrics in art pixels, the header, the cells, the
/// stack and the empty, missing and drop-hover states. <see cref="Plan"/> works out the layout once; <see cref="Draw"/> paints it
/// and <see cref="HitTest"/> reads the same regions, so what is drawn is what is clickable.
/// </summary>
internal static class FolderPixelScene
{
    public const int CellWidth = 36;

    private static readonly int[] SmallColumns = [4, 42];
    private static readonly int[] WideColumns = [5, 47, 89, 131];

    private static readonly PixelSprite DropArrow = new(
        "...#...",
        "...#...",
        ".#.#.#.",
        "..###..",
        "...#...",
        "#.....#",
        "#######");

    /// <summary>The layout of a card: every number the drawing and the pointer need.</summary>
    internal sealed class CardPlan
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        /// <summary>The metric set: 0 pixel fonts, 1 Fusion 8 px, 2 SimSun (the highest-ranked text on the card decides).</summary>
        public required int Rank { get; init; }
        public required int HeaderY { get; init; }
        public required int HeaderHeight { get; init; }
        public required int NameGap { get; init; }
        public required int NameHeight { get; init; }
        /// <summary>True on a Large card in a pixel-font or Fusion set: names wrap onto two lines, as in the panel (SimSun cannot fit two).</summary>
        public required bool TwoLines { get; init; }
        /// <summary>A cell's content height, icon to last name line: the hover plate and the click rect are this plus 4.</summary>
        public required int CellHeight { get; init; }
        public required int[] Columns { get; init; }
        public required int[] Rows { get; init; }
        public required PixelText Title { get; init; }
        public required PixelText Count { get; init; }
        /// <summary>Each name's lines: one, or two on a <see cref="TwoLines"/> card.</summary>
        public required PixelText[][] Names { get; init; }
        public required CardCells Cells { get; init; }
        public required PixelText? StackLabel { get; init; }
        public required List<(int X, int Y, int W, int H, FolderHit Hit)> Regions { get; init; }
    }

    internal static Func<string, double> Measure(PixelFont font) => text => PixelText.Layout(text, font).Width;

    /// <summary>
    /// The font a whole text is set in (Phase 8, "one font per name"): the pixel font when it can spell all of it, else Fusion 8 px when it
    /// covers all of it (emoji included), else SimSun. Chosen from the whole text, not from the shortened or wrapped piece ("2026年度报告"
    /// becomes "2026…", which the pixel font could spell: the name would change size).
    /// </summary>
    internal static PixelFont FontFor(string text, PixelFont latin)
    {
        string encoded = PixelEmoji.Encode(text);
        if (latin.Covers(latin.Normalize(encoded)))
            return latin;
        return PixelFonts.Fusion.Covers(encoded) ? PixelFonts.Fusion : PixelFonts.System;
    }

    /// <summary>Text laid out in the font <see cref="FontFor"/> picks, with its emoji as stand-in characters.</summary>
    internal static PixelText Layout(string text, PixelFont latin, int maxWidth = int.MaxValue, int maxLines = 1)
    {
        string encoded = PixelEmoji.Encode(text);
        return PixelText.Layout(encoded, FontFor(encoded, latin), maxWidth, maxLines);
    }

    /// <summary>One line, shortened by the shared rule, in the font <see cref="FontFor"/> picks.</summary>
    internal static PixelText OneLine(string text, PixelFont latin, int maxWidth)
    {
        string encoded = PixelEmoji.Encode(text);
        var font = FontFor(encoded, latin);
        return PixelText.Layout(NameShortener.OneLine(encoded, maxWidth, Measure(font)), font);
    }

    public static CardPlan Plan(FolderPixelView view)
    {
        var (width, height) = PixelCard.ArtSize(view.Size);
        int columns = FolderCards.Columns(view.Size);
        var xs = view.Size == WidgetSize.Small ? SmallColumns : WideColumns;
        bool missing = !view.Exists, empty = view.Exists && view.Items.Count == 0;
        var cells = FolderCards.Layout(view.Size, view.Exists ? view.Items.Count : 0);

        var names = cells.Shown == 0 ? [] : view.Items.Take(cells.Shown).Select(i => new[] { OneLine(i.DisplayName, PixelFonts.Small, CellWidth) }).ToArray();
        var stackLabel = cells.HasStack ? Layout("+" + cells.StackMore, PixelFonts.Small) : null;
        var count = Layout(missing ? "—" : view.Items.Count.ToString(), PixelFonts.Regular);
        var title = OneLine(view.Name, PixelFonts.Regular, width - 14 - count.Width - 6);
        // One metric set per card: that of the highest-ranked text, the header or any visible name (pixel font < Fusion < SimSun).
        int rank = title.Rank;
        if (!empty && !missing)
            rank = Math.Max(rank, Math.Max(names.Select(n => n[0].Rank).DefaultIfEmpty(0).Max(), stackLabel?.Rank ?? 0));

        int headerY, headerHeight, nameGap;
        int[] rows;
        if (height == 82)
        {
            (headerY, headerHeight, nameGap) = rank switch { 2 => (5, 12, 1), 1 => (7, 9, 1), _ => (7, 7, 2) };
            rows = rank switch { 2 => [19, 48], 1 => [19, 49], _ => [19, 52] };
        }
        else
        {
            (headerY, headerHeight, nameGap) = rank switch { 2 => (7, 12, 1), 1 => (7, 9, 1), _ => (7, 7, 2) };
            rows = rank switch { 2 => [22, 63, 105, 146], 1 => [19, 59, 99, 139], _ => [19, 61, 103, 145] };
        }
        int nameHeight = rank switch { 2 => 12, 1 => 9, _ => 5 };
        // Phase 11: a Large card's names take two lines like the panel's (the same breaks); a name that fits stays one line, top-aligned.
        bool twoLines = view.Size == WidgetSize.Large && rank < 2;
        if (twoLines)
            names = view.Items.Take(cells.Shown).Select(i => FolderPixelPanelScene.NameLines(i.DisplayName).ToArray()).ToArray();
        int cellHeight = 16 + nameGap + nameHeight + (twoLines ? rank switch { 1 => 10, _ => 7 } : 0);

        var regions = new List<(int, int, int, int, FolderHit)>();
        if (view.Exists)
            regions.Add((3, headerY - 2, width - 6, headerHeight + 4, new FolderHit(FolderHitKind.Header)));
        int total = cells.HasStack ? cells.Shown + 1 : cells.Shown;
        for (int cell = 0; cell < total; cell++)
        {
            int x = xs[cell % columns], y = rows[cell / columns];
            var hit = cells.HasStack && cell == cells.Shown ? new FolderHit(FolderHitKind.Stack) : new FolderHit(FolderHitKind.Item, cell);
            regions.Add((x - 1, y - 2, CellWidth + 2, cellHeight + 4, hit));
        }
        return new CardPlan
        {
            Width = width, Height = height, Rank = rank, HeaderY = headerY, HeaderHeight = headerHeight, NameGap = nameGap, NameHeight = nameHeight, TwoLines = twoLines, CellHeight = cellHeight,
            Columns = xs, Rows = rows, Title = title, Count = count, Names = names, Cells = cells, StackLabel = stackLabel, Regions = regions,
        };
    }

    public static FolderHit HitTest(CardPlan plan, int x, int y)
    {
        foreach (var (rx, ry, rw, rh, hit) in plan.Regions)
        {
            if (x >= rx && x < rx + rw && y >= ry && y < ry + rh)
                return hit;
        }
        return FolderHit.None;
    }

    public static void Draw(PixelCanvas canvas, PixelPalette p, FolderPixelView view, CardPlan plan)
    {
        PixelCard.Draw(canvas, p);
        int width = plan.Width, height = plan.Height;
        bool missing = !view.Exists, empty = view.Exists && view.Items.Count == 0;
        var keep = new List<(int X, int Y, int W, int H)>(); // boxes the drop tint must not touch: every icon and every text, grown by 1

        // A3: the header's plate and its words go 1 px up on hover and 1 px down on a press; a completed click flashes it once.
        bool headerFlash = view.Flash.Kind == FolderHitKind.Header;
        bool headerPressed = view.Pressed.Kind == FolderHitKind.Header, headerHover = view.Hover.Kind == FolderHitKind.Header;
        int headerDy = headerPressed ? 1 : headerHover ? -1 : 0;
        if (headerFlash)
            FillRound(canvas, 3, plan.HeaderY - 2, width - 6, plan.HeaderHeight + 4, p.Select);
        else if (headerHover || headerPressed)
            FillRound(canvas, 3, plan.HeaderY - 2, width - 6, plan.HeaderHeight + 4, headerPressed ? p.Shade : p.OpenFill);
        plan.Title.Draw(canvas, 7, plan.HeaderY + (plan.HeaderHeight - plan.Title.Height) / 2 + headerDy, headerFlash ? p.OnSelect : p.Ink);
        plan.Count.Draw(canvas, width - 7 - plan.Count.Width, plan.HeaderY + (plan.HeaderHeight - plan.Count.Height) / 2 + headerDy, headerFlash ? p.OnSelect : p.Soft);
        keep.Add((6, plan.HeaderY + (plan.HeaderHeight - plan.Title.Height) / 2 - 1, plan.Title.Width + 2, plan.Title.Height + 2));
        keep.Add((width - 8 - plan.Count.Width, plan.HeaderY + (plan.HeaderHeight - plan.Count.Height) / 2 - 1, plan.Count.Width + 2, plan.Count.Height + 2));
        int top = plan.HeaderY + plan.HeaderHeight;

        if (missing)
        {
            DrawMissing(canvas, p, view, plan, top);
            return;
        }
        if (empty)
        {
            DrawEmpty(canvas, p, plan, top, keep);
        }
        else
        {
            DrawCells(canvas, p, view, plan, keep);
        }
        if (view.Drop is { } verb && view.Exists && view.DropPhase > 0)
            DrawDrop(canvas, p, DropLabel(verb, view.Name), keep, view.DropPhase);
    }

    public static string DropLabel(DropVerb verb, string folder) => verb switch
    {
        DropVerb.Move => Strings.FolderDropMove(folder),
        DropVerb.Copy => Strings.FolderDropCopy(folder),
        DropVerb.Shortcut => Strings.FolderDropShortcut(folder),
        _ => Strings.FolderDropMixed(folder),
    };

    private static void DrawCells(PixelCanvas canvas, PixelPalette p, FolderPixelView view, CardPlan plan, List<(int X, int Y, int W, int H)> keep)
    {
        var cells = plan.Cells;
        int columns = cells.Columns;
        int total = cells.HasStack ? cells.Shown + 1 : cells.Shown;
        var motion = view.Motion;
        // The cells that left stay a moment longer, at half their pixels, then a quarter (LEAVE), under the ones that came.
        if (motion is not null && motion.Frame < ListMotion.Frames)
        {
            int minBayer = motion.Frame == 1 ? 8 : 12;
            foreach (var gone in motion.Leaving)
                PixelMotion.BlitMasked(gone.Pixels, canvas, gone.X, gone.Y, minBayer);
        }
        for (int i = 0; i < total; i++)
        {
            int x = plan.Columns[i % columns], y = plan.Rows[i / columns];
            bool isStack = cells.HasStack && i == cells.Shown;
            var hit = isStack ? new FolderHit(FolderHitKind.Stack) : new FolderHit(FolderHitKind.Item, i);
            bool pressed = view.Pressed == hit, hover = view.Hover == hit, flash = view.Flash == hit;
            // A3: 1 px up on hover, 1 px down (and the Shade plate) on a press; a completed click flashes once.
            int dy = pressed ? 1 : hover ? -1 : 0;
            int plateX = x - 1;
            if (!isStack && motion is not null)
            {
                var (effect, moveDx) = motion.Of(i);
                if (effect == CellEffect.Move)
                {
                    x += motion.MoveOffset(moveDx);
                    plateX = x - 1;
                }
                else if (effect == CellEffect.Pop && motion.Frame == 1)
                {
                    // POP, frame 1: the item's mini tile, centred on where its icon will be.
                    var mini = PixelFolderIcons.Mini(p, view.Icons.Icon(p, view.Items[i]));
                    Blit(mini, canvas, x + 15, y + 5);
                    continue;
                }
                else if (effect == CellEffect.Pop && motion.Frame == 2)
                {
                    dy -= 1; // the full cell, 1 px above its place
                }
            }
            if (flash)
                FillRound(canvas, plateX, y - 2, CellWidth + 2, plan.CellHeight + 4, p.Select);
            else if (hover || pressed)
                FillRound(canvas, plateX, y - 2, CellWidth + 2, plan.CellHeight + 4, pressed ? p.Shade : p.OpenFill);
            uint nameColor(FolderItem item) => flash ? p.OnSelect : item.TargetMissing ? p.Faint : p.Ink;
            if (!isStack)
            {
                var item = view.Items[i];
                var icon = view.Icons.Icon(p, item);
                Blit(item.TargetMissing ? PixelFolderIcons.Dimmed(p, icon) : icon, canvas, x + 10, y + dy);
                keep.Add((x + 9, y + dy - 1, 18, 18));
                int lineY = y + 16 + plan.NameGap + dy;
                foreach (var line in plan.Names[i])
                {
                    int lineX = x + (CellWidth - line.Width) / 2, top = plan.TwoLines ? lineY : y + 16 + plan.NameGap + (plan.NameHeight - line.Height) / 2 + dy;
                    line.Draw(canvas, lineX, top, nameColor(item));
                    keep.Add((lineX - 1, top - 1, line.Width + 2, line.Height + 2));
                    lineY += LinePitch(line);
                }
            }
            else
            {
                for (int k = 0; k < cells.Minis.Length; k++)
                    Blit(PixelFolderIcons.Mini(p, view.Icons.Icon(p, view.Items[cells.Minis[k]])), canvas, x + 10 + k % 2 * 9, y + k / 2 * 9 + dy);
                var label = plan.StackLabel!;
                int labelX = x + (CellWidth - label.Width) / 2;
                int labelY = y + 16 + plan.NameGap + (plan.TwoLines ? 0 : (plan.NameHeight - label.Height) / 2) + dy; // "+K" is line 1
                label.Draw(canvas, labelX, labelY, flash ? p.OnSelect : p.Ink);
                keep.Add((x + 9, y + dy - 1, 18, 18));
                keep.Add((labelX - 1, labelY - 1, label.Width + 2, label.Height + 2));
            }
        }
    }

    /// <summary>The step from one name line to the next: each font keeps its own pitch (5 + 2 for the pixel font, 9 + 1 for Fusion, 12 + 1 for SimSun).</summary>
    private static int LinePitch(PixelText line) => line.Rank switch { 2 => 13, 1 => 10, _ => 7 };

    private static void DrawEmpty(PixelCanvas canvas, PixelPalette p, CardPlan plan, int top, List<(int X, int Y, int W, int H)> keep)
    {
        int x0 = 7, y0 = top + 5, x1 = plan.Width - 8, y1 = plan.Height - 8;
        DottedRect(canvas, x0, y0, x1, y1, p.Faint);
        var text = Layout(Strings.FolderEmpty, PixelFonts.Small, x1 - x0 - 8, 2);
        int block = 7 + 4 + text.Height;
        int y = y0 + (y1 - y0 - block) / 2 + 1;
        canvas.Draw(DropArrow, (plan.Width - 7) / 2, y, _ => p.Soft);
        text.Draw(canvas, x0, y + 11, p.Soft, PixelAlign.Center, x1 - x0 + 1);
        keep.Add(((plan.Width - 7) / 2 - 1, y - 1, 9, 9));
        keep.Add((x0 + (x1 - x0 + 1 - text.Width) / 2 - 1, y + 10, text.Width + 2, text.Height + 2));
    }

    private static void DrawMissing(PixelCanvas canvas, PixelPalette p, FolderPixelView view, CardPlan plan, int top)
    {
        var text = Layout(Strings.FolderMissing, PixelFonts.Small, plan.Width - 16, 2);
        int block = 12 + 5 + text.Height;
        int y = top + (plan.Height - 4 - top - block) / 2;
        canvas.Draw(PixelIcons.Warning, (plan.Width - 13) / 2, y, ch => ch switch { '+' => p.Yellow, 'x' => p.SpriteInk, _ => p.Outline });
        text.Draw(canvas, 8, y + 17, p.Ink, PixelAlign.Center, plan.Width - 16);
        if (view.Size == WidgetSize.Large)
        {
            var pathFont = FontFor(view.Path, PixelFonts.Small);
            var path = PixelText.Layout(NameShortener.Path(PixelEmoji.Encode(view.Path), plan.Width - 16, Measure(pathFont)), pathFont);
            path.Draw(canvas, 8, y + 17 + text.Height + 6, p.Soft, PixelAlign.Center, plan.Width - 16);
        }
    }

    /// <summary>Drop hover: every other panel-coloured pixel turns Select (icons and names stay crisp), then a Select plate with the label.</summary>
    private static void DrawDrop(PixelCanvas canvas, PixelPalette p, string label, List<(int X, int Y, int W, int H)> keep, int phase)
    {
        // A2a: phase 1 is the tint at 1 dot in 16 with no label, phase 2 the tint as it is (1 in 4) with the label plate.
        int step = phase == 1 ? 4 : 2;
        for (int y = 2; y < canvas.Height - 3; y += step)
        {
            for (int x = 2; x < canvas.Width - 2; x += step)
            {
                if (canvas.Get(x, y) == p.Panel && !keep.Any(k => x >= k.X && x < k.X + k.W && y >= k.Y && y < k.Y + k.H))
                    canvas.Set(x, y, p.Select);
            }
        }
        if (phase < 2)
            return;
        var text = Layout(label, PixelFonts.Small, canvas.Width - 24, 2);
        int plateWidth = text.Width + 12, plateHeight = text.Height + 9;
        int px = (canvas.Width - plateWidth) / 2, py = (canvas.Height - plateHeight) / 2;
        PixelFrame.Draw(canvas, px, py, plateWidth, plateHeight, [1], p.SelectPressed, p.Select, p.SelectLight, p.SelectPressed, 1);
        text.Draw(canvas, px + 6, py + 4, p.OnSelect, PixelAlign.Center, text.Width);
    }

    /// <summary>A rectangle with its four corner pixels cut.</summary>
    internal static void FillRound(PixelCanvas canvas, int x, int y, int width, int height, uint color)
    {
        canvas.Fill(x + 1, y, width - 2, height, color);
        canvas.Fill(x, y + 1, 1, height - 2, color);
        canvas.Fill(x + width - 1, y + 1, 1, height - 2, color);
    }

    internal static void DottedRect(PixelCanvas canvas, int x0, int y0, int x1, int y1, uint color)
    {
        for (int x = x0 + 2; x <= x1 - 2; x += 2)
        {
            canvas.Set(x, y0, color);
            canvas.Set(x, y1, color);
        }
        for (int y = y0 + 2; y <= y1 - 2; y += 2)
        {
            canvas.Set(x0, y, color);
            canvas.Set(x1, y, color);
        }
        canvas.Set(x0 + 1, y0 + 1, color);
        canvas.Set(x1 - 1, y0 + 1, color);
        canvas.Set(x0 + 1, y1 - 1, color);
        canvas.Set(x1 - 1, y1 - 1, color);
    }

    internal static void Blit(PixelCanvas source, PixelCanvas target, int x, int y)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            for (int sx = 0; sx < source.Width; sx++)
            {
                uint value = source.Get(sx, sy);
                if (value != 0)
                    target.Set(x + sx, y + sy, value);
            }
        }
    }
}
