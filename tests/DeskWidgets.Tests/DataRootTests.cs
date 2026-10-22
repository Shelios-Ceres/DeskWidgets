using DeskWidgets.Core;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Desktop;
using DeskWidgets.Hosting;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class DataRootTests
{
    private const string Default = @"C:\Users\someone\AppData\Local\DeskWidgets";

    [Fact]
    public void NoFlagAndNoVariable_IsTheDefaultFolder_AndNotATestInstance()
    {
        var resolved = DataRoot.Resolve(["DeskWidgets.exe", "--autostart"], null, Default);

        Assert.Equal(Default, resolved.Path);
        Assert.False(resolved.IsTestInstance);
        Assert.Null(resolved.Error);
    }

    [Theory]
    [InlineData("--data-dir", @"D:\Widget\.scratch\run1")]
    [InlineData("--DATA-DIR", @"D:\Widget\.scratch\run1")]
    public void TheFlagWithASeparateValue_PicksTheFolder(string flag, string folder)
    {
        var resolved = DataRoot.Resolve(["DeskWidgets.exe", flag, folder, "--demo"], null, Default);

        Assert.Equal(folder, resolved.Path);
        Assert.True(resolved.IsTestInstance);
        Assert.Null(resolved.Error);
    }

    [Fact]
    public void TheFlagWithAnEqualsSign_PicksTheFolder()
    {
        var resolved = DataRoot.Resolve(["DeskWidgets.exe", @"--data-dir=D:\Widget\.scratch\run2"], null, Default);

        Assert.Equal(@"D:\Widget\.scratch\run2", resolved.Path);
        Assert.True(resolved.IsTestInstance);
    }

    [Fact]
    public void TheVariable_PicksTheFolder_AndTheFlagBeatsIt()
    {
        var fromVariable = DataRoot.Resolve(["DeskWidgets.exe"], @"D:\Widget\.scratch\env", Default);
        Assert.Equal(@"D:\Widget\.scratch\env", fromVariable.Path);
        Assert.True(fromVariable.IsTestInstance);

        var both = DataRoot.Resolve(["DeskWidgets.exe", "--data-dir", @"D:\Widget\.scratch\flag"], @"D:\Widget\.scratch\env", Default);
        Assert.Equal(@"D:\Widget\.scratch\flag", both.Path);
    }

    [Fact]
    public void ARelativeFolder_IsTakenAgainstTheCurrentDirectory()
    {
        var resolved = DataRoot.Resolve(["DeskWidgets.exe", "--data-dir", "scratch-data"], null, Default);

        Assert.Equal(Path.GetFullPath("scratch-data"), resolved.Path);
        Assert.True(Path.IsPathRooted(resolved.Path));
    }

    [Theory]
    [InlineData(new[] { "DeskWidgets.exe", "--data-dir" }, null)]
    [InlineData(new[] { "DeskWidgets.exe", "--data-dir", "--demo" }, null)]
    [InlineData(new[] { "DeskWidgets.exe", "--data-dir=" }, null)]
    [InlineData(new[] { "DeskWidgets.exe" }, "  ")]
    [InlineData(new[] { "DeskWidgets.exe", "--data-dir", "bad|name" }, null)]
    public void AnUnusableValue_IsAnError_NotASilentFallbackToTheUsersData(string[] arguments, string? variable)
    {
        var resolved = DataRoot.Resolve(arguments, variable, Default);

        Assert.NotNull(resolved.Error);
        Assert.True(resolved.IsTestInstance);
    }

    [Fact]
    public void TheDefaultFolder_IsDeskWidgetsUnderLocalAppData()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets"),
            DataRoot.DefaultPath());

    // The test host sets DESKWIDGETS_DATA_DIR before anything runs (TestSetup), so a test never touches the user's data.

    [Fact]
    public void TheTestRun_UsesItsOwnDataFolder()
    {
        Assert.True(DataRoot.IsTestInstance);
        Assert.Null(DataRoot.Error);
        Assert.Equal(TestSetup.DataFolder, DataRoot.Path);
        Assert.NotEqual(DataRoot.DefaultPath(), DataRoot.Path);
    }

    [Fact]
    public void AllFourStoresFollowTheRoot()
    {
        Assert.Equal(Path.Combine(DataRoot.Path, "logs"), Log.LogDirectory);
        Assert.Equal(Path.Combine(DataRoot.Path, "desktop-icons.json"), IconStore.LayoutPath);
        Assert.Equal(Path.Combine(DataRoot.Path, "settings.json"), DataRoot.SettingsFile);
        Assert.StartsWith(Path.Combine(DataRoot.Path, "folder-origins") + Path.DirectorySeparatorChar, OriginLog.PathFor(@"C:\Users\asus\Big Folder"));
    }

    [Fact]
    public void SettingsAreSavedUnderTheRoot()
    {
        var store = new SettingsStore();
        store.Current.Initialized = true;
        store.Save();

        Assert.True(File.Exists(DataRoot.SettingsFile));
        Assert.StartsWith(TestSetup.DataFolder, DataRoot.SettingsFile);
    }

    [Fact]
    public void StartWithWindows_IsNotAvailableInATestInstance()
    {
        Assert.False(StartupRegistration.Available);
        Assert.False(StartupRegistration.IsEnabled);
        Assert.Throws<InvalidOperationException>(() => StartupRegistration.SetEnabled(true));
        Assert.Throws<InvalidOperationException>(() => StartupRegistration.SetEnabled(false));
    }

    [Fact]
    public void TheArranger_CannotBeSwitchedOnInATestInstance()
    {
        Assert.False(IconArranger.MaySwitchOn(testInstance: true));
        Assert.True(IconArranger.MaySwitchOn(testInstance: false));
    }

    [Fact]
    public void ATestInstanceDoesNotTakeOverTheOldToolsIconOrder()
    {
        string root = Path.Combine(TestSetup.DataFolder, "icons-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "legacy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), """{"Order":["a.lnk","b.lnk"]}""");
        string layout = Path.Combine(root, "mine", "desktop-icons.json");

        var test = IconStore.LoadLayout(layout, legacy, takeOver: false, save: true);
        Assert.Empty(test.Order);
        Assert.False(File.Exists(layout));

        var user = IconStore.LoadLayout(layout, legacy, takeOver: true, save: true);
        Assert.Equal(["a.lnk", "b.lnk"], user.Order);
        Assert.True(File.Exists(layout));
    }

    [Fact]
    public void ANewBigFolder_GoesUnderTheTestFolder_InATestInstance()
    {
        string testFolder = Path.Combine(TestSetup.DataFolder, "Big Folders");

        Assert.Equal(testFolder, FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", testFolder));
        Assert.Equal(testFolder, FolderActions.DefaultFolderParent(@"C:\Users\asus", @"D:\Desktop", testFolder));
        Assert.Equal(DataRoot.BigFolders, Path.Combine(DataRoot.Path, "Big Folders"));
    }

    [Fact]
    public void ANewBigFolder_ForTheUser_StaysOnTheDesktopsDrive()
    {
        Assert.Equal(@"C:\Users\asus", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", null));
        Assert.Equal(@"D:\Big Folders", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"D:\Desktop", null));
    }

    [Fact]
    public void ANewBigFolder_GoesInTheFolderRoot_WhenOneIsSet()
    {
        Assert.Equal(@"D:\Widget\big folder", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", null, @"D:\Widget\big folder"));
        Assert.Equal(@"D:\Widget\big folder", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"D:\Desktop", null, @"D:\Widget\big folder\")); // a trailing slash is dropped
        Assert.Equal(@"E:\", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", null, @"E:\"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"big folder")] // relative: it would depend on the working directory
    [InlineData(@"\big folder")]
    [InlineData(@"D:big folder")]
    [InlineData("D:\\bad|name")]
    public void AnUnusableFolderRoot_FallsBackToTheDefault(string? root)
    {
        Assert.Null(FolderActions.UsableRoot(root));
        Assert.Equal(@"C:\Users\asus", FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", null, root));
    }

    [Fact]
    public void ATestInstance_IgnoresAFolderRoot()
    {
        string testFolder = Path.Combine(TestSetup.DataFolder, "Big Folders");
        // A seed copied from the user's settings must never send a test into the user's real folders.
        Assert.Equal(testFolder, FolderActions.DefaultFolderParent(@"C:\Users\asus", @"C:\Users\asus\Desktop", testFolder, @"D:\Widget\big folder"));
    }

    [Fact]
    public void TheFolderRoot_IsSavedOnlyWhenSet()
    {
        var settings = new AppSettings();
        string unset = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("FolderRoot", unset);

        settings.FolderRoot = @"D:\Widget\big folder";
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.Equal(@"D:\Widget\big folder", back!.FolderRoot);
    }
}
