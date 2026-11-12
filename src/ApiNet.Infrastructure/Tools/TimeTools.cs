using System.Globalization;
using System.Text.RegularExpressions;

namespace ApiNet.Infrastructure.Tools;

public static partial class TimeTools
{
    public static DateTimeOffset FromTimestamp(string input, bool milliseconds)
    {
        if (!long.TryParse(input.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("请输入整数时间戳，并选择秒或毫秒。支持 1970 年之前的负数时间戳。");
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value); }
        catch (ArgumentOutOfRangeException) { throw new FormatException("时间戳超出日期范围（公元 1 至 9999 年）。"); }
    }

    public static DateTimeOffset ParseDate(string input, TimeZoneInfo zone)
    {
        input = input.Trim();
        if (OffsetSuffix().IsMatch(input))
        {
            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var withOffset)) return withOffset;
        }
        else if (DateTime.TryParseExact(input, ["yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(date)) throw new FormatException("此日期位于夏令时跳过的时段，请填写有效时间或明确的 UTC 偏移。");
            if (zone.IsAmbiguousTime(date)) throw new FormatException("此日期位于夏令时重复的时段，请附加 UTC 偏移，例如 -04:00 或 -05:00。");
            return new DateTimeOffset(date, zone.GetUtcOffset(date));
        }
        throw new FormatException("日期格式无效，请使用 yyyy-MM-dd HH:mm:ss.fff，或带 Z / +08:00 等偏移的 ISO 8601 日期。");
    }

    public static string FormatDate(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?:[zZ]|[+-]\d{2}:?\d{2})$")]
    private static partial Regex OffsetSuffix();
}
