using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>Which button a dialog's logic currently has focus on (plain logic, testable).</summary>
internal sealed class DialogFocus
{
    public DialogFocus(int buttonCount, int defaultButton)
    {
        ButtonCount = buttonCount;
        Index = Math.Clamp(defaultButton, 0, buttonCount - 1);
    }

    public int ButtonCount { get; }

    public int Index { get; private set; }

    public void Move(int direction) => Index = ((Index + direction) % ButtonCount + ButtonCount) % ButtonCount;

    public void Set(int index) => Index = Math.Clamp(index, 0, ButtonCount - 1);
}

/// <summary>
/// A small modal question in the pixel look, replacing the system MessageBox: the owner window is disabled
/// while it is up, the default button has the focus, Esc answers the cancel button, Enter presses the focused
/// one and the title strip drags the dialog.
/// </summary>
internal sealed class PixelDialog : PixelPopupWindow
{
    private readonly nint _owner;
    private readonly DialogLayout _layout;
    private readonly DialogBadge _badge;
    private readonly bool _bodyIsDetail;
    private readonly DialogButtonSpec[] _buttons;
    private readonly DialogFocus _focus;
    private readonly int _cancelIndex;
    private readonly string _name;
    private readonly string _description;
    private readonly Action<int> _done;
    private readonly DialogAccessible _accessible;
    private int _pressed = -1;
    private int _hot = -1;
    private bool _finished;

    private PixelDialog(nint owner, DialogBadge badge, string? heading, string body, bool bodyIsDetail, DialogButtonSpec[] buttons, int defaultButton, int cancelButton, Action<int> done)
    {
        _owner = owner;
        _badge = badge;
        _bodyIsDetail = bodyIsDetail;
        _buttons = buttons;
        _focus = new DialogFocus(buttons.Length, defaultButton);
        _cancelIndex = Math.Clamp(cancelButton, 0, buttons.Length - 1);
        _done = done;
        _name = Strings.AppName;
        _description = heading is null ? body : heading + " " + body;
        _layout = DialogLayout.Create(Strings.AppName, heading, body, buttons);
        _accessible = new DialogAccessible(this);
        CreateWindow(noActivate: false, owner);
    }

    /// <summary>The dialog being shown, if any (one at a time).</summary>
    public static PixelDialog? Active { get; private set; }

    public string Name => _name;

    public string Description => _description;

    public IReadOnlyList<DialogButtonSpec> Buttons => _buttons;

    public int FocusedButton => _focus.Index;

    public Win32.RECT ButtonRect(int index)
    {
        var (x, y, w, h) = _layout.Buttons[index];
        return new Win32.RECT { Left = Origin.X + x * Factor, Top = Origin.Y + y * Factor, Right = Origin.X + (x + w) * Factor, Bottom = Origin.Y + (y + h) * Factor };
    }

    public Win32.RECT PanelRect() => new() { Left = Origin.X, Top = Origin.Y, Right = Origin.X + DialogLayout.Width * Factor, Bottom = Origin.Y + _layout.Height * Factor };

    /// <summary>
    /// Shows a dialog over <paramref name="owner"/> (disabled until it closes; 0 for none) and calls <paramref name="done"/>
    /// with the pressed button's index. Esc answers <paramref name="cancelButton"/>.
    /// </summary>
    public static PixelDialog Show(nint owner, DialogBadge badge, string? heading, string body, bool bodyIsDetail, DialogButtonSpec[] buttons, int defaultButton, int cancelButton, Action<int> done)
    {
        Active?.Finish(Active._cancelIndex);
        var dialog = new PixelDialog(owner, badge, heading, body, bodyIsDetail, buttons, defaultButton, cancelButton, done);
        Active = dialog;
        dialog.Open();
        return dialog;
    }

    /// <summary>Exit's question: Yes and No, with No as the default and as Esc's answer.</summary>
    public static PixelDialog AskExit(nint owner, Action<bool> done)
        => Show(owner, DialogBadge.Question, Strings.ExitQuestion, Strings.ExitDetail, bodyIsDetail: true,
            [new DialogButtonSpec(Strings.Yes, false), new DialogButtonSpec(Strings.No, true)], defaultButton: 1, cancelButton: 1, answer => done(answer == 0));

