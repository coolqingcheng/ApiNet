using PostNet.Infrastructure.Tools;

namespace PostNet.Tests;

public class TimeAndCronToolsTests
{
    [Theory]
    [InlineData("0", false, "1970-01-01T00:00:00.000Z")]
    [InlineData("-1", false, "1969-12-31T23:59:59.000Z")]
    [InlineData("-1", true, "1969-12-31T23:59:59.999Z")]
    [InlineData("1709164800123", true, "2024-02-29T00:00:00.123Z")]
    public void TimestampsHandleUnitsLeapDaysAndNegativeValues(string input, bool milliseconds, string expected)
    {
        var date = TimeTools.FromTimestamp(input, milliseconds);
        Assert.Equal(DateTimeOffset.Parse(expected), date);
        Assert.Equal(long.Parse(input), milliseconds ? date.ToUnixTimeMilliseconds() : date.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("not a timestamp")]
    [InlineData("999999999999999999999999")]
    [InlineData("253402300800000")]
    public void InvalidOrOutOfRangeTimestampsHaveReadableErrors(string input) => Assert.Throws<FormatException>(() => TimeTools.FromTimestamp(input, true));

    [Fact]
    public void DatesUseTheSelectedZoneUnlessAnExplicitOffsetIsPresent()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var date = TimeTools.ParseDate("2024-02-29 08:00:00.123", zone);
        Assert.Equal(1709164800123, date.ToUnixTimeMilliseconds());
        Assert.Equal("2024-02-29 08:00:00.123 +08:00", TimeTools.FormatDate(date, zone));
        Assert.Equal(date, TimeTools.ParseDate("2024-02-29T00:00:00.123Z", zone));
        Assert.Equal(date, TimeTools.ParseDate("2024-02-29T01:00:00.123+01:00", zone));
        Assert.Equal(date, TimeTools.ParseDate(TimeTools.FormatDate(date, zone), TimeZoneInfo.Utc));
        Assert.Throws<FormatException>(() => TimeTools.ParseDate("2023-02-29 00:00:00", zone));
    }

    [Fact]
    public void AmbiguousAndSkippedLocalDatesRequireAnExplicitOffset()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2024, 1, 1), new(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test DST", TimeSpan.FromHours(-5), "Test DST", "Standard", "Daylight", [rule]);
        Assert.Contains("夏令时", Assert.Throws<FormatException>(() => TimeTools.ParseDate("2024-03-10 02:30:00", zone)).Message);
        Assert.Contains("重复", Assert.Throws<FormatException>(() => TimeTools.ParseDate("2024-11-03 01:30:00", zone)).Message);
        Assert.Equal(TimeSpan.FromHours(-4), TimeTools.ParseDate("2024-11-03T01:30:00-04:00", zone).Offset);
    }

    [Fact]
    public void CronPreviewsFiveAndSixFieldsFromTheNextMatchingInstant()
    {
        var from = new DateTimeOffset(2026, 11, 12, 0, 2, 0, TimeSpan.Zero);
        var minutes = CronTools.Parse("*/5 * * * *", false, from, TimeZoneInfo.Utc);
        Assert.Equal(10, minutes.Occurrences.Count);
        Assert.Equal(from.AddMinutes(3), minutes.Occurrences[0]);
        Assert.All(minutes.Occurrences, x => Assert.Equal(0, x.Minute % 5));
        var seconds = CronTools.Parse("*/30 * * * * *", true, from.AddSeconds(1), TimeZoneInfo.Utc);
        Assert.Equal(from.AddSeconds(30), seconds.Occurrences[0]);
        Assert.Equal("秒", seconds.Fields[0].Name);
        Assert.Contains("30", seconds.Fields[0].Meaning);
    }

    [Fact]
    public void CronHonorsTimeZonesNamedWeekdaysAndCombinedDayRules()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var from = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        var weekday = CronTools.Parse("0 9 * * THU", false, from, zone);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(8)), weekday.Occurrences[0]);
        Assert.Equal("周四", weekday.Fields[^1].Meaning);
        var combined = CronTools.Parse("0 0 13 * FRI", false, from, TimeZoneInfo.Utc);
        Assert.All(combined.Occurrences, x => { Assert.Equal(13, x.Day); Assert.Equal(DayOfWeek.Friday, x.DayOfWeek); });
        var lastDay = CronTools.Parse("0 0 L * *", false, new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), zone);
        Assert.Equal(29, lastDay.Occurrences[0].Day);
        Assert.Contains("最后一天", lastDay.Fields[2].Meaning);
    }

    [Fact]
    public void CronGenerationValidatesFieldsAndHandlesImpossibleDates()
    {
        Assert.Equal("0 9 * * MON-FRI", CronTools.Generate(false, "0", "0", "9", "*", "*", "MON-FRI"));
        Assert.Equal("*/30 * * * * *", CronTools.Generate(true, "*/30", "*", "*", "*", "*", "*"));
        Assert.Throws<FormatException>(() => CronTools.Generate(false, "0", "0 1", "9", "*", "*", "*"));
        Assert.Throws<FormatException>(() => CronTools.Parse("0 0 0 * * * 2026", true, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
        Assert.Throws<FormatException>(() => CronTools.Parse("60 * * * *", false, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
        Assert.Throws<FormatException>(() => CronTools.Parse("H * * * *", false, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
        Assert.Empty(CronTools.Parse("0 0 31 2 *", false, DateTimeOffset.UtcNow, TimeZoneInfo.Utc).Occurrences);
    }
}
