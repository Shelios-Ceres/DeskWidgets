using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// The window that brings a hidden dock back: a few pixels high along the bottom of the screen's work area, at alpha 1/255 so it is invisible
/// and still takes the pointer, topmost, never activated. It receives the pointer (<see cref="Reached"/>) and OLE drags
/// (<see cref="DragReached"/>). It is shown only while the dock is hidden, so it does not own the bottom pixels otherwise (SPIKE section 1).
/// </summary>
internal sealed class DockEdgeStrip : IDisposable
{
    private const string ClassName = "DeskWidgets.DockEdge";

    private static readonly Win32.WindowProcedure s_procedure = StaticProc; // must outlive the class
    private static readonly Dictionary<nint, DockEdgeStrip> s_strips = [];
    private static bool s_registered;

    private nint _hwnd;
    private DockRect _rect;
    private bool _visible;

    /// <summary>The pointer moved over the strip.</summary>
    public event Action? Reached;

    /// <summary>A file drag came over the strip.</summary>
    public event Action? DragReached;

    /// <summary>Places the strip along <paramref name="area"/>'s bottom, <paramref name="height"/> pixels high. Takes effect on the next <see cref="SetVisible"/> or at once while shown.</summary>
    public void Place(DockRect area, int height)
    {
        _rect = new DockRect(area.X, area.Bottom - height, area.Width, height);
        if (_hwnd != 0)
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOP, _rect.X, _rect.Y, _rect.Width, _rect.Height, Win32.SWP_NOACTIVATE | (_visible ? 0u : Win32.SWP_NOZORDER));
    }

    public void SetVisible(bool visible)
    {
        if (visible == _visible)
            return;
        _visible = visible;
        if (visible)
        {
            Ensure();
            if (_hwnd != 0)
            {
                Win32.SetWindowPos(_hwnd, -1 /* HWND_TOPMOST */, _rect.X, _rect.Y, _rect.Width, _rect.Height, Win32.SWP_NOACTIVATE);
                ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
            }
        }
        else if (_hwnd != 0)
        {
            ShowWindow(_hwnd, 0 /* SW_HIDE */);
        }
    }

    private void Ensure()
    {
        if (_hwnd != 0)
            return;
        if (!s_registered)
        {
            var windowClass = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_procedure),
                hInstance = Win32.GetModuleHandle(null),
                hbrBackground = 6 /* COLOR_WINDOW + 1: any colour, it is nearly invisible */,
                lpszClassName = ClassName,
            };
            Win32.RegisterClassEx(ref windowClass);
            s_registered = true;
        }
        // TOPMOST | TOOLWINDOW | LAYERED | NOACTIVATE; popup.
        _hwnd = Win32.CreateWindowEx(unchecked((int)(0x8 | 0x80 | 0x80000 | 0x08000000)), ClassName, "", unchecked((int)0x80000000), _rect.X, _rect.Y, _rect.Width, _rect.Height, 0, 0, Win32.GetModuleHandle(null), 0);
        if (_hwnd == 0)
        {
            Log.Warn("The dock's edge strip could not be created: " + Marshal.GetLastWin32Error());
            return;
        }
        s_strips[_hwnd] = this;
        SetLayeredWindowAttributes(_hwnd, 0, 1, 2 /* LWA_ALPHA */);
        ShellNative.OleInitialize(IntPtr.Zero);
        int hr = ShellNative.RegisterDragDrop(_hwnd, new DragTarget(this));
        if (hr < 0)
            Log.Warn($"Registering the dock's edge strip as a drop target failed: 0x{hr:X8}");
    }

    private static nint StaticProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_strips.TryGetValue(hwnd, out var strip))
        {
            switch (msg)
            {
                case 0x0021: // WM_MOUSEACTIVATE
                    return 3; // MA_NOACTIVATE
                case 0x0200: // WM_MOUSEMOVE
                    strip.Reached?.Invoke();
                    break;
            }
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == 0)
            return;
        ShellNative.RevokeDragDrop(_hwnd);
        s_strips.Remove(_hwnd);
        Win32.DestroyWindow(_hwnd);
        _hwnd = 0;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DragTarget(DockEdgeStrip strip) : IDropTarget
    {
        public int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
        {
            effect = 0;
            strip.DragReached?.Invoke();
            return 0;
        }

        public int DragOver(uint keyState, POINTL pt, ref uint effect)
        {
            effect = 0;
            return 0;
        }

        public int DragLeave() => 0;

        public int Drop(System.Runtime.InteropServices.ComTypes.IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
        {
            effect = 0;
            return 0;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
}
