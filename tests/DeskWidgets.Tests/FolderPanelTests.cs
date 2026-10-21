using DeskWidgets.Interop;
using DeskWidgets.Pixel;
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
        => new("Projects", @"D:\Work\Projects", items, search, sort, Selection(items.Count), -1, -1, 0, false, false, null, new PixelFolderIcons(() => { }));

    [Fact]
    public void ThePlan_ShowsFourRowsInLatinAndTwoInTheSimSunSet()
    {
        var items = Enumerable.Range(0, 23).Select(i => Item("file" + i)).ToList();
        var latin = FolderPixelPanelScene.Plan(View(items), simSun: false);
        Assert.Equal(4, latin.VisibleRows);
        Assert.Equal(5, latin.TotalRows);
        var simSun = FolderPixelPanelScene.Plan(View(items), simSun: true);
        Assert.Equal(2, simSun.VisibleRows);
        Assert.True(FolderPixelPanelScene.NeedsSimSun("项目", "", FolderSort.Newest, items));
        Assert.False(FolderPixelPanelScene.NeedsSimSun("Projects", "", FolderSort.Newest, items));
    }

    [Fact]
    public void HitTest_FindsTheCell_TheSortButtonAndTheSearchField()
    {
        var items = Enumerable.Range(0, 23).Select(i => Item("file" + i)).ToList();
        var plan = FolderPixelPanelScene.Plan(View(items), simSun: false);
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
        var canvas = FolderPixelPanelScene.Draw(palette, view, FolderPixelPanelScene.Plan(view, false));
        Assert.Equal((220, 196), (canvas.Width, canvas.Height));
        Assert.Equal(0u, canvas.Get(0, 0));
        Assert.Equal(palette.Outline, canvas.Get(4, 0));
    }
}
