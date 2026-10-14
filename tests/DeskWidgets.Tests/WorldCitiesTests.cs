using DeskWidgets.Widgets.WorldClock;

namespace DeskWidgets.Tests;

public sealed class WorldCitiesTests : IDisposable
{
    // 09:35 in New York on 14 Oct 2026 (EDT, UTC−4).
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 13, 35, 0, TimeSpan.Zero);

    private readonly AppLanguage _language = Strings.Language;

    public WorldCitiesTests() => Strings.Language = AppLanguage.English;

    public void Dispose() => Strings.Language = _language;

    private static DateTimeOffset In(string windowsZone, DateTimeOffset? at = null)
        => TimeZoneInfo.ConvertTime(at ?? Now, TimeZoneInfo.FindSystemTimeZoneById(windowsZone));

    [Theory]
    [InlineData("China Standard Time", "Today +12h")]
    [InlineData("Pacific Standard Time", "Today −3h")]
    [InlineData("Newfoundland Standard Time", "Today +1.5h")]
    [InlineData("Eastern Standard Time", "Today +0h")]
    [InlineData("New Zealand Standard Time", "Tomorrow +17h")]
    public void Offset_IsTheCitysDayAndItsHoursFromLocalTime(string zone, string expected)
        => Assert.Equal(expected, WorldCities.DescribeOffset(In(zone), In("Eastern Standard Time")));

    [Fact]
    public void Offset_SaysYesterday_WhileTheCityIsStillOnTheDayBefore()
    {
        var justAfterMidnightInNewYork = new DateTimeOffset(2026, 10, 14, 4, 30, 0, TimeSpan.Zero);

        string offset = WorldCities.DescribeOffset(In("Hawaiian Standard Time", justAfterMidnightInNewYork), In("Eastern Standard Time", justAfterMidnightInNewYork));

        Assert.Equal("Yesterday −6h", offset);
    }

    [Fact]
    public void TimeIs24Hour_AndTheDateIsShort()
    {
        Assert.Equal("21:35", WorldCities.FormatTime(In("China Standard Time")));
        Assert.Equal("Oct 14", WorldCities.FormatDate(In("China Standard Time")));
    }

    [Fact]
    public void ChineseUi_UsesChineseDayWordsAndDates()
    {
        Strings.Language = AppLanguage.Chinese;

        Assert.Equal("今天 +12小时", WorldCities.DescribeOffset(In("China Standard Time"), In("Eastern Standard Time")));
        Assert.Equal("10月14日", WorldCities.FormatDate(In("China Standard Time")));
    }

    [Fact]
    public void EveryCatalogCity_HasARealTimeZone()
        => Assert.All(WorldCities.Catalog, city => Assert.NotEqual(TimeZoneInfo.Utc.Id, city.Zone.Id));

    [Fact]
    public void Resolve_FindsCatalogCities_AndNamesOtherZonesAfterTheirCity()
    {
        Assert.Same(WorldCities.Catalog.Single(c => c.Name == "Beijing"), WorldCities.Resolve("asia/shanghai"));

        var rome = WorldCities.Resolve("Europe/Rome");
        Assert.NotNull(rome);
        Assert.Equal("Rome", rome.Name);
        Assert.Equal("ROM", rome.Code);

        Assert.Null(WorldCities.Resolve("Mars/Olympus_Mons"));
    }
}
