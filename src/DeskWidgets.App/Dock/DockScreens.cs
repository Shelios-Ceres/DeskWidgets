namespace DeskWidgets.Dock;

/// <summary>What becomes of a saved dock when its screen is, or is not, there (B2-3): one dock per screen, and no program is ever dropped from the settings.</summary>
internal enum DockFate
{
    /// <summary>Open it on its screen.</summary>
    Open,

    /// <summary>Its screen is gone and the screen it falls back to is taken: keep the settings, open it again when its screen comes back.</summary>
    Dormant,

    /// <summary>Saved for the same screen as an earlier dock: its programs go into that dock.</summary>
    Merge,
}

internal static class DockScreens
{
    /// <summary>
    /// For each saved dock (by its saved screen name, null = the primary) the fate on these screens. A dock whose screen exists comes first;
    /// a dock whose screen is absent falls back to the primary screen and is dormant when that is taken. Two docks saved for the same screen
    /// merge. <paramref name="target"/> is the index of the dock a merged one goes into (else -1).
    /// </summary>
    public static (DockFate Fate, int Target)[] Plan(IReadOnlyList<string?> saved, IReadOnlyList<string> screens, string primary)
    {
        var result = new (DockFate, int)[saved.Count];
        string Present(string? name) => screens.FirstOrDefault(s => string.Equals(s, name ?? primary, StringComparison.OrdinalIgnoreCase)) ?? "";
        var order = Enumerable.Range(0, saved.Count).OrderBy(i => Present(saved[i]).Length == 0 ? 1 : 0).ThenBy(i => i).ToList();
        var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (int i in order)
        {
            string screen = Present(saved[i]);
            if (screen.Length == 0)
                screen = primary;
            if (!taken.TryGetValue(screen, out int owner))
            {
                taken[screen] = i;
                result[i] = (DockFate.Open, -1);
            }
            else if (string.Equals(saved[owner] ?? primary, saved[i] ?? primary, StringComparison.OrdinalIgnoreCase))
            {
                result[i] = (DockFate.Merge, owner);
            }
            else
            {
                result[i] = (DockFate.Dormant, -1);
            }
        }
        return result;
    }
}
