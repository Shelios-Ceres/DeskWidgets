using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskWidgets.Qa;

/// <summary>
/// Design QA for motion (<c>--motion-qa</c>): records what DWM really composed in a screen rectangle (BitBlt of the screen with
/// CAPTUREBLT) and keeps only the frames that differ from the one before. The gaps between the kept frames are the real frame
/// cadence (the capture itself takes ~7 ms, so changes closer than that cannot be told apart). The captures only ever show the
/// noise stage the test widgets sit on, never the user's own screen.
/// </summary>
internal sealed class Recorder
{
    internal sealed record Frame(double Ms, byte[] Bgra);

    private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    private readonly int _x, _y;
    private readonly Stopwatch _clock;
    private readonly List<Frame> _frames = [];
    private Thread? _thread;
    private volatile bool _stop;

    public Recorder(int x, int y, int width, int height, Stopwatch clock)
    {
        _x = x;
        _y = y;
        Width = width;
        Height = height;
        _clock = clock;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<Frame> Frames => _frames;

    public void Start()
    {
        _stop = false;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "motion-qa-recorder" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join();
    }

    private void Loop()
    {
        nint screen = GetDC(0);
        nint dc = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = Width, biHeight = -Height, biPlanes = 1, biBitCount = 32 };
        nint bitmap = CreateDIBSection(screen, ref header, 0, out nint bits, 0, 0);
        nint old = SelectObject(dc, bitmap);
        int length = Width * Height * 4;
        var previous = new byte[length];
        var current = new byte[length];
        while (!_stop && _frames.Count < 700)
        {
            BitBlt(dc, 0, 0, Width, Height, screen, _x, _y, SrcCopy | CaptureBlt);
            double now = _clock.Elapsed.TotalMilliseconds;
            Marshal.Copy(bits, current, 0, length);
            if (!current.AsSpan().SequenceEqual(previous))
            {
                _frames.Add(new Frame(now, (byte[])current.Clone()));
                (previous, current) = (current, previous);
            }
            Thread.Sleep(1);
        }
        SelectObject(dc, old);
        DeleteObject(bitmap);
        DeleteDC(dc);
        ReleaseDC(0, screen);
    }

    /// <summary>The kept frames' times after <paramref name="t0"/>, and the gaps between them: how many, when the last one came, the median gap.</summary>
    public string Describe(double t0)
    {
        var times = _frames.Select(f => f.Ms - t0).Where(t => t >= -5).ToList();
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).Where(g => g > 0).OrderBy(g => g).ToList();
        return $"{times.Count} frames, first +{(times.Count > 0 ? times[0] : 0):0} ms, last +{(times.Count > 0 ? times[^1] : 0):0} ms, median gap {(gaps.Count > 0 ? gaps[gaps.Count / 2] : 0):0.0} ms, max gap {(gaps.Count > 0 ? gaps[^1] : 0):0.0} ms; at +{string.Join(", +", times.Take(30).Select(t => t.ToString("0")))}";
    }

    /// <summary>Where the last kept frame differs from the one before it (a late repaint: what is it?), as a box, or null.</summary>
    public string DescribeLastChange(double afterMs)
    {
        for (int n = _frames.Count - 1; n > 0; n--)
        {
            if (_frames[n].Ms < afterMs)
                break;
            var a = _frames[n - 1].Bgra;
            var b = _frames[n].Bgra;
            int left = Width, top = Height, right = -1, bottom = -1, count = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width + x) * 4;
                    if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2])
                        continue;
                    count++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (count > 0)
                return $"frame at +{_frames[n].Ms:0}: {count} pixels differ, in [{left},{top} to {right},{bottom}]";
        }
        return "none";
    }

    /// <summary>Tiles up to <paramref name="max"/> evenly spread frames, each shrunk by <paramref name="shrink"/>, into one PNG.</summary>
    public void SaveSheet(string path, int max, int shrink = 2, int columns = 4, double fromMs = double.MinValue)
    {
        var usable = _frames.Where(f => f.Ms >= fromMs).ToList();
        if (usable.Count == 0)
            return;
        var picks = new List<Frame>();
        for (int i = 0; i < Math.Min(max, usable.Count); i++)
            picks.Add(usable[max >= usable.Count ? i : max == 1 ? usable.Count - 1 : i * (usable.Count - 1) / (max - 1)]);
        int tileWidth = Width / shrink, tileHeight = Height / shrink;
        int rows = (picks.Count + columns - 1) / columns;
        int sheetWidth = tileWidth * Math.Min(columns, picks.Count), sheetHeight = tileHeight * rows;
        var sheet = new byte[sheetWidth * sheetHeight * 4];
        for (int n = 0; n < picks.Count; n++)
        {
            int ox = n % columns * tileWidth, oy = n / columns * tileHeight;
            var data = picks[n].Bgra;
            for (int y = 0; y < tileHeight; y++)
            {
                for (int x = 0; x < tileWidth; x++)
                {
                    int to = ((oy + y) * sheetWidth + ox + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0;
                        for (int dy = 0; dy < shrink; dy++)
                            for (int dx = 0; dx < shrink; dx++)
                                sum += data[((y * shrink + dy) * Width + x * shrink + dx) * 4 + c];
                        sheet[to + c] = (byte)(sum / (shrink * shrink));
                    }
                    sheet[to + 3] = 255;
                }
            }
        }
        SavePng(path, sheet, sheetWidth, sheetHeight);
    }

    internal static void SavePng(string path, byte[] bgra, int width, int height)
    {
        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int from = (y * width + x) * 4, to = 1 + x * 4;
                    row[to] = bgra[from + 2];
                    row[to + 1] = bgra[from + 1];
                    row[to + 2] = bgra[from];
                    row[to + 3] = 255;
                }
                zlib.Write(row);
            }
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", pixels.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        var number = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(name);
        stream.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte[] part in new[] { name, data })
        {
            foreach (byte value in part)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xEDB88320 & (0 - (crc & 1)));
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
}
