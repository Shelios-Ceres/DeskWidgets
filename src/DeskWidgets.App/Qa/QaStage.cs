using System.Runtime.InteropServices.WindowsRuntime;
using DeskWidgets.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;

namespace DeskWidgets.Qa;

/// <summary>
/// Design QA for motion: an opaque topmost window of per-pixel noise, put behind the always-on-top test widgets. The glass blur
/// of noise is easy to see (the noise goes flat), and a recording never shows the user's own screen.
/// </summary>
internal sealed class QaStage
{
    public QaStage(RectInt32 rect, double scale)
    {
        Window = new Window();
        var pixels = new byte[rect.Width * rect.Height * 4];
        var random = new Random(7);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte value = (byte)random.Next(30, 226);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        var bitmap = new WriteableBitmap(rect.Width, rect.Height);
        pixels.CopyTo(bitmap.PixelBuffer);
        Window.Content = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Width = rect.Width / scale,
            Height = rect.Height / scale,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        Window.AppWindow.SetPresenter(presenter);
        Window.AppWindow.IsShownInSwitchers = false;
        Win32.MakeTransparentToolWindow(WindowNative.GetWindowHandle(Window));
        Window.AppWindow.MoveAndResize(rect);
        Window.AppWindow.Show(false); // without taking the focus from whatever the user is doing
        // A window made always-on-top before it is shown does not reach the top of the topmost band until the flag is set again.
        presenter.IsAlwaysOnTop = false;
        presenter.IsAlwaysOnTop = true;
    }

    public Window Window { get; }

    public void Close() => Window.Close();
}
