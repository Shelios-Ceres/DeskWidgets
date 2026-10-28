namespace DeskWidgets.Tests;

using DeskWidgets.Desktop;

public class DesktopDragWatcherTests
{
    private static DesktopDragWatcher Watcher() => new(4);

    [Fact]
    public void WithTheButtonsSwapped_TheRightPhysicalButtonIsTheOneThatDrags()
    {
        // GetAsyncKeyState reads physical buttons: a left-handed mouse drags icons with VK_RBUTTON.
        Assert.Equal(0x01, IconArranger.PrimaryButton(swapped: false));
        Assert.Equal(0x02, IconArranger.PrimaryButton(swapped: true));
    }

    [Fact]
    public void AClickWithoutMovingIsNotADrag()
    {
        var watcher = Watcher();

        Assert.False(watcher.Update(true, 100, 100, true));
        Assert.False(watcher.Update(true, 101, 102, true));
        Assert.False(watcher.Update(false, 0, 0, false));
    }

    [Fact]
    public void AMoveOverTheDesktopEndsAsADrag()
    {
        var watcher = Watcher();

        Assert.False(watcher.Update(true, 100, 100, true));
        Assert.False(watcher.Update(true, 180, 140, true));
        Assert.True(watcher.Update(false, 0, 0, false));
    }

    [Fact]
    public void AMoveThatNeverCrossesTheDesktopIsNotOne()
    {
        var watcher = Watcher();

        watcher.Update(true, 100, 100, false);
        watcher.Update(true, 400, 300, false);

        Assert.False(watcher.Update(false, 0, 0, false));
    }

    [Fact]
    public void AFileDraggedInFromAnotherWindowCounts()
    {
        var watcher = Watcher();

        watcher.Update(true, 100, 100, false); // pressed in a folder window
        watcher.Update(true, 600, 400, false);
        Assert.True(watcher.NeedsDesktopCheck);
        watcher.Update(true, 900, 500, true); // now over the desktop

        Assert.False(watcher.NeedsDesktopCheck);
        Assert.True(watcher.Update(false, 0, 0, false));
    }

    [Fact]
    public void EachPressStartsFresh()
    {
        var watcher = Watcher();
        watcher.Update(true, 0, 0, true);
        watcher.Update(true, 50, 50, true);
        Assert.True(watcher.Update(false, 0, 0, false));

        watcher.Update(true, 60, 60, true);
        Assert.False(watcher.Update(false, 0, 0, false));
        Assert.False(watcher.Pressed);
    }
}