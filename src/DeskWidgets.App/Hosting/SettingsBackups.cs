using System.Globalization;

namespace DeskWidgets.Hosting;

/// <summary>
/// The rules for the snapshots of settings.json kept in <c>&lt;data&gt;\backups\</c> (Phase 13.1): their names, when one is due, and which
/// ones to keep: the newest <see cref="Recent"/> plus the newest of each of the last <see cref="Days"/> days. Pure, so it can be tested.
/// </summary>
internal static class SettingsBackups
{
    /// <summary>How many of the newest snapshots are always kept.</summary>
    public const int Recent = 10;

    /// <summary>How many days back the newest snapshot of each day is kept too.</summary>
    public const int Days = 7;

    /// <summary>The least time between two snapshots made while settings change.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromHours(1);

    private const string Prefix = "settings-";
    private const string Format = "yyyyMMdd-HHmmss";

    /// <summary>The file name of a snapshot made at <paramref name="utc"/>.</summary>
    public static string NameFor(DateTime utc) => Prefix + utc.ToString(Format, CultureInfo.InvariantCulture) + ".json";

    /// <summary>The time a snapshot's name says, or false for a file that is not one of ours.</summary>
    public static bool TryParse(string fileName, out DateTime utc)
    {
        utc = default;
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) || !fileName.EndsWith(".json", StringComparison.Ordinal))
            return false;
        string stamp = fileName[Prefix.Length..^".json".Length];
        if (!DateTime.TryParseExact(stamp, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
            return false;
        return true;
    }

    /// <summary>Whether a snapshot is due now: none yet, or the last one is at least <see cref="MinGap"/> old.</summary>
    public static bool Due(DateTime? lastUtc, DateTime nowUtc) => lastUtc is null || nowUtc - lastUtc.Value >= MinGap;

    /// <summary>The snapshots to delete from <paramref name="all"/> (their times), keeping the newest few and the newest of each recent day.</summary>
    public static List<DateTime> ToDelete(IEnumerable<DateTime> all, DateTime nowUtc)
    {
        var newestFirst = all.OrderByDescending(t => t).ToList();
        var keep = new HashSet<DateTime>(newestFirst.Take(Recent));
        for (int back = 0; back < Days; back++)
        {
            var day = nowUtc.Date.AddDays(-back);
            var newestOfDay = newestFirst.FirstOrDefault(t => t.Date == day);
            if (newestOfDay != default)
                keep.Add(newestOfDay);
        }
        return newestFirst.Where(t => !keep.Contains(t)).ToList();
    }
}
