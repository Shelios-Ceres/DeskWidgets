using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;
using SystemComposition = Windows.UI.Composition;

namespace DeskWidgets.Hosting;

/// <summary>
/// Live frosted glass inside a rounded card, fully transparent everywhere else.
///
/// WinUI renders its content into a child window. As a system backdrop this
/// class only makes that content's background transparent; the glass itself
/// is a visual on the top-level window (a DesktopWindowTarget below the child
/// window) painted with DWM's host backdrop — the real-time blur of whatever
/// is behind the window, the base layer acrylic is built on — and clipped to
/// the card's rounded rectangle. XAML draws the thin tint and edge on top.
/// Unlike DWM's own window rounding (8 px) the corner radius here is free.
/// </summary>
internal sealed class GlassBackdrop : SystemBackdrop, IDisposable
{
    private static SystemComposition.Compositor? s_compositor;
    private static nint s_dispatcherQueueController;

    private SystemComposition.Desktop.DesktopWindowTarget? _target;
    private SystemComposition.SpriteVisual? _glass;
    private SystemComposition.CompositionRoundedRectangleGeometry? _clipGeometry;

    /// <summary>Creates the glass visual on the top-level window.</summary>
    public void Attach(nint hwnd)
    {
        var compositor = GetCompositor();
        compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(hwnd, false, out nint raw);
        _target = SystemComposition.Desktop.DesktopWindowTarget.FromAbi(raw);
        Marshal.Release(raw);

        _clipGeometry = compositor.CreateRoundedRectangleGeometry();
        _glass = compositor.CreateSpriteVisual();
        _glass.Brush = compositor.CreateHostBackdropBrush();
        _glass.Clip = compositor.CreateGeometricClip(_clipGeometry);
        _target.Root = _glass;
    }

    /// <summary>Shows or hides the glass; the pixel theme's cards are opaque and need none.</summary>
    public void SetVisible(bool visible)
    {
        if (_glass is not null)
            _glass.IsVisible = visible;
    }

    /// <summary>Card size and corner radius in physical pixels (the window is the card).</summary>
    public void SetLayout(Vector2 size, float cornerRadius)
    {
        if (_glass is null || _clipGeometry is null)
            return;
        _glass.Size = size;
        _clipGeometry.Size = size;
        _clipGeometry.CornerRadius = new Vector2(cornerRadius);
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = GetCompositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    public void Dispose()
    {
        if (_target is not null)
            _target.Root = null;
        _target?.Dispose();
        _glass?.Dispose();
        _target = null;
        _glass = null;
    }

    // Windows.UI (system) composition needs a Windows.System dispatcher queue on this thread.
    private static SystemComposition.Compositor GetCompositor()
    {
        if (s_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                    threadType = 2,    // DQTYPE_THREAD_CURRENT
                    apartmentType = 2, // DQTAT_COM_STA
                };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_dispatcherQueueController));
            }
            s_compositor = new SystemComposition.Compositor();
        }
        return s_compositor;
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint dispatcherQueueController);
}
