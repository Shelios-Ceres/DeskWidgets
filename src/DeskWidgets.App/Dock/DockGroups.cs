namespace DeskWidgets.Dock;

/// <summary>
/// Groups on the bar (PROPOSAL 3.3), as plain list edits with no window: making one, adding to one, taking a program out, ungrouping, and the
/// rule that a group always holds at least two programs. Every method changes the list it is given and says where things went.
/// </summary>
internal static class DockGroups
{
    /// <summary>Every program on the bar, the members of groups included, in order.</summary>
    public static IEnumerable<DockItem> Programs(IEnumerable<DockItem> items)
        => items.SelectMany(item => item.IsGroup ? item.Children! : [item]);

    /// <summary>Whether a program with this key is on the bar, alone or in a group.</summary>
    public static bool Contains(IEnumerable<DockItem> items, string key) => Programs(items).Any(item => item.Key == key);

    /// <summary>
    /// Drops the empty groups and turns a group of one into its program, as at every change and when the settings are read (a hand-edited file
    /// may hold anything). A group inside a group is flattened into the outer one. Returns whether anything changed.
    /// </summary>
    public static bool Normalize(List<DockItem> items)
    {
        bool changed = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (!item.IsGroup)
                continue;
            var members = item.Children!;
            for (int c = members.Count - 1; c >= 0; c--)
            {
                if (!members[c].IsGroup)
                    continue;
                var inner = members[c].Children!;
                members.RemoveAt(c);
                members.InsertRange(c, inner);
                changed = true;
            }
            if (string.IsNullOrEmpty(item.GroupId))
            {
                item.GroupId = Guid.NewGuid().ToString("N");
                changed = true;
            }
            if (members.Count > 1)
                continue;
            items.RemoveAt(i);
            if (members.Count == 1)
                items.Insert(i, members[0]);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// The program at <paramref name="dragged"/> was held over the item at <paramref name="target"/>: over a program they become a group (at the
    /// target's place, the target first); over a group the program joins it at the end. A group cannot be dragged into anything. Returns the
    /// index of the group in the list afterwards, or -1 when nothing changed.
    /// </summary>
    public static int MakeGroup(List<DockItem> items, int dragged, int target)
    {
        if (dragged == target || dragged < 0 || target < 0 || dragged >= items.Count || target >= items.Count || items[dragged].IsGroup)
            return -1;
        var program = items[dragged];
        var over = items[target];
        if (over.IsGroup)
        {
            over.Children!.Add(program);
            items.RemoveAt(dragged);
            return items.IndexOf(over);
        }
        var group = new DockItem { Name = "", GroupId = Guid.NewGuid().ToString("N"), Children = [over, program] };
        items[target] = group;
        items.RemoveAt(dragged);
        return items.IndexOf(group);
    }

    /// <summary>How far from an icon's middle (as a share of its size) a dragged icon still counts as "over" it for grouping.</summary>
    public const double TargetZone = 0.3;

    /// <summary>
    /// The icon a dragged icon is held over for grouping: one other than the dragged one whose middle zone holds <paramref name="x"/> (from the
    /// bar's left, DIPs), or -1. Outside the zones the icons reorder instead, so the two never fight: reordering happens when the pointer passes
    /// an icon's middle, which lies inside its zone, and the zone suppresses it.
    /// </summary>
    public static int TargetAt(DockIconSize size, int count, double x, int dragged)
    {
        double icon = DockGeometry.IconDip(size);
        for (int i = 0; i < count; i++)
        {
            if (i == dragged)
                continue;
            double middle = DockGeometry.IconLeftDip(size, i) + icon / 2;
            if (Math.Abs(x - middle) <= icon * TargetZone)
                return i;
        }
        return -1;
    }

    /// <summary>A program from outside the bar was dropped on a group: it joins at the end (unless it is already on the bar). Returns whether it was added.</summary>
    public static bool AddToGroup(List<DockItem> items, int group, DockItem program)
    {
        if (group < 0 || group >= items.Count || !items[group].IsGroup || program.IsGroup || Contains(items, program.Key))
            return false;
        items[group].Children!.Add(program);
        return true;
    }

    /// <summary>
    /// Takes the member at <paramref name="child"/> out of the group at <paramref name="group"/> and puts it on the bar at <paramref name="slot"/>
    /// (a slot counted in the list as it is before the call, -1 or past the end: the end). A group left with one program becomes that program.
    /// Returns the member's index on the bar afterwards, or -1.
    /// </summary>
    public static int TakeOut(List<DockItem> items, int group, int child, int slot)
    {
        if (group < 0 || group >= items.Count || !items[group].IsGroup)
            return -1;
        var members = items[group].Children!;
        if (child < 0 || child >= members.Count)
            return -1;
        var program = members[child];
        members.RemoveAt(child);
        int at = slot < 0 || slot > items.Count ? items.Count : slot;
        items.Insert(at, program);
        // The group sits before the slot unless the slot is at or before it: normalizing may remove or replace it.
        Normalize(items);
        return items.IndexOf(program);
    }

    /// <summary>Ungroup: the group's programs take its place, in order. Returns how many there were.</summary>
    public static int Ungroup(List<DockItem> items, int group)
    {
        if (group < 0 || group >= items.Count || !items[group].IsGroup)
            return 0;
        var members = items[group].Children!;
        items.RemoveAt(group);
        items.InsertRange(group, members);
        return members.Count;
    }

    /// <summary>Reorders inside a group: the member at <paramref name="from"/> goes to <paramref name="to"/>.</summary>
    public static bool Reorder(DockItem group, int from, int to)
    {
        if (!group.IsGroup)
            return false;
        var members = group.Children!;
        if (from < 0 || from >= members.Count || to < 0 || to >= members.Count || from == to)
            return false;
        var program = members[from];
        members.RemoveAt(from);
        members.Insert(to, program);
        return true;
    }

    /// <summary>"Open all": the programs to start, which are those not running (<paramref name="isRunning"/> by key), in order, each once.</summary>
    public static List<DockItem> OpenAll(DockItem group, Func<string, bool> isRunning)
    {
        var started = new HashSet<string>();
        var result = new List<DockItem>();
        foreach (var program in group.Children ?? [])
        {
            if (!isRunning(program.Key) && started.Add(program.Key))
                result.Add(program);
        }
        return result;
    }

    /// <summary>The state a group icon shows: running while any member is (Pixel's halo), opening while any member is opening and none runs, else idle.</summary>
    public static DockRunState GroupState(IEnumerable<DockRunState> members)
    {
        var states = members.ToList();
        return states.Contains(DockRunState.Running) ? DockRunState.Running : states.Contains(DockRunState.Opening) ? DockRunState.Opening : DockRunState.Idle;
    }
}

/// <summary>
/// "Held for a moment over a target" (a group forming, a group's panel opening for a drag), as state and a clock, so the controller only has to
/// start one timer for <see cref="DueAt"/>. Moving to another target starts the wait again.
/// </summary>
internal sealed class DockHold
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private TimeSpan _since;

