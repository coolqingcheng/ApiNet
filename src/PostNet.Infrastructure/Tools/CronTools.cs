using Cronos;

namespace PostNet.Infrastructure.Tools;

public sealed record CronFieldDescription(string Name, string Value, string Meaning);
public sealed record CronAnalysis(string Expression, IReadOnlyList<CronFieldDescription> Fields, IReadOnlyList<DateTimeOffset> Occurrences);

public static class CronTools
{
    public static CronAnalysis Parse(string input, bool includeSeconds, DateTimeOffset from, TimeZoneInfo zone)
    {
        var fields = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var count = includeSeconds ? 6 : 5;
        if (fields.Length != count) throw new FormatException($"当前格式需要 {count} 段，实际为 {fields.Length} 段。支持 5 段或含秒的 6 段，不含年份。");
        // H requires a stable jitter seed; it has no single execution time in this tool.
        if (fields.Any(x => x.ToUpperInvariant().Replace("THU", "").Contains('H'))) throw new FormatException("暂不支持随机偏移 H，请使用确定的值、范围或步长。");
        var text = string.Join(' ', fields);
        CronExpression expression;
        try { expression = CronExpression.Parse(text, includeSeconds ? CronFormat.IncludeSeconds : CronFormat.Standard); }
        catch (CronFormatException exception) { throw new FormatException("Cron 表达式无效：" + exception.Message, exception); }
        string[] names = includeSeconds ? ["秒", "分", "时", "日", "月", "周"] : ["分", "时", "日", "月", "周"];
        var descriptions = fields.Select((value, index) => new CronFieldDescription(names[index], value, Describe(value, names[index]))).ToArray();
        var times = new List<DateTimeOffset>();
        var cursor = from;
        for (var i = 0; i < 10; i++)
        {
            var next = expression.GetNextOccurrence(cursor, zone, inclusive: false);
            if (next is null) break;
            times.Add(next.Value);
            cursor = next.Value;
        }
        return new(text, descriptions, times);
    }

    public static string Generate(bool includeSeconds, string second, string minute, string hour, string day, string month, string week)
    {
        string[] fields = includeSeconds ? [second, minute, hour, day, month, week] : [minute, hour, day, month, week];
        if (fields.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(char.IsWhiteSpace))) throw new FormatException("每个字段需要填写一个值，字段内不能包含空格。可填写 *、*/5、1,5、1-10 等。");
        var text = string.Join(' ', fields);
        // Validate with the same parser used for previews; no schedule is executed.
        Parse(text, includeSeconds, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        return text;
    }

    private static string Describe(string value, string name)
    {
        if (value is "*" or "?") return "任意" + name;
        if (value.Contains(',')) return "任选：" + string.Join("、", value.Split(',').Select(x => Describe(x, name)));
        if (value.Contains('/')) { var parts = value.Split('/'); return "在 " + (parts[0] == "*" ? "完整范围" : parts[0]) + " 内，每 " + parts[1] + " 个值取一次"; }
        if (name == "日" && value == "L") return "每月最后一天";
        if (name == "日" && value == "LW") return "每月最后一个工作日";
        if (name == "日" && value.EndsWith('W')) return "靠近 " + value[..^1] + " 日的工作日";
        if (name == "周" && value.Contains('#')) { var parts = value.Split('#'); return "每月第 " + parts[1] + " 个" + WeekName(parts[0]); }
        if (name == "周" && value.EndsWith('L')) return "每月最后一个" + WeekName(value[..^1]);
        if (value.Contains('-')) return "范围 " + value + "（含两端，反向范围跨周期）";
        return name == "周" ? WeekName(value) : "指定" + name + "：" + value;
    }

    private static string WeekName(string value) => value.ToUpperInvariant() switch
    {
        "0" or "7" or "SUN" => "周日", "1" or "MON" => "周一", "2" or "TUE" => "周二",
        "3" or "WED" => "周三", "4" or "THU" => "周四", "5" or "FRI" => "周五", "6" or "SAT" => "周六", _ => value
    };
}
