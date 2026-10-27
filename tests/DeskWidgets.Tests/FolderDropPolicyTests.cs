using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderDropPolicyTests
{
    private const string Folder = @"D:\Cards\Projects";

    private static readonly DropKeys Ctrl = new(true, false, false);
    private static readonly DropKeys Shift = new(false, true, false);
    private static readonly DropKeys Alt = new(false, false, true);
    private static readonly DropKeys CtrlShift = new(true, true, false);

    /// <summary>The policy of this PC's layout, with the "does this folder hold a program" question answered from a list.</summary>
    private static DropPolicy Policy(params string[] foldersWithExe) => new(
        protectedRoots: [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows", @"C:\ProgramData"],
        programOnlyRoots: [@"C:\Users\asus\AppData\Roaming", @"C:\Users\asus\AppData\Local"],
        desktopRoots: [@"C:\Users\asus\Desktop", @"C:\Users\Public\Desktop"],
        portableExe: folder => foldersWithExe.Contains(folder, StringComparer.OrdinalIgnoreCase) ? Path.Combine(folder, "app.exe") : null);

    private static DropEntry File(string path) => new(path, false, Path.GetFileName(path));

    private static DropEntry Dir(string path) => new(path, true, Path.GetFileName(path));

    private static DropEffect Effect(DropEntry entry, DropKeys keys, DropPolicy? policy = null)
        => (policy ?? Policy()).Decide(entry, keys, Folder).Effect;

    [Theory]
    [InlineData(@"D:\src\notes.txt", false, false, false, "Move")]
    [InlineData(@"D:\src\notes.txt", true, false, false, "Copy")]
    [InlineData(@"D:\src\notes.txt", false, true, false, "Move")]
    [InlineData(@"D:\src\notes.txt", false, false, true, "Link")]
    [InlineData(@"C:\Users\asus\Documents\notes.txt", false, false, false, "Copy")] // another drive
    [InlineData(@"C:\Users\asus\Documents\notes.txt", false, true, false, "Move")]
    public void OrdinaryFiles_FollowExplorersRules(string path, bool ctrl, bool shift, bool alt, string expected)
        => Assert.Equal(expected, Effect(File(path), new DropKeys(ctrl, shift, alt)).ToString());

    [Fact]
    public void CtrlShift_MakesAShortcutLikeExplorer() => Assert.Equal(DropEffect.Link, Effect(File(@"D:\src\notes.txt"), CtrlShift));

    [Fact]
    public void OrdinaryFolders_MoveOnTheSameDriveAndCopyAcrossDrives()
    {
        Assert.Equal(DropEffect.Move, Effect(Dir(@"D:\src\Folder A"), DropKeys.None));
        Assert.Equal(DropEffect.Copy, Effect(Dir(@"C:\work\Folder A"), DropKeys.None));
    }

    [Fact]
    public void AFolderWithoutAProgramDirectlyInside_IsAnOrdinaryFolder()
    {
        var policy = Policy(); // nothing holds a program
        Assert.Equal(DropEffect.Move, Effect(Dir(@"D:\src\Deep"), DropKeys.None, policy));
        Assert.Equal(DropEffect.Move, Effect(Dir(@"D:\src\OnlyUninstall"), DropKeys.None, policy));
    }

    [Theory]
    [InlineData(@"C:\Users\asus\Desktop\Tool.lnk")]
    [InlineData(@"C:\Users\Public\Desktop\Tool.lnk")] // moves too; IFileOperation asks Windows for the admin prompt
    [InlineData(@"C:\Users\asus\Desktop\Game.url")]
    public void ShortcutsFromTheDesktop_MoveIn_EvenFromAnotherDrive(string path)
    {
        Assert.Equal(DropEffect.Move, Effect(File(path), DropKeys.None));
        Assert.Equal(DropEffect.Copy, Effect(File(path), Ctrl));
    }

    [Theory]
    [InlineData(@"C:\Users\asus\Desktop\note.txt", false)]
    [InlineData(@"C:\Users\Public\Desktop\note.txt", false)]
    [InlineData(@"C:\Users\asus\Desktop\Photos", true)]
    public void OrdinaryItemsFromTheDesktop_AlwaysMove_EvenAcrossDrives(string path, bool folder)
    {
        var entry = folder ? Dir(path) : File(path);
        Assert.Equal(DropEffect.Move, Effect(entry, DropKeys.None)); // the folder is on D:, the desktop on C:: Explorer would copy
        Assert.Equal(DropEffect.Move, Effect(entry, Shift));
        Assert.Equal(DropEffect.Copy, Effect(entry, Ctrl));
        Assert.Equal(DropEffect.Link, Effect(entry, Alt));
        Assert.Equal(DropEffect.Link, Effect(entry, CtrlShift));
    }

    [Fact]
    public void TheDesktopRule_DoesNotReachBeyondTheDesktopItself()
    {
        Assert.Equal(DropEffect.Copy, Effect(File(@"C:\Users\asus\Documents\Tool.lnk"), DropKeys.None)); // not on the desktop
        Assert.Equal(DropEffect.Copy, Effect(File(@"C:\Users\asus\Desktop\Sub\Tool.lnk"), DropKeys.None)); // a subfolder of it is not the desktop
        Assert.Equal(DropEffect.Copy, Effect(File(@"C:\Users\asus\Desktop\Sub\note.txt"), DropKeys.None));
        Assert.Equal(DropEffect.Copy, Effect(File(@"C:\Users\asus\Documents\note.txt"), DropKeys.None)); // other drives still copy
    }

    [Fact]
    public void ProgramsOnTheDesktop_KeepTheProgramRules()
    {
        var program = File(@"C:\Users\asus\Desktop\Tool.exe");
        Assert.Equal(DropEffect.Link, Effect(program, DropKeys.None));
        Assert.Equal(DropEffect.Move, Effect(program, Shift));
        Assert.Equal(DropEffect.Copy, Effect(program, Ctrl));

        var policy = Policy(@"C:\Users\asus\Desktop\Portable");
        var portable = Dir(@"C:\Users\asus\Desktop\Portable");
        Assert.Equal(DropEffect.Link, Effect(portable, DropKeys.None, policy));
        Assert.Equal(DropEffect.Move, Effect(portable, Shift, policy));
    }

    [Fact]
    public void AMixedDesktopDrop_IsNamedByItsItems()
    {
        var policy = Policy();
        var note = policy.Decide(File(@"C:\Users\asus\Desktop\note.txt"), DropKeys.None, Folder);
        var other = policy.Decide(File(@"C:\Users\asus\Documents\note.txt"), DropKeys.None, Folder);
        Assert.Equal(DropVerb.Move, DropPolicy.Summarize([note]));
        Assert.Equal(DropVerb.Mixed, DropPolicy.Summarize([note, other])); // move + copy
    }

    [Fact]
    public void Programs_BecomeShortcuts_AndTheFileStays()
    {
        var program = File(@"D:\src\hello.exe");
        Assert.Equal(DropEffect.Link, Effect(program, DropKeys.None));
        Assert.Equal(DropEffect.Link, Effect(program, Alt));
        Assert.Equal(DropEffect.Move, Effect(program, Shift));
        Assert.Equal(DropEffect.Copy, Effect(program, Ctrl));
        Assert.Equal(DropEffect.Link, Effect(File(@"C:\tools\cross.exe"), DropKeys.None)); // from another drive too
    }

    [Fact]
    public void PortableAppFolders_BecomeShortcutsToTheirExe()
    {
        var policy = Policy(@"D:\src\PortableApp");
        var portable = Dir(@"D:\src\PortableApp");
        var decision = policy.Decide(portable, DropKeys.None, Folder);
        Assert.Equal(DropEffect.Link, decision.Effect);
        Assert.Equal(@"D:\src\PortableApp\app.exe", decision.Target);
        Assert.Equal(DropEffect.Move, Effect(portable, Shift, policy));
        Assert.Equal(DropEffect.Copy, Effect(portable, Ctrl, policy));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"C:\Program Files\Some App\readme.txt")] // everything there, not only programs
    [InlineData(@"C:\ProgramData\user.ini")]
    [InlineData(@"C:\Windows\Fonts\arial.ttf")]
    public void UnderProgramFilesWindowsAndProgramData_EverythingIsAShortcut_EvenWithShift(string path)
    {
        Assert.Equal(DropEffect.Link, Effect(File(path), DropKeys.None));
        Assert.Equal(DropEffect.Link, Effect(File(path), Shift));
        Assert.Equal(DropEffect.Link, Effect(File(path), Ctrl));
    }

    [Fact]
    public void UnderAppData_OnlyProgramsAreProtected()
    {
        var cursor = File(@"C:\Users\asus\AppData\Local\Programs\cursor\Cursor.exe");
        Assert.Equal(DropEffect.Link, Effect(cursor, DropKeys.None));
        Assert.Equal(DropEffect.Link, Effect(cursor, Shift));

        var portable = Dir(@"C:\Users\asus\AppData\Roaming\Tool");
        Assert.Equal(DropEffect.Link, Effect(portable, Shift, Policy(@"C:\Users\asus\AppData\Roaming\Tool")));

        // Everything else there follows the ordinary rules, %TEMP% included.
        var temp = File(@"C:\Users\asus\AppData\Local\Temp\report.docx");
        Assert.Equal(DropEffect.Copy, Effect(temp, DropKeys.None));
        Assert.Equal(DropEffect.Move, Effect(temp, Shift));
        Assert.Equal(DropEffect.Copy, Effect(Dir(@"C:\Users\asus\AppData\Roaming\Notes"), DropKeys.None));
    }

    [Fact]
    public void VirtualItems_StartAndStoreApps_AreShortcuts_WhateverTheKeys()
    {
        var app = new DropEntry(null, false, "Notepad");
        Assert.Equal(DropEffect.Link, Effect(app, DropKeys.None));
        Assert.Equal(DropEffect.Link, Effect(app, Shift));
        Assert.Equal(DropEffect.Link, Effect(app, Ctrl));
    }

    [Fact]
    public void AnItemAlreadyInTheFolder_NeedsNoWork()
    {
        Assert.Equal(DropEffect.None, Effect(File(Folder + @"\already.txt"), DropKeys.None));
        Assert.Equal(DropEffect.None, Effect(File(Folder.ToLowerInvariant() + @"\already.txt"), DropKeys.None));
    }

    [Fact]
    public void ADrop_IsNamedByWhatItsItemsDo()
    {
        var policy = Policy();
        var move = policy.Decide(File(@"D:\src\a.txt"), DropKeys.None, Folder);
        var copy = policy.Decide(File(@"C:\x\a.txt"), DropKeys.None, Folder);
        var link = policy.Decide(File(@"D:\src\a.exe"), DropKeys.None, Folder);
        var none = policy.Decide(File(Folder + @"\a.txt"), DropKeys.None, Folder);

        Assert.Equal(DropVerb.Move, DropPolicy.Summarize([move, move]));
        Assert.Equal(DropVerb.Copy, DropPolicy.Summarize([copy]));
        Assert.Equal(DropVerb.Shortcut, DropPolicy.Summarize([link]));
        Assert.Equal(DropVerb.Mixed, DropPolicy.Summarize([move, link]));
        Assert.Equal(DropVerb.Move, DropPolicy.Summarize([move, none])); // an item that needs no work does not make the drop mixed
        Assert.Equal(DropVerb.None, DropPolicy.Summarize([none]));
    }

    [Fact]
    public void TheCursor_ShowsTheEffectTheSourceAllows()
    {
        var policy = Policy();
        var move = policy.Decide(File(@"D:\src\a.txt"), DropKeys.None, Folder);
        var link = policy.Decide(File(@"D:\src\a.exe"), DropKeys.None, Folder);
        const DropEffect All = DropEffect.Copy | DropEffect.Move | DropEffect.Link;

        Assert.Equal(DropEffect.Move, DropPolicy.CursorEffect([move], All));
        Assert.Equal(DropEffect.Copy, DropPolicy.CursorEffect([move, link], All)); // a mixed drop shows copy
        // A Start-menu drag allows only copy and link: a move request falls back to link.
        Assert.Equal(DropEffect.Link, DropPolicy.CursorEffect([move], DropEffect.Copy | DropEffect.Link));
        Assert.Equal(DropEffect.None, DropPolicy.CursorEffect([move], DropEffect.None));
        Assert.Equal(DropEffect.None, DropPolicy.CursorEffect([], All));
    }

    [Fact]
    public void AMoveTheSourceDoesNotOffer_BecomesACopy_OrAShortcut()
    {
        var policy = Policy();
        const DropEffect All = DropEffect.Copy | DropEffect.Move | DropEffect.Link;
        var move = policy.Decide(File(@"D:\src\a.txt"), DropKeys.None, Folder); // the same drive: the table says move
        Assert.Equal(DropEffect.Move, move.Effect);

        Assert.Same(move, DropPolicy.Allow(move, All));
        // An app handing out a file from its own store offers copy only: the file is copied, and the app keeps its own.
        Assert.Equal(DropEffect.Copy, DropPolicy.Allow(move, DropEffect.Copy).Effect);
        Assert.Equal(DropEffect.Copy, DropPolicy.Allow(move, DropEffect.Copy | DropEffect.Link).Effect);
        Assert.Equal(DropEffect.Link, DropPolicy.Allow(move, DropEffect.Link).Effect);
        // Shift, or the desktop rule, asks for a move; the source still has the last word.
        Assert.Equal(DropEffect.Copy, DropPolicy.Allow(policy.Decide(File(@"D:\src\a.txt"), Shift, Folder), DropEffect.Copy).Effect);
        Assert.Equal(DropEffect.Copy, DropPolicy.Allow(policy.Decide(File(@"C:\Users\asus\Desktop\b.txt"), DropKeys.None, Folder), DropEffect.Copy).Effect);

        // Copies and shortcuts leave the source as it is, and stay what the table says.
        var copy = policy.Decide(File(@"D:\src\a.txt"), Ctrl, Folder);
        var link = policy.Decide(File(@"D:\src\a.exe"), DropKeys.None, Folder);
        Assert.Same(copy, DropPolicy.Allow(copy, DropEffect.Link));
        Assert.Same(link, DropPolicy.Allow(link, DropEffect.Copy));

        // The label and the cursor then say what will happen.
        var allowed = DropPolicy.Allow(move, DropEffect.Copy | DropEffect.Link);
        Assert.Equal(DropVerb.Copy, DropPolicy.Summarize([allowed]));
        Assert.Equal(DropEffect.Copy, DropPolicy.CursorEffect([allowed], DropEffect.Copy | DropEffect.Link));
    }

    [Fact]
    public void ThePortableExe_IsTheOneNamedLikeTheFolder_ElseTheBiggest_AndNeverAnUninstaller()
    {
        string root = Path.Combine(Path.GetTempPath(), "bigfolder-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string named = Directory.CreateDirectory(Path.Combine(root, "Tool")).FullName;
            System.IO.File.WriteAllBytes(Path.Combine(named, "other.exe"), new byte[500]);
            System.IO.File.WriteAllBytes(Path.Combine(named, "Tool.exe"), new byte[10]);
            System.IO.File.WriteAllBytes(Path.Combine(named, "unins000.exe"), new byte[900]);
            Assert.Equal(Path.Combine(named, "Tool.exe"), DropPolicy.FindPortableExe(named));

            string biggest = Directory.CreateDirectory(Path.Combine(root, "Suite")).FullName;
            System.IO.File.WriteAllBytes(Path.Combine(biggest, "a.exe"), new byte[10]);
            System.IO.File.WriteAllBytes(Path.Combine(biggest, "b.exe"), new byte[300]);
            Assert.Equal(Path.Combine(biggest, "b.exe"), DropPolicy.FindPortableExe(biggest));

            string onlyJunk = Directory.CreateDirectory(Path.Combine(root, "Junk")).FullName;
            System.IO.File.WriteAllBytes(Path.Combine(onlyJunk, "uninstall.exe"), new byte[10]);
            Assert.Null(DropPolicy.FindPortableExe(onlyJunk));

            string deeper = Directory.CreateDirectory(Path.Combine(root, "Deep", "bin")).FullName;
            System.IO.File.WriteAllBytes(Path.Combine(deeper, "inner.exe"), new byte[10]);
            Assert.Null(DropPolicy.FindPortableExe(Path.GetDirectoryName(deeper)!)); // not directly inside
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void ModifierKeys_AreReadFromOleKeyState()
    {
        Assert.Equal(new DropKeys(true, true, true), DropKeys.FromOle(0x08 | 0x04 | 0x20 | 0x01));
        Assert.Equal(DropKeys.None, DropKeys.FromOle(0x01));
    }

    [Fact]
    public void ADroppedFolder_IsListedOnce_WhateverTheKeysAndHoverCount()
    {
        int listings = 0;
        var policy = new DropPolicy([@"C:\Windows"], [@"C:\Users\asus\AppData\Local"], [@"C:\Users\asus\Desktop"], folder =>
        {
            listings++;
            return Path.Combine(folder, "app.exe");
        });
        var folder = Dir(@"D:\src\Portable");
        var other = Dir(@"D:\src\Other");
        for (int hover = 0; hover < 50; hover++)
        {
            policy.Decide(folder, hover % 2 == 0 ? Ctrl : DropKeys.None, Folder);
            policy.Decide(other, Shift, Folder);
        }
        Assert.Equal(2, listings); // one listing per dropped item, not one per DragOver
    }
}