    /// <summary>The held target's index, or -1.</summary>
    public int Target { get; private set; } = -1;

    /// <summary>The pointer is over <paramref name="target"/> (-1: over nothing). Returns whether the target changed.</summary>
    public bool Over(int target, TimeSpan now)
    {
        if (target == Target)
            return false;
        Target = target;
        _since = now;
        return true;
    }

    public void Clear() => Target = -1;

    public bool Ready(TimeSpan now) => Target >= 0 && now - _since >= Delay;

    /// <summary>When the hold will be long enough, or null when nothing is held.</summary>
    public TimeSpan? DueAt => Target >= 0 ? _since + Delay : null;
}

/// <summary>What a drop on the dock does (PROPOSAL 3.5, 3.6): a program is added, any other file opens with the app it is dropped on, and a gap does nothing.</summary>
internal enum DockDropAction
{
    Nothing,

    /// <summary>The program is added at the slot under the pointer (dropped on a group: it joins it).</summary>
    AddProgram,

    /// <summary>The files open with the app under the pointer.</summary>
    OpenWith,
}

internal static class DockDrop
{
    /// <summary>
    /// <paramref name="allPrograms"/>: every dropped item is a program (an exe, a shortcut to one, a Store app, a Steam link) and there is at
    /// least one; <paramref name="overIcon"/>: the index of the icon under the pointer (-1: over a gap or the bar's padding);
    /// <paramref name="canOpen"/>: that icon is an app that registered for the files' types.
    /// </summary>
    public static DockDropAction Decide(bool allPrograms, int overIcon, bool canOpen)
    {
        if (allPrograms)
            return DockDropAction.AddProgram;
        return overIcon >= 0 && canOpen ? DockDropAction.OpenWith : DockDropAction.Nothing;
    }
}
