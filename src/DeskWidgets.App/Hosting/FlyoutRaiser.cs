using System.Runtime.InteropServices;

namespace DeskWidgets.Hosting;

/// <summary>
/// A card's Glass right-click menu is a WinUI popup window owned by the card. Over another always-on-top card it was drawn underneath it,
/// because the popup is not topmost. While the menu is open this puts the popup windows the card owns above the other topmost windows.
/// </summary>
internal static class FlyoutRaiser
{
    private const uint GwOwner = 4;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
    private static readonly nint HwndTopmost = -1;

    private delegate bool EnumProc(nint hwnd, nint lParam);

    public static void Raise(nint owner)
    {
        EnumWindows((hwnd, _) =>
        {
            if (hwnd != owner && IsWindowVisible(hwnd) && GetWindow(hwnd, GwOwner) == owner)
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
            return true;
        }, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
