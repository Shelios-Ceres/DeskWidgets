using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Motion;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Dock;

/// <summary>Opening files with a dock app (Build C, PROPOSAL 3.6): files dragged over the dock light the apps that registered for their types.</summary>
internal sealed partial class DockController
{
    /// <summary>
    /// One drag of files. The handlers are asked once, on the worker, when the drag comes in (64 to 79 ms per type) and are only used there;
    /// the UI thread works with the answer: which programs on the bar each handler stands for.
    /// </summary>
    private sealed class OpenWithSession(int id, List<string> paths)
    {
        public int Id { get; } = id;

        public List<string> Paths { get; } = paths;

        /// <summary>Program key to its handler, filled by the worker. Null until the answer is in.</summary>
        public Dictionary<string, DockHandler>? ByKey { get; set; }

        public IReadOnlyList<DockHandler> All { get; set; } = [];

        public bool Over { get; set; }
    }

    private OpenWithSession? _session;
    private int _sessionCounter;
    private readonly DockHold _owHold = new();
    private FrameClock.Ticket? _owTimer;
    private FrameClock.Ticket? _owLeave;
    private int _owHovered = -1;
    private int _owPanelHovered = -1;

    /// <summary>A drag of files that are not programs came in (the dock's own drop target calls this). Asks the handlers, once per drag, off the UI thread.</summary>
    internal void BeginOpenWith(IReadOnlyList<string> paths)
    {
        _owLeave?.Stop();
        _owLeave = null;
        if (_session is not null && _session.Paths.SequenceEqual(paths))
            return; // the same drag came from the panel back to the bar
        EndOpenWithNow();
        if (_surface is null || paths.Count == 0)
            return;
        var session = new OpenWithSession(++_sessionCounter, [.. paths]);
        _session = session;
        var programs = DockGroups.Programs(Settings.Items).Select(item => item.Copy()).ToList();
        var extensions = DockOpenWith.ExtensionsOf(paths);
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            var byKey = new Dictionary<string, DockHandler>();
            IReadOnlyList<DockHandler> all = [];
            try
            {
                all = DockOpenWith.Enumerate(extensions);
                foreach (var program in programs)
                {
                    if (all.FirstOrDefault(handler => DockOpenWith.Matches(program, handler)) is { } handler)
                        byKey[program.Key] = handler;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Asking which apps open these files failed", ex);
            }
            ui.TryEnqueue(() =>
            {
                if (_disposed || _session != session)
                {
                    _worker.Post(() => DockOpenWith.Release(all));
                    return;
                }
                session.ByKey = byKey;
                session.All = all;
                if (session.Over)
                    ShowOpenWith();
            });
        });
    }

    /// <summary>Whether the program is lit: the files' types are registered for it.</summary>
    private bool Lit(DockItem program) => _session?.ByKey is { } byKey && byKey.ContainsKey(program.Key);

    /// <summary>A program lights when it takes the files; a group when at least one of its members does (its panel opens for the drop).</summary>
    private bool LitIcon(DockItem item) => item.IsGroup ? item.Children!.Any(Lit) : Lit(item);

    /// <summary>The pointer is at a screen point during a drag of files: show what is lit and which app is under it (D12).</summary>
    internal void OpenWithOver(int screenX, int screenY)
    {
        if (_session is null || _surface is null)
            return;
        _owLeave?.Stop();
        _owLeave = null;
        CancelLeaveHide();
        _session.Over = true;
        _dropping = true;
        double x = (screenX - _layout.Bar.X) / PxPerGeometryDip, y = (screenY - _layout.Bar.Y) / PxPerGeometryDip;
        int hovered = y >= 0 && y <= DockGeometry.BarHeightDip(Settings.IconSize) ? DockGeometry.IconAt(Settings.IconSize, Settings.Items.Count, x) : -1;
        // Over a group with a member that takes the files, the panel opens after a moment, so that member can take the drop (D12).
        if (_owHold.Over(hovered >= 0 && Settings.Items[hovered].IsGroup && LitIcon(Settings.Items[hovered]) ? hovered : -1, Now))
        {
            _owTimer?.Stop();
            _owTimer = null;
            if (_owHold.DueAt is { } due && !(_surface.PanelOpen && _openGroup == Settings.Items[_owHold.Target]))
            {
                _owTimer = FrameClock.After((int)Math.Max(1, (due - Now).TotalMilliseconds), () =>
                {
                    if (_disposed || _session is null || !_owHold.Ready(Now))
                        return;
                    ShowPanelFor(_owHold.Target, forDrop: true);
                    ShowOpenWith();
                });
            }
        }
        if (hovered != _owHovered)
        {
            _owHovered = hovered;
            ShowOpenWith();
        }
    }

    internal bool OpenWithAccepts(int screenX, int screenY)
    {
        double x = (screenX - _layout.Bar.X) / PxPerGeometryDip;
        int index = DockGeometry.IconAt(Settings.IconSize, Settings.Items.Count, x);
        return index >= 0 && !Settings.Items[index].IsGroup && Lit(Settings.Items[index]);
    }

    private void ShowOpenWith()
    {
        if (_session?.ByKey is null || _surface is null)
            return;
        var lit = Settings.Items.Select(LitIcon).ToList();
        _surface.SetOpenWith(lit, _owHovered >= 0 && _owHovered < lit.Count && lit[_owHovered] ? _owHovered : -1);
        if (_surface.PanelOpen && _openGroup is not null)
            ShowPanelOpenWith();
    }

    private void ShowPanelOpenWith()
    {
        if (_openGroup is null || _surface is null)
            return;
        var lit = _openGroup.Children!.Select(Lit).ToList();
        _surface.SetPanelOpenWith(lit, _owPanelHovered >= 0 && _owPanelHovered < lit.Count && lit[_owPanelHovered] ? _owPanelHovered : -1);
    }

    /// <summary>The pointer is over the open panel during the drag (its own drop target calls this).</summary>
    internal void OpenWithOverPanel(int screenX, int screenY)
    {
        if (_session is null || _surface is not { PanelOpen: true })
            return;
        _owLeave?.Stop();
        _owLeave = null;
        _session.Over = true;
        _dropping = true;
        int member = _surface.PanelMemberAt(screenX, screenY);
        if (member != _owPanelHovered)
        {
            _owPanelHovered = member;
            ShowPanelOpenWith();
        }
    }

    internal bool PanelOpenWithAccepts(int screenX, int screenY)
    {
        if (_surface is not { PanelOpen: true } || _openGroup is null)
            return false;
        int member = _surface.PanelMemberAt(screenX, screenY);
        return member >= 0 && member < _openGroup.Children!.Count && Lit(_openGroup.Children[member]);
    }

    /// <summary>The drag left a window of the dock. Over the panel the drag goes on, so the session stays a moment; elsewhere it ends.</summary>
    internal void OpenWithLeft(bool fromPanel)
    {
        if (_session is null)
            return;
        if (!fromPanel && _surface is { PanelOpen: true } && _openGroup is not null)
        {
            _owLeave?.Stop();
            _owLeave = FrameClock.After(400, () =>
            {
                _owLeave = null;
                EndOpenWithNow();
                if (_surface is { PanelOpen: true })
                    HidePanel(animated: true);
            });
            return;
        }
        EndOpenWithNow();
        if (fromPanel && _surface is { PanelOpen: true })
            HidePanel(animated: true);
    }

    private void EndOpenWithNow()
    {
        _owTimer?.Stop();
        _owTimer = null;
        _owHold.Clear();
        _owHovered = -1;
        _owPanelHovered = -1;
        var session = _session;
        _session = null;
        _dropping = false;
        if (session is null)
            return;
        _surface?.SetOpenWith(null, -1);
        if (_surface is { PanelOpen: true })
            _surface.SetPanelOpenWith(null, -1);
        var all = session.All;
        _worker.Post(() => DockOpenWith.Release(all));
        ScheduleLeaveHide();
    }

    /// <summary>The files were dropped on the bar at a screen point: open them with the lit app under the pointer. Anything else does nothing.</summary>
    internal void DropFiles(int screenX, int screenY)
    {
        var session = _session;
        double x = (screenX - _layout.Bar.X) / PxPerGeometryDip;
        int index = DockGeometry.IconAt(Settings.IconSize, Settings.Items.Count, x);
        var item = index >= 0 ? Settings.Items[index] : null;
        bool canOpen = item is { IsGroup: false } && Lit(item);
        var action = DockDrop.Decide(allPrograms: false, index, canOpen);
        if (action == DockDropAction.OpenWith && session is not null && item is not null)
            OpenSessionWith(session, item);
        EndAfterDrop(session);
    }

    /// <summary>The files were dropped on a member of the open panel.</summary>
    internal void OpenDroppedWith(DockItem member)
    {
        var session = _session;
        if (session is not null && Lit(member))
        {
            HidePanel(animated: true);
            OpenSessionWith(session, member);
        }
        EndAfterDrop(session);
    }

    private void EndAfterDrop(OpenWithSession? session)
    {
        if (session is not null && _session == session)
        {
            _owLeave?.Stop();
            _owLeave = null;
            EndOpenWithNow();
        }
        if (_surface is { PanelOpen: true } && _openGroup is not null)
            HidePanel(animated: true);
    }

    /// <summary>Opens the session's files with the handler of <paramref name="app"/> on the worker, then the app's opening state (D3) starts.</summary>
    private void OpenSessionWith(OpenWithSession session, DockItem app)
    {
        if (session.ByKey is null || !session.ByKey.TryGetValue(app.Key, out var handler))
            return;
        bool tracks = app.SupportsRunning && _runStates.Started(app.Key, Now);
        if (tracks)
        {
            ApplyRunState(app.Key, DockRunState.Opening, DockRunTransition.Opening);
            ScheduleExpiry();
        }
        var paths = session.Paths;
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            string? error = DockOpenWith.Invoke(handler, paths);
            ui.TryEnqueue(() =>
            {
                if (_disposed)
                    return;
                if (error is not null)
                {
                    if (tracks && _runStates.Cancel(app.Key))
                        ApplyRunState(app.Key, DockRunState.Idle, DockRunTransition.TimedOut);
                    Report(error);
                }
            });
        });
    }
}