    /// <summary>An error box with one OK button.</summary>
    public static PixelDialog ShowWarning(nint owner, string text)
        => Show(owner, DialogBadge.Warning, null, text, bodyIsDetail: false, [new DialogButtonSpec(Strings.Ok, true)], 0, 0, _ => { });

    private void Open()
    {
        var anchor = new Win32.POINT();
        if (_owner != 0 && Native.GetWindowRect(_owner, out var ownerRect))
            anchor = new Win32.POINT { X = (ownerRect.Left + ownerRect.Right) / 2, Y = (ownerRect.Top + ownerRect.Bottom) / 2 };
        else
            Win32.GetCursorPos(out anchor);
        int factor = PixelCard.Factor(Win32.GetScaleAt(anchor));
        var work = Win32.GetWorkArea(anchor);
        int width = (DialogLayout.Width + DialogLayout.ShadowOffset) * factor, height = (_layout.Height + DialogLayout.ShadowOffset) * factor;
        Draw(factor, work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2);

        if (_owner != 0)
            Native.EnableWindow(_owner, false);
        ShowWindow(activate: true);
        Win32.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
        Native.NotifyWinEvent(Native.EVENT_SYSTEM_DIALOGSTART, Hwnd, Native.OBJID_CLIENT, 0);
        _accessible.RaiseOpened(UiaNative.EventWindowOpened);
        AnnounceFocus();
    }

    private void Draw(int factor, int x, int y)
    {
        bool dark = IsDark();
        var palette = PixelPalette.For(dark);
        var panel = DialogPainter.Draw(palette, _layout, new DialogPaint(_badge, _bodyIsDetail, _buttons, _hot, _pressed, _focus.Index), dark);
        Present(PixelFrame.WithShadow(panel, palette.Shadow, DialogLayout.ShadowOffset), factor, x, y);
    }

    private void Redraw()
    {
        // The strip may have been dragged: the window's own position is the truth.
        if (Native.GetWindowRect(Hwnd, out var rect))
            Draw(Factor, rect.Left, rect.Top);
    }

