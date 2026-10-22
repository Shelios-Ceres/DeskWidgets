using DeskWidgets.Interop;
using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderPanelTests
{
    private static FolderItem Item(string name) => new()
    {
        Path = @"D:\Cards\Projects\" + name,
        DisplayName = name,
        Kind = FolderItemKind.Other,
        AddedUtc = new DateTime(2026, 10, 21),
    };

    private static PanelSelection Selection(int count)
    {
        var selection = new PanelSelection();
        selection.SetView(Enumerable.Range(0, count).Select(i => Item("file" + i)).ToList());
        return selection;
    }

    private static int[] Selected(PanelSelection selection, int count) => Enumerable.Range(0, count).Where(selection.IsSelected).ToArray();

    // ----- LineEditor -----

    [Fact]
    public void TypingReplacesASelection_AndBackspaceRemovesOneCharacter()
    {
        var editor = new LineEditor("report", selectAll: true);
        Assert.True(editor.AllSelected);
        editor.Insert("x");
        Assert.Equal("x", editor.Text);
        editor.Insert("yz");
        Assert.Equal("xyz", editor.Text);
        editor.Backspace();
        Assert.Equal("xy", editor.Text);
        editor.Left();
        editor.Insert("-");
        Assert.Equal("x-y", editor.Text);
        Assert.Equal(2, editor.Caret);
    }

    [Fact]
    public void TheCaret_NeverLandsInsideASurrogatePair()
    {
        var editor = new LineEditor("a😀b");
        editor.Left();
        editor.Left();
        Assert.Equal(1, editor.Caret);
        editor.Right();
        Assert.Equal(3, editor.Caret); // over the whole emoji
        editor.Backspace();
        Assert.Equal("ab", editor.Text);
    }

    [Fact]
    public void BackspaceOnASelection_ClearsTheField_AndNothingMovesPastTheEnds()
    {
        var editor = new LineEditor("abc", selectAll: true);
        editor.Backspace();
        Assert.Equal("", editor.Text);
        Assert.False(editor.Backspace());
        Assert.False(editor.Delete());
        editor.Insert("q");
        editor.Home();
        Assert.True(editor.Delete());
        Assert.Equal("", editor.Text);
    }

    [Fact]
    public void ReplacingARange_PutsTheCaretAfterTheNewText_AndKeepsAnEmojiWhole()
    {
        var editor = new LineEditor("ni'hao", selectAll: true);
        editor.Replace(0, 6, "你好");
        Assert.Equal("你好", editor.Text);
        Assert.Equal(2, editor.Caret);
        Assert.False(editor.AllSelected);
        editor.Replace(2, 2, "😀");
        Assert.Equal("你好😀", editor.Text);
        Assert.Equal(4, editor.Caret);
        editor.Backspace();
        Assert.Equal("你好", editor.Text);
    }

    [Fact]
    public void ASelectedRange_IsOnlyDrawnWhenItIsTheWholeText()
    {
        var editor = new LineEditor("abc");
        editor.SelectRange(0, 3);
        Assert.True(editor.AllSelected);
        Assert.Equal((0, 3), editor.Selection);
        editor.SelectRange(1, 1);
        Assert.False(editor.AllSelected);
        Assert.Equal((1, 1), editor.Selection);
        editor.SelectRange(0, 99);
        Assert.Equal(3, editor.Caret);
    }

    // ----- PanelSelection -----

    [Fact]
    public void AClick_SelectsOne_CtrlToggles_ShiftExtendsFromTheAnchor()
    {
        var selection = Selection(10);
        selection.Press(2, ctrl: false, shift: false);
        Assert.Equal(1, selection.Count);
        selection.Press(4, ctrl: true, shift: false);
        Assert.Equal(2, selection.Count);
        selection.Press(4, ctrl: true, shift: false);
        Assert.Equal(1, selection.Count);
        selection.Press(6, ctrl: false, shift: true); // the anchor is 4
        Assert.Equal([4, 5, 6], Selected(selection, 10));
    }

    [Fact]
    public void PressingASelectedCellOfSeveral_KeepsThemUntilTheButtonComesUpWithoutADrag()
    {
        var selection = Selection(6);
        selection.SelectAll();
        bool collapse = selection.Press(3, ctrl: false, shift: false);
        Assert.True(collapse);
        Assert.Equal(6, selection.Count); // still all of them: a drag would take the lot
        selection.SelectOnly(3);
        Assert.Equal([3], Selected(selection, 6));
    }

    [Fact]
    public void ArrowKeysMoveTheFocusAndTheSelectionWithIt_UnlessCtrlHoldsIt()
    {
        var selection = Selection(12);
        Assert.Equal(0, selection.MoveFocus(1, extend: false, keepSelection: false));
        Assert.Equal(5, selection.MoveFocus(5, extend: false, keepSelection: false));
        Assert.Equal([5], Selected(selection, 12));
        selection.MoveFocus(1, extend: false, keepSelection: true);
        Assert.Equal(6, selection.Focus);
        Assert.Equal([5], Selected(selection, 12));
        selection.MoveFocus(5, extend: true, keepSelection: false); // Shift extends from the anchor, which is still 5
        Assert.Equal(11, selection.Focus);
        Assert.Equal([5, 6, 7, 8, 9, 10, 11], Selected(selection, 12));
    }

    [Fact]
    public void FocusAndSelection_StayInsideAShorterList()
    {
        var selection = Selection(10);
        selection.SelectOnly(8);
        selection.SetView(Enumerable.Range(0, 4).Select(i => Item("file" + i)).ToList());
        Assert.Equal(0, selection.Count);
        Assert.Equal(3, selection.Focus);
        Assert.Equal(-1, new PanelSelection().MoveFocus(1, false, false));
    }

    [Theory]
    [InlineData(0, 0, 4, 0)]
    [InlineData(19, 0, 4, 0)]   // row 3 is still in sight with 4 rows
    [InlineData(20, 0, 4, 1)]   // row 4 scrolls down just enough
    [InlineData(5, 3, 4, 1)]    // row 1 is above the window: it becomes the first row
    [InlineData(44, 0, 2, 7)]
    public void Scrolling_ShowsJustTheRowOfTheFocus(int index, int firstRow, int visible, int expected)
        => Assert.Equal(expected, PanelSelection.FirstRowFor(index, 5, firstRow, visible));

    // ----- placement -----

    private static Win32.RECT Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [Fact]
    public void ThePanel_OpensToTheRightOfTheCard_ElseToTheLeft_ElseOverIt()
    {
        var work = Rect(0, 0, 2560, 1560);
        var card = Rect(100, 200, 100 + 516, 200 + 246);
        Assert.Equal((100 + 516 + 12, 200), FolderPanelPlacement.Place(card, work, 666, 594, 12));

        var rightEdge = Rect(2560 - 516 - 40, 200, 2560 - 40, 446);
        var (x, _) = FolderPanelPlacement.Place(rightEdge, work, 666, 594, 12);
        Assert.Equal(rightEdge.Left - 12 - 666, x);

        var narrow = Rect(0, 0, 900, 700);
        var wide = Rect(100, 100, 700, 346);
        var (centred, _) = FolderPanelPlacement.Place(wide, narrow, 666, 594, 12);
        Assert.InRange(centred, 0, 900 - 666);
    }

    [Fact]
    public void ThePanel_MovesUpWhenItWouldRunOffTheBottom()
    {
        var work = Rect(0, 0, 2560, 1560);
        var card = Rect(100, 1400, 616, 1646);
        var (_, y) = FolderPanelPlacement.Place(card, work, 666, 594, 12);
        Assert.Equal(1560 - 594, y);
    }

    // ----- the scene -----

    private static FolderPanelView View(IReadOnlyList<FolderItem> items, FolderSort sort = FolderSort.Newest, string search = "")
        => new("Projects", @"D:\Work\Projects", items, search, sort, items.Count, Selection(items.Count), -1, -1, 0, false, false, null, new PixelFolderIcons(() => { }));

    [Fact]
    public void ThePlan_ShowsFourRowsInLatin_ThreeInFusionAndTwoInTheSimSunSet()
    {
        var items = Enumerable.Range(0, 23).Select(i => Item("file" + i)).ToList();
        var latin = FolderPixelPanelScene.Plan(View(items), rank: 0);
        Assert.Equal(4, latin.VisibleRows);
        Assert.Equal(5, latin.TotalRows);
        var fusion = FolderPixelPanelScene.Plan(View(items), rank: 1);
        Assert.Equal(3, fusion.VisibleRows);
        Assert.Equal((36, 39, 15), (fusion.CellHeight, fusion.Pitch, fusion.FieldHeight)); // 16, 1, 9, 1, 9
        var simSun = FolderPixelPanelScene.Plan(View(items), rank: 2);
        Assert.Equal(2, simSun.VisibleRows);
        Assert.Equal(1, FolderPixelPanelScene.MetricRank("项目", "", FolderSort.Newest, items));
        Assert.Equal(0, FolderPixelPanelScene.MetricRank("Projects", "", FolderSort.Newest, items));
        Assert.Equal(2, FolderPixelPanelScene.MetricRank("Projects", "", FolderSort.Newest, [Item("\U00020000 file")])); // Fusion has no glyph for it
    }

    [Fact]
    public void HitTest_FindsTheCell_TheSortButtonAndTheSearchField()
    {
        var items = Enumerable.Range(0, 23).Select(i => Item("file" + i)).ToList();
        var plan = FolderPixelPanelScene.Plan(View(items), rank: 0);
        var cell = FolderPixelPanelScene.CellRect(plan, 7, 0)!.Value; // row 1, column 2
        var hit = FolderPixelPanelScene.HitTest(plan, items.Count, 0, cell.X + 3, cell.Y + 3);
        Assert.Equal(new PanelHit(PanelHitKind.Cell, 7), hit);
        var sort = FolderPixelPanelScene.SortRect(plan);
        Assert.Equal(PanelHitKind.Sort, FolderPixelPanelScene.HitTest(plan, items.Count, 0, sort.X + 2, sort.Y + 2).Kind);
        Assert.Equal(PanelHitKind.Search, FolderPixelPanelScene.HitTest(plan, items.Count, 0, 20, plan.FieldY + 2).Kind);
        // After scrolling a row, the first visible row holds items 5..9.
        var scrolled = FolderPixelPanelScene.CellRect(plan, 5, 1)!.Value;
        Assert.Equal(new PanelHit(PanelHitKind.Cell, 5), FolderPixelPanelScene.HitTest(plan, items.Count, 1, scrolled.X + 2, scrolled.Y + 2));
        Assert.Null(FolderPixelPanelScene.CellRect(plan, 0, 1)); // scrolled out of sight
    }

    [Fact]
    public void TheDrawnPanel_IsTheDesignsSize_AndItsCornerIsCutByTheStair()
    {
        var items = Enumerable.Range(0, 7).Select(i => Item("file" + i)).ToList();
        var palette = PixelPalette.For(dark: false);
        var view = View(items);
        var canvas = FolderPixelPanelScene.Draw(palette, view, FolderPixelPanelScene.Plan(view, 0));
        Assert.Equal((220, 196), (canvas.Width, canvas.Height));
        Assert.Equal(0u, canvas.Get(0, 0));
        Assert.Equal(palette.Outline, canvas.Get(4, 0));
    }

    // ----- design QA fixes -----

    [Fact]
    public void AChineseNameIsSetInOneFont_EvenWhenAShortenedPieceIsOnlyDigits()
    {
        var card = FolderPixelScene.OneLine("2026年度报告审计结果", PixelFonts.Small, 40);
        Assert.Equal(1, card.Rank); // Fusion 8 px; "2026…" alone could be spelled in the pixel font
        foreach (var line in FolderPixelPanelScene.NameLines("2026年度报告审计结果汇总说明"))
            Assert.Equal(1, line.Rank);
        Assert.Equal(0, FolderPixelScene.OneLine("Quarterly report 2026", PixelFonts.Small, 36).Rank);
        // A name Fusion cannot spell falls back to SimSun as a whole.
        var rare = FolderPixelScene.OneLine("2026年\U00020000度报告", PixelFonts.Small, 60);
        Assert.Equal(2, rare.Rank);
    }

    [Fact]
    public void TheDropTint_LeavesEveryIconAndNameAlone()
    {
        var palette = PixelPalette.For(dark: false);
        var items = new[] { "Budget", "Report", "Notes" }.Select(Item).ToList();
        var icons = new PixelFolderIcons(() => { });
        FolderPixelView View(DropVerb? drop) => new("Projects", @"D:\Work\Projects", items, true, WidgetSize.Medium, FolderHit.None, FolderHit.None, drop, icons);
        PixelCanvas Draw(DropVerb? drop)
        {
            var view = View(drop);
            var plan = FolderPixelScene.Plan(view);
            var canvas = new PixelCanvas(plan.Width, plan.Height);
            FolderPixelScene.Draw(canvas, palette, view, plan);
            return canvas;
        }
        var plain = Draw(null);
        var tinted = Draw(DropVerb.Move);
        // The first cell: the 16 x 16 icon at (15, 19) and its name below it. The label sits in the middle, so look at the left column.
        for (int y = 18; y < 40; y++)
        {
            for (int x = 14; x < 32; x++)
                Assert.Equal(plain.Get(x, y), tinted.Get(x, y));
        }
        Assert.NotEqual(plain.Pixels, tinted.Pixels); // but the card does take the tint elsewhere
    }

    [Fact]
    public void AFileWithNoExtension_GetsThePlainPage_AndOneWithAnExtensionGetsTheTag()
    {
        var palette = PixelPalette.For(dark: false);
        var icons = new PixelFolderIcons(() => { });
        FolderItem Other(string name) => new() { Path = @"D:\Cards\" + name, DisplayName = name, Kind = FolderItemKind.Other, LabelName = name, AddedUtc = DateTime.UtcNow };
        var plain = icons.Icon(palette, Other("README"));
        var tagged = icons.Icon(palette, Other("data.abc"));
        Assert.NotEqual(palette.Sprite('d'), plain.Get(8, 9)); // no dusk band across the page
        Assert.Equal(palette.Sprite('d'), tagged.Get(1, 9));
    }

    [Fact]
    public void TheRenameField_IsTheCellsWidth()
    {
        var palette = PixelPalette.For(dark: false);
        var items = Enumerable.Range(0, 10).Select(i => Item("name" + i)).ToList();
        var editor = new LineEditor("a very long file name indeed.docx", selectAll: false);
        var view = new FolderPanelView("Projects", @"D:\Work\Projects", items, "", FolderSort.Newest, items.Count, Selection(items.Count), -1, -1, 0, false, false,
            new PanelEdit(PanelEditTarget.Item, editor, 1), new PixelFolderIcons(() => { }));
        var plan = FolderPixelPanelScene.Plan(view, rank: 0);
        var canvas = FolderPixelPanelScene.Draw(palette, view, plan);
        var cell = FolderPixelPanelScene.CellRect(plan, 1, 0)!.Value;
        // Right of the field, in the gap before the next cell, nothing of the editor is drawn: the panel's own colour is still there.
        int gapX = cell.X + cell.W + 1;
        Assert.Equal(palette.Panel, canvas.Get(gapX, cell.Y + 2 + 16 + 1 + 5));
    }
}
