using System.Globalization;

namespace DeskWidgets.Widgets.Folder;

internal enum PathOpKind
{
    Move,
    Line,
    Cubic,
    Quad,
    Arc,
    Close,
}

/// <summary>One drawing step with absolute coordinates. Which fields are used depends on <see cref="Kind"/>.</summary>
internal readonly record struct PathOp(PathOpKind Kind, double X = 0, double Y = 0, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0,
    double Rx = 0, double Ry = 0, double Rotation = 0, bool LargeArc = false, bool Sweep = false);

/// <summary>
/// A reader for SVG path data (the Glass art in ICONS.md): M L H V C S Q T A Z, absolute and relative, with the compact number
/// forms SVG allows ("2.2.96" is two numbers, "a3 3 0 0 1 4-4" has its flags as single digits). It turns every command into
/// absolute <see cref="PathOp"/>s, which the Glass view then builds into a WinUI geometry.
/// </summary>
internal static class SvgPath
{
    public static List<PathOp> Parse(string data)
    {
        var ops = new List<PathOp>();
        int i = 0;
        double x = 0, y = 0, startX = 0, startY = 0;
        double lastControlX = 0, lastControlY = 0;
        char command = ' ', previous = ' ';

        while (true)
        {
            SkipSeparators(data, ref i);
            if (i >= data.Length)
                break;
            if (char.IsLetter(data[i]))
                command = data[i++];
            else if (command is ' ' or 'z' or 'Z')
                throw new FormatException("Path data must start with a command: " + data);

            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    double nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { nx += x; ny += y; }
                    (x, y, startX, startY) = (nx, ny, nx, ny);
                    ops.Add(new PathOp(PathOpKind.Move, x, y));
                    command = relative ? 'l' : 'L'; // extra pairs after a move are lines
                    break;
                }
                case 'L':
                {
                    double nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { nx += x; ny += y; }
                    (x, y) = (nx, ny);
                    ops.Add(new PathOp(PathOpKind.Line, x, y));
                    break;
                }
                case 'H':
                {
                    double nx = Number(data, ref i);
                    x = relative ? x + nx : nx;
                    ops.Add(new PathOp(PathOpKind.Line, x, y));
                    break;
                }
                case 'V':
                {
                    double ny = Number(data, ref i);
                    y = relative ? y + ny : ny;
                    ops.Add(new PathOp(PathOpKind.Line, x, y));
                    break;
                }
                case 'C':
                {
                    double x1 = Number(data, ref i), y1 = Number(data, ref i), x2 = Number(data, ref i), y2 = Number(data, ref i), nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { x1 += x; y1 += y; x2 += x; y2 += y; nx += x; ny += y; }
                    ops.Add(new PathOp(PathOpKind.Cubic, nx, ny, x1, y1, x2, y2));
                    (lastControlX, lastControlY, x, y) = (x2, y2, nx, ny);
                    break;
                }
                case 'S':
                {
                    double x2 = Number(data, ref i), y2 = Number(data, ref i), nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { x2 += x; y2 += y; nx += x; ny += y; }
                    bool chained = char.ToUpperInvariant(previous) is 'C' or 'S';
                    double x1 = chained ? 2 * x - lastControlX : x, y1 = chained ? 2 * y - lastControlY : y;
                    ops.Add(new PathOp(PathOpKind.Cubic, nx, ny, x1, y1, x2, y2));
                    (lastControlX, lastControlY, x, y) = (x2, y2, nx, ny);
                    break;
                }
                case 'Q':
                {
                    double x1 = Number(data, ref i), y1 = Number(data, ref i), nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { x1 += x; y1 += y; nx += x; ny += y; }
                    ops.Add(new PathOp(PathOpKind.Quad, nx, ny, x1, y1));
                    (lastControlX, lastControlY, x, y) = (x1, y1, nx, ny);
                    break;
                }
                case 'T':
                {
                    double nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { nx += x; ny += y; }
                    bool chained = char.ToUpperInvariant(previous) is 'Q' or 'T';
                    double x1 = chained ? 2 * x - lastControlX : x, y1 = chained ? 2 * y - lastControlY : y;
                    ops.Add(new PathOp(PathOpKind.Quad, nx, ny, x1, y1));
                    (lastControlX, lastControlY, x, y) = (x1, y1, nx, ny);
                    break;
                }
                case 'A':
                {
                    double rx = Number(data, ref i), ry = Number(data, ref i), rotation = Number(data, ref i);
                    bool large = Flag(data, ref i), sweep = Flag(data, ref i);
                    double nx = Number(data, ref i), ny = Number(data, ref i);
                    if (relative) { nx += x; ny += y; }
                    ops.Add(new PathOp(PathOpKind.Arc, nx, ny, Rx: rx, Ry: ry, Rotation: rotation, LargeArc: large, Sweep: sweep));
                    (x, y) = (nx, ny);
                    break;
                }
                case 'Z':
                    ops.Add(new PathOp(PathOpKind.Close));
                    (x, y) = (startX, startY);
                    command = ' ';
                    break;
                default:
                    throw new FormatException($"Unknown path command '{command}' in: {data}");
            }
            previous = command;
        }
        return ops;
    }

    private static void SkipSeparators(string data, ref int i)
    {
        while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ','))
            i++;
    }

    private static bool Flag(string data, ref int i)
    {
        SkipSeparators(data, ref i);
        if (i >= data.Length || data[i] is not ('0' or '1'))
            throw new FormatException("Expected an arc flag in: " + data);
        return data[i++] == '1';
    }

    private static double Number(string data, ref int i)
    {
        SkipSeparators(data, ref i);
        int start = i;
        if (i < data.Length && data[i] is '+' or '-')
            i++;
        bool dot = false;
        while (i < data.Length && (char.IsAsciiDigit(data[i]) || (data[i] == '.' && !dot)))
        {
            if (data[i] == '.')
                dot = true;
            i++;
        }
        if (i < data.Length && data[i] is 'e' or 'E')
        {
            i++;
            if (i < data.Length && data[i] is '+' or '-')
                i++;
            while (i < data.Length && char.IsAsciiDigit(data[i]))
                i++;
        }
        if (i == start || !double.TryParse(data.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException($"Expected a number at {start} in: {data}");
        return value;
    }
}
