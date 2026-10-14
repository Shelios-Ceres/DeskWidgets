using System.Globalization;

namespace DeskWidgets.Widgets.WorldClock;

/// <summary>A city on the world clock and its time zone.</summary>
internal sealed class WorldCity
{
    private readonly string[] _zoneIds;
    private TimeZoneInfo? _zone;

    public WorldCity(string id, string name, string code, params string[] zoneIds)
    {
        Id = id;
        Name = name;
        Code = code;
        _zoneIds = zoneIds;
    }

    /// <summary>IANA time zone id; what the widget's options store.</summary>
    public string Id { get; }

    /// <summary>English name, whatever the UI language.</summary>
    public string Name { get; }

    /// <summary>Three-letter abbreviation for tight layouts (mostly the airport code, e.g. "NYC", "PEK").</summary>
    public string Code { get; }

    public TimeZoneInfo Zone => _zone ??= WorldCities.FindZone(_zoneIds) ?? TimeZoneInfo.Utc;
}

internal static class WorldCities
{
    /// <summary>
    /// Cities offered in the menu, west to east. Each names its Windows time
    /// zone, which always exists, rather than relying on ICU to map IANA ids.
    /// </summary>
    public static IReadOnlyList<WorldCity> Catalog { get; } =
    [
        new("Pacific/Honolulu", "Honolulu", "HNL", "Hawaiian Standard Time"),
        new("America/Anchorage", "Anchorage", "ANC", "Alaskan Standard Time"),
        new("America/Los_Angeles", "Los Angeles", "LAX", "Pacific Standard Time"),
        new("America/Vancouver", "Vancouver", "VAN", "Pacific Standard Time"),
        new("America/Denver", "Denver", "DEN", "Mountain Standard Time"),
        new("America/Chicago", "Chicago", "CHI", "Central Standard Time"),
        new("America/Mexico_City", "Mexico City", "MEX", "Central Standard Time (Mexico)"),
        new("America/New_York", "New York", "NYC", "Eastern Standard Time"),
        new("America/Toronto", "Toronto", "TOR", "Eastern Standard Time"),
        new("America/Halifax", "Halifax", "HFX", "Atlantic Standard Time"),
        new("America/St_Johns", "St. John's", "STJ", "Newfoundland Standard Time"),
        new("America/Sao_Paulo", "São Paulo", "SAO", "E. South America Standard Time"),
        new("Europe/London", "London", "LON", "GMT Standard Time"),
        new("Europe/Paris", "Paris", "PAR", "Romance Standard Time"),
        new("Europe/Berlin", "Berlin", "BER", "W. Europe Standard Time"),
        new("Europe/Moscow", "Moscow", "MOS", "Russian Standard Time"),
        new("Asia/Dubai", "Dubai", "DXB", "Arabian Standard Time"),
        new("Asia/Kolkata", "New Delhi", "DEL", "India Standard Time"),
        new("Asia/Singapore", "Singapore", "SIN", "Singapore Standard Time"),
        new("Asia/Shanghai", "Beijing", "PEK", "China Standard Time"),
        new("Asia/Hong_Kong", "Hong Kong", "HKG", "China Standard Time"),
        new("Asia/Taipei", "Taipei", "TPE", "Taipei Standard Time"),
        new("Asia/Tokyo", "Tokyo", "TYO", "Tokyo Standard Time"),
        new("Asia/Seoul", "Seoul", "SEL", "Korea Standard Time"),
        new("Australia/Sydney", "Sydney", "SYD", "AUS Eastern Standard Time"),
        new("Pacific/Auckland", "Auckland", "AKL", "New Zealand Standard Time"),
    ];

    /// <summary>New York, Los Angeles, St. John's (Newfoundland) and Beijing.</summary>
    public static IReadOnlyList<string> DefaultIds { get; } =
        ["America/New_York", "America/Los_Angeles", "America/St_Johns", "Asia/Shanghai"];

    /// <summary>
    /// A catalog city, or for any other zone id (e.g. hand-edited settings) a
    /// city named after the id's last segment; null when the zone is unknown.
    /// </summary>
    public static WorldCity? Resolve(string id)
    {
        var known = Catalog.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (known is not null)
            return known;
        if (FindZone([id]) is null)
            return null;
        string name = id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
        return new WorldCity(id, name, name[..Math.Min(3, name.Length)].ToUpperInvariant(), id);
    }

    public static TimeZoneInfo? FindZone(IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return null;
    }

    /// <summary>The city's time on the 24-hour clock: "20:20".</summary>
    public static string FormatTime(DateTimeOffset cityTime) => cityTime.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The city's date: "Oct 13" / "10月13日".</summary>
    public static string FormatDate(DateTimeOffset cityTime) => cityTime.ToString(Strings.CityDatePattern, Strings.Culture);

    /// <summary>"今天 −12小时", "Tomorrow +2.5h": the city's day as seen from here and its offset from local time.</summary>
    public static string DescribeOffset(DateTimeOffset cityTime, DateTimeOffset localTime)
    {
        int days = DaysApart(cityTime, localTime);
        string day = days < 0 ? Strings.Yesterday : days > 0 ? Strings.Tomorrow : Strings.Today;
        double hours = (cityTime.Offset - localTime.Offset).TotalHours;
        string amount = (hours < 0 ? "−" : "+") + Math.Abs(hours).ToString("0.##", CultureInfo.InvariantCulture);
        return Strings.DayAndOffset(day, amount);
    }

    private static int DaysApart(DateTimeOffset cityTime, DateTimeOffset localTime) => (cityTime.Date - localTime.Date).Days;
}
