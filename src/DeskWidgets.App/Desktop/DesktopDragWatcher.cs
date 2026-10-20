namespace DeskWidgets.Desktop;

/// <summary>
/// Spots a mouse drag that went over the desktop, from samples of the left
/// button and the cursor taken every few tens of milliseconds: the press has
/// to move by more than the system's drag distance and pass over the desktop
/// (not a widget or a window). That covers an icon dragged to another slot and
/// a file dragged in from a folder window, and ignores plain clicks.
/// </summary>
internal sealed class DesktopDragWatcher
{
    private readonly int _threshold;
    private bool _pressed, _moved, _overDesktop;
    private int _startX, _startY;

    public DesktopDragWatcher(int dragDistance) => _threshold = Math.Max(1, dragDistance);

    /// <summary>Whether the button is down and no sample has been over the desktop yet (so the caller checks the window under the cursor).</summary>
    public bool NeedsDesktopCheck => _pressed && !_overDesktop;

    public bool Pressed => _pressed;

    /// <summary>
    /// Feeds one sample. Returns true on the sample that sees the button come
    /// up after a drag that went over the desktop.
    /// </summary>
    public bool Update(bool leftDown, int x, int y, bool overDesktop)
    {
        if (leftDown)
        {
            if (!_pressed)
            {
                _pressed = true;
                _moved = _overDesktop = false;
                (_startX, _startY) = (x, y);
            }
            _overDesktop |= overDesktop;
            _moved |= Math.Abs(x - _startX) >= _threshold || Math.Abs(y - _startY) >= _threshold;
            return false;
        }
        bool dragged = _pressed && _moved && _overDesktop;
        Reset();
        return dragged;
    }

    public void Reset() => _pressed = _moved = _overDesktop = false;
}