    private int ButtonAt(int screenX, int screenY)
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            var rect = ButtonRect(i);
            if (screenX >= rect.Left && screenX < rect.Right && screenY >= rect.Top && screenY < rect.Bottom)
                return i;
        }
        return -1;
    }

    private bool OnTitleStrip(int screenX, int screenY)
    {
        int ax = (screenX - Origin.X) / Factor, ay = (screenY - Origin.Y) / Factor;
        return screenX >= Origin.X && screenY >= Origin.Y && ax < DialogLayout.Width && ay >= 2 && ay < 2 + _layout.TitleHeight;
    }

    /// <summary>Answers with the button at <paramref name="index"/> and closes.</summary>
    public void Finish(int index)
    {
        if (_finished)
            return;
        _finished = true;
        if (Active == this)
            Active = null;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        Native.NotifyWinEvent(Native.EVENT_SYSTEM_DIALOGEND, Hwnd, Native.OBJID_CLIENT, 0);
        if (_owner != 0)
            Native.EnableWindow(_owner, true); // before the dialog goes, or the owner loses the foreground to another app
        Dispose();
        try
        {
            _done(index);
        }
        catch (Exception ex)
        {
            Log.Warn("Dialog handler failed", ex);
        }
    }

    public void OnKey(int virtualKey)
    {
        switch (virtualKey)
        {
            case Native.VK_ESCAPE:
                Finish(_cancelIndex);
                break;
            case Native.VK_RETURN or Native.VK_SPACE:
                Finish(_focus.Index);
                break;
            case Native.VK_LEFT or Native.VK_UP:
                MoveFocus(-1);
                break;
            case Native.VK_RIGHT or Native.VK_DOWN:
                MoveFocus(+1);
                break;
            case Native.VK_TAB:
                MoveFocus((Win32.GetAsyncKeyState(0x10) & 0x8000) != 0 ? -1 : +1);
                break;
            case 0x59 when _buttons.Length == 2: // Y
                Finish(0);
                break;
            case 0x4E when _buttons.Length == 2: // N
                Finish(1);
                break;
        }
    }

    private void MoveFocus(int direction)
    {
        _focus.Move(direction);
        Redraw();
        AnnounceFocus();
    }

    public void FocusButton(int index)
    {
        _focus.Set(index);
        Redraw();
        AnnounceFocus();
    }

    private void AnnounceFocus()
    {
        Native.NotifyWinEvent(Native.EVENT_OBJECT_FOCUS, Hwnd, Native.OBJID_CLIENT, _focus.Index + 2);
        _accessible.RaiseFocus(_focus.Index + 2);
    }

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND:
                return 1;
            case Native.WM_GETOBJECT:
            {
                int objectId = unchecked((int)lParam.ToInt64());
                if (objectId == UiaNative.UiaRootObjectId)
                    return _accessible.GetUiaObject(wParam, lParam);
                if (objectId == Native.OBJID_CLIENT)
                    return _accessible.GetObject(wParam);
                break;
            }
            case Native.WM_NCHITTEST:
            {
                // The title strip drags the dialog; everything else is client area.
                int x = Native.SignedLow(lParam), y = Native.SignedHigh(lParam);
                return OnTitleStrip(x, y) ? Native.HTCAPTION : Native.HTCLIENT;
            }
            case Native.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;
            case Native.WM_MOUSEMOVE:
            {
                Native.TrackLeave(Hwnd);
                Win32.GetCursorPos(out var point);
                int hot = ButtonAt(point.X, point.Y);
                if (hot != _hot)
                {
                    _hot = hot;
                    Redraw();
                }
                return 0;
            }
            case Native.WM_MOUSELEAVE:
                if (_hot != -1)
                {
                    _hot = -1;
                    Redraw();
                }
                return 0;
            case Native.WM_LBUTTONDOWN:
            {
                Win32.GetCursorPos(out var point);
                _pressed = ButtonAt(point.X, point.Y);
                if (_pressed >= 0)
                {
                    _focus.Set(_pressed);
                    Native.SetCapture(Hwnd);
                    Redraw();
                }
                return 0;
            }
            case Native.WM_LBUTTONUP:
            {
                Win32.GetCursorPos(out var point);
                int pressed = _pressed;
                _pressed = -1;
                if (Native.GetCapture() == Hwnd)
                    Native.ReleaseCapture();
                if (pressed >= 0 && ButtonAt(point.X, point.Y) == pressed)
                    Finish(pressed);
                else if (pressed >= 0)
                    Redraw();
                return 0;
            }
            case 0x0010 /* WM_CLOSE */:
                Finish(_cancelIndex);
                return 0;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    public override void Dispose()
    {
        if (Hwnd != 0)
            UiaNative.UiaReturnRawElementProvider(Hwnd, 0, 0, null);
        _accessible.DisconnectUia();
        base.Dispose();
    }
}

/// <summary>The dialog's accessibility tree: 1 = the text, 2.. = the buttons; the dialog's name is the app name, its description the text.</summary>
internal sealed class DialogAccessible(PixelDialog dialog) : PopupAccessible(() => dialog.Hwnd)
{
    protected override int ChildCount => 1 + dialog.Buttons.Count;

    protected override int SelfRole => RoleDialog;

    protected override string? Name(int id) => id switch
    {
        0 => dialog.Name,
        1 => dialog.Description,
        _ => dialog.Buttons[id - 2].Text,
    };

    protected override string? Description(int id) => id == 0 ? dialog.Description : null;

    protected override int Role(int id) => id == 1 ? RoleStaticText : RolePushButton;

    protected override int State(int id)
    {
        if (id <= 1)
            return 0;
        int state = StateFocusable;
        if (dialog.FocusedButton == id - 2)
            state |= StateFocused | StateDefault;
        return state;
    }

    protected override Win32.RECT Location(int id) => id <= 1 ? dialog.PanelRect() : dialog.ButtonRect(id - 2);

    protected override int FocusedId => dialog.FocusedButton + 2;

    protected override void Select(int id)
    {
        if (id >= 2)
            dialog.FocusButton(id - 2);
    }

    protected override void DoDefault(int id)
    {
        if (id >= 2)
        {
            dialog.FocusButton(id - 2);
            Win32.PostMessage(dialog.Hwnd, Native.WM_KEYDOWN, Native.VK_RETURN, 0); // answered from the message loop, not inside the COM call
        }
    }
}
