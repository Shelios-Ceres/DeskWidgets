using DeskWidgets.Interop;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Tests;

public class PixelMenuTests
{
    private static readonly Action None = () => { };

    private static List<MenuEntry> Radios(int count) => Enumerable.Range(0, count).Select(i => MenuEntry.Radio("City " + i, i == 7, None)).ToList();

    private static MenuEntry Disabled(string text) => new() { Text = text, Kind = MenuKind.Item, Enabled = false };

    [Fact]
    public void Navigation_SkipsSeparatorsStatusLinesAndDisabledRows_AndWraps()
    {
        MenuEntry[] items =
        [
            MenuEntry.Item("a", None),
            MenuEntry.Separator(),
            MenuEntry.Status("status"),
            Disabled("off"),
            MenuEntry.Item("b", None),
        ];

        Assert.Equal(0, MenuNav.First(items));
        Assert.Equal(4, MenuNav.Last(items));
        Assert.Equal(4, MenuNav.Step(items, 0, +1));
        Assert.Equal(0, MenuNav.Step(items, 4, +1));
        Assert.Equal(4, MenuNav.Step(items, 0, -1));
        Assert.Equal(0, MenuNav.Step(items, -1, +1));
        Assert.Equal(4, MenuNav.Step(items, -1, -1));
    }

    [Fact]
    public void ASubmenuOpensOnItsCheckedRadioRow_OtherwiseItsFirstRow()
    {
        Assert.Equal(7, MenuNav.Entry(Radios(26)));
        Assert.Equal(1, MenuNav.Entry([MenuEntry.Separator(), MenuEntry.Item("a", None)]));
        Assert.Equal(-1, MenuNav.Entry([MenuEntry.Separator()]));
    }

    [Fact]
    public void Rows_AreFifteenArtPixelsHigh_AndThePanelHasTheDesignedInsets()
    {
        var layout = MenuLayout.Create([MenuEntry.Item("Refresh devices", None), MenuEntry.Separator(), MenuEntry.Item("Exit", None)]);

        Assert.Equal(15, layout.RowHeight);
        Assert.Equal(new[] { 15, 7, 15 }, layout.Heights);
        Assert.Equal(3 + 15 + 7 + 15 + 4, layout.ContentHeight);
    }

    [Fact]
    public void APanelWithChineseText_UsesTheTallerRowForEveryRow()
    {
        var layout = MenuLayout.Create([MenuEntry.Item("Exit", None), MenuEntry.Item("退出", None)]);

        Assert.Equal(18, layout.RowHeight);
        Assert.Equal(new[] { 18, 18 }, layout.Heights);
    }

    [Fact]
    public void Width_IsTheLabelColumnPlusTheWidestLabelPlusRoomForAnArrow_AtLeastSixtyFour()
    {
        var plain = MenuLayout.Create([MenuEntry.Item("Lock position", None)]);
        var withSubmenu = MenuLayout.Create([MenuEntry.Item("Lock position", None), MenuEntry.Sub("Theme", [MenuEntry.Item("x", None)])]);
        var tiny = MenuLayout.Create([MenuEntry.Item("OK", None)]);
        int label = PixelText.Layout("Lock position", PixelFonts.Regular).Width;

        Assert.Equal(14 + label + 8, plain.Width);
        Assert.Equal(14 + label + 16, withSubmenu.Width);
        Assert.Equal(64, tiny.Width);
    }

    [Fact]
    public void TheCityList_IsThreeHundredNinetySevenArtPixelsTall_AndScrollsOnAShortScreen()
    {
        var layout = MenuLayout.Create(Radios(26));

        Assert.Equal(397, layout.ContentHeight);
        Assert.Equal((397, false), layout.Plan(400));

        var (height, scrollable) = layout.Plan(200);
        Assert.True(scrollable);
        Assert.Equal(3 + 9 + 11 * 15 + 9 + 4, height); // eleven whole rows between two scroll rows
        Assert.Equal((0, 10), layout.Visible(0, height, true));
        Assert.Equal((5, 15), layout.Visible(5, height, true));
        Assert.Equal(15, layout.MaxFirstRow(height)); // the last page shows rows 15 to 25
    }

