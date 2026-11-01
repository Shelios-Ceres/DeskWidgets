using System.Runtime.InteropServices;
using DeskWidgets.Interop;
using DeskWidgets.Motion;

namespace DeskWidgets.Dock;

/// <summary>
/// Brings a window forward or minimizes it from a click on the dock (PROPOSAL 3.8). A plain <c>SetForegroundWindow</c> worked on every window
/// tried (SPIKE section 4); the Alt-key tap is the fallback when the target is not in front after about 100 ms (Aero Dock's trick for a PC
/// whose foreground lock is not 0). The click on the dock is the last input, which is what Windows asks for.
/// </summary>
internal static class DockSwitcher
{
    private const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    private const int FallbackCheckMs = 100;

    public static void Do(DockRunning.SwitchPlan plan)
    {
        if (plan.Kind == DockRunning.SwitchKind.Minimize)
        {
            ShowWindow(plan.Window, SW_MINIMIZE);
            return;
        }
        Activate(plan.Window);
    }

    public static void Activate(nint window)
    {
        if (IsIconic(window))
            ShowWindow(window, SW_RESTORE);
        Win32.SetForegroundWindow(window);
        FrameClock.After(FallbackCheckMs, () =>
        {
            if (Win32.ForegroundWindow() == window || !IsWindow(window))
                return;
            // The foreground lock refused: a tap on Alt lets the next call through.
            keybd_event(0x12, 0, 0, 0);
            keybd_event(0x12, 0, 2, 0);
            Win32.SetForegroundWindow(window);
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
}