    [Fact]
    public void ScrollingToARow_MovesTheListAsLittleAsPossible()
    {
        var layout = MenuLayout.Create(Radios(26));
        var (height, _) = layout.Plan(200);

        Assert.Equal(0, layout.FirstRowFor(4, 0, height));   // already visible
        Assert.Equal(10, layout.FirstRowFor(20, 0, height)); // the last row of the page
        Assert.Equal(2, layout.FirstRowFor(2, 9, height));   // above the page: that row becomes the first
    }

    [Fact]
    public void ARootMenu_OpensAtTheAnchor_FlippingLeftAndUpToStayOnTheScreen()
    {
        var work = new Win32.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1500 };

        Assert.Equal((100, 100), MenuPlacement.Place(100, 100, 300, 400, work, preferAbove: false));
        Assert.Equal((2200, 100), MenuPlacement.Place(2500, 100, 300, 400, work, preferAbove: false));
        Assert.Equal((100, 1000), MenuPlacement.Place(100, 1400, 300, 400, work, preferAbove: false));
        Assert.Equal((2200, 1000), MenuPlacement.Place(2500, 1400, 300, 400, work, preferAbove: false));
    }

    [Fact]
    public void ASubmenu_SharesTheParentEdgeColumn_AndLinesUpItsFirstRow()
    {
        var work = new Win32.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1500 };

        // Factor 3: the parent is 300 px wide at x 100, its row starts at y 400; the child is 200 x 300.
        Assert.Equal((100 + 300 - 3, 400 - 3 * 3), MenuPlacement.PlaceSubmenu(100, 300, 400, 200, 300, work, 3));
        // No room on the right: open to the left, the outlines sharing a column the same way.
        Assert.Equal((2300 - 200 + 3, 400 - 9), MenuPlacement.PlaceSubmenu(2300, 300, 400, 200, 300, work, 3));
        // Near the bottom: pushed up inside the work area; near the top: pushed down.
        Assert.Equal(1200, MenuPlacement.PlaceSubmenu(100, 300, 1400, 200, 300, work, 3).Y);
        Assert.Equal(0, MenuPlacement.PlaceSubmenu(100, 300, 5, 200, 300, work, 3).Y);
    }

    [Fact]
    public void ADialogFocus_StartsOnTheDefaultButton_AndWrapsAround()
    {
        var focus = new DialogFocus(2, defaultButton: 1);

        Assert.Equal(1, focus.Index);
        focus.Move(+1);
        Assert.Equal(0, focus.Index);
        focus.Move(-1);
        Assert.Equal(1, focus.Index);
        focus.Set(9);
        Assert.Equal(1, focus.Index);
    }

    [Fact]
    public void ADialog_IsAsWideAsTheLargeCard_AndItsButtonsAreAtLeastThirtySixWide()
    {
        var layout = DialogLayout.Create("Desk Widgets", "Quit Desk Widgets?", "The widgets will close until you start the app again.",
            [new DialogButtonSpec("Yes", false), new DialogButtonSpec("No", true)]);

        Assert.Equal(DialogLayout.Width, PixelCard.ArtSize(DeskWidgets.Widgets.WidgetSize.Large).Width);
        Assert.All(layout.Buttons, b => Assert.True(b.W >= 36));
        Assert.All(layout.Buttons, b => Assert.Equal(17, b.H)); // a 7-pixel label plus 10
        Assert.Equal(layout.Buttons[1].X, layout.Buttons[0].X + layout.Buttons[0].W + DialogLayout.ButtonGap); // six apart
        Assert.Equal(DialogLayout.Width - DialogLayout.Pad, layout.Buttons[1].X + layout.Buttons[1].W);        // right-aligned
    }

    [Fact]
    public void ClosingPunctuation_NeverStartsALineOfChinese()
    {
        const string text = "没能设置开机自动启动：拒绝访问。所有小组件都会关闭，直到你再次启动本程序。";
        for (int width = 40; width <= 140; width++)
        {
            var laid = PixelText.Layout(text, PixelFonts.Regular, width, 6);
            foreach (string line in laid.Lines)
                Assert.DoesNotContain(line[0], "，。、：；！？）」』》");
        }
    }

    [Fact]
    public void MenuColours_ArePinnedToTheDesignedRoles()
    {
        var light = PixelPalette.For(false);
        var dark = PixelPalette.For(true);

        Assert.Equal(0xFF3B5DC9u, light.Select);
        Assert.Equal(0xFF29366Fu, light.SelectPressed);
        Assert.Equal(0xFF41A6F6u, light.SelectLight);
        Assert.Equal(0xFFF4F4F4u, light.OnSelect);
        Assert.Equal((0xFFD5DFE6u, 0xFF333C57u), (light.OpenFill, dark.OpenFill));
        Assert.Equal((0xFFFFFFFFu, 0xFF333C57u), (light.Button, dark.Button));
        Assert.Equal((0xFFFFFFFFu, 0xFF566C86u), (light.ButtonTop, dark.ButtonTop));
        Assert.Equal((0xFFE3E9EDu, 0xFF566C86u), (light.ButtonHover, dark.ButtonHover));
        Assert.Equal((0xFFC5D3DCu, 0xFF1A1C2Cu), (light.ButtonPressed, dark.ButtonPressed));
        Assert.Equal((0x661A1C2Cu, 0x99000000u), (light.Shadow, dark.Shadow));
        Assert.Equal((dark.Select, dark.SelectPressed, dark.SelectLight, dark.OnSelect), (light.Select, light.SelectPressed, light.SelectLight, light.OnSelect));
    }

    [Fact]
    public void ThePainter_FillsTheCurrentRowFromTwoPixelsInToTwoPixelsShortOfTheRightEdge()
    {
        var p = PixelPalette.For(false);
        var layout = MenuLayout.Create([MenuEntry.Item("One", None), MenuEntry.Item("Two", None)]);
        var drawing = MenuPainter.Draw(p, layout, new MenuPaint(layout.ContentHeight, false, 0, 1, -1, false));
        var row = drawing.Rows[1];

        Assert.Equal(layout.Width, drawing.Panel.Width);
        Assert.Equal(layout.ContentHeight, drawing.Panel.Height);
        Assert.Equal(p.Select, drawing.Panel.Get(2, row.Y));
        Assert.Equal(p.Select, drawing.Panel.Get(layout.Width - 3, row.Y + row.Height - 1));
        Assert.NotEqual(p.Select, drawing.Panel.Get(1, row.Y));
        Assert.NotEqual(p.Select, drawing.Panel.Get(2, drawing.Rows[0].Y));
    }

    [Fact]
    public void AParentRowWhoseSubmenuHasTheFocus_ShowsTheOpenFillInsteadOfTheSelectBar()
    {
        var p = PixelPalette.For(true);
        var layout = MenuLayout.Create([MenuEntry.Sub("Theme", [MenuEntry.Item("x", None)])]);
        var drawing = MenuPainter.Draw(p, layout, new MenuPaint(layout.ContentHeight, false, 0, 0, -1, ChildFocused: true));

        Assert.Equal(p.OpenFill, drawing.Panel.Get(2, drawing.Rows[0].Y));
    }

    [Fact]
    public void TheShadow_FollowsThePanelShapeTwoPixelsDownAndRight()
    {
        var p = PixelPalette.For(false);
        var panel = new PixelCanvas(10, 10);
        PixelFrame.Draw(panel, 0, 0, 10, 10, PixelFrame.MenuCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);

        var composed = PixelFrame.WithShadow(panel, p.Shadow);

        Assert.Equal(12, composed.Width);
        Assert.Equal(p.Shadow, composed.Get(10, 10));      // two pixels down and right of the panel's bottom-right area
        Assert.Equal(0u, composed.Get(11, 0));             // above the shadow's top: transparent
        Assert.Equal(panel.Get(5, 5), composed.Get(5, 5)); // the panel itself is on top
    }
}
