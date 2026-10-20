using System.Globalization;

namespace RangeReservationPoc.Services;

public readonly record struct Occurrence(DateTime Start, DateTime End);

/// <summary>
/// Server-side expansion of the RFC 5545 RRULE strings produced by the Syncfusion RecurrenceEditor,
/// so that booking caps, conflicts and blackouts can be validated authoritatively on the server.
/// Supports FREQ=DAILY|WEEKLY|MONTHLY|YEARLY with INTERVAL, COUNT, UNTIL, BYDAY (incl. ordinals),
/// BYMONTHDAY, BYMONTH and BYSETPOS, plus exception dates (RecurrenceException / EXDATE).
/// All date-times are facility wall-clock time.
/// </summary>
public static class RecurrenceExpander
{
    private static readonly Dictionary<string, DayOfWeek> DayMap = new()
    {
        ["SU"] = DayOfWeek.Sunday, ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday,
        ["WE"] = DayOfWeek.Wednesday, ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday,
        ["SA"] = DayOfWeek.Saturday
    };

    public sealed class RRule
    {
        public string Freq { get; init; } = "DAILY";
        public int Interval { get; init; } = 1;
        public int? Count { get; init; }
        public DateTime? Until { get; init; }
        public List<(int Ordinal, DayOfWeek Day)> ByDay { get; init; } = new();
        public List<int> ByMonthDay { get; init; } = new();
        public List<int> ByMonth { get; init; } = new();
        public int? BySetPos { get; init; }
        public bool IsInfinite => Count is null && Until is null;
    }

    public static RRule Parse(string rule)
    {
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var byDay = new List<(int, DayOfWeek)>();
        if (parts.TryGetValue("BYDAY", out var bd))
        {
            foreach (var token in bd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var code = token[^2..].ToUpperInvariant();
                var ord = token.Length > 2 ? int.Parse(token[..^2], CultureInfo.InvariantCulture) : 0;
                if (DayMap.TryGetValue(code, out var dow)) byDay.Add((ord, dow));
            }
        }

        return new RRule
        {
            Freq = parts.GetValueOrDefault("FREQ", "DAILY").ToUpperInvariant(),
            Interval = parts.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var i) && i > 0 ? i : 1,
            Count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, out var ci) ? ci : null,
            Until = parts.TryGetValue("UNTIL", out var u) ? ParseRecurrenceDate(u) : null,
            ByDay = byDay,
            ByMonthDay = ParseIntList(parts.GetValueOrDefault("BYMONTHDAY")),
            ByMonth = ParseIntList(parts.GetValueOrDefault("BYMONTH")),
            BySetPos = parts.TryGetValue("BYSETPOS", out var sp) && int.TryParse(sp, out var spi) ? spi : null
        };
    }

    /// <summary>Parses yyyyMMdd or yyyyMMddTHHmmss[Z] (treated as facility wall-clock).</summary>
    public static DateTime? ParseRecurrenceDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().TrimEnd('Z', 'z');
        string[] formats = { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
        return DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static string ToRecurrenceDate(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static HashSet<DateTime> ParseExceptions(string? exceptions) =>
        (exceptions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseRecurrenceDate).Where(d => d.HasValue).Select(d => d!.Value).ToHashSet();

    /// <summary>Expands occurrences that overlap [windowStart, windowEnd).</summary>
    public static IEnumerable<Occurrence> Expand(DateTime start, DateTime end, string? rule, string? exceptions,
        DateTime windowStart, DateTime windowEnd, int maxOccurrences = 5000)
    {
        var duration = end - start;
        if (string.IsNullOrWhiteSpace(rule))
        {
            if (start < windowEnd && end > windowStart) yield return new Occurrence(start, end);
            yield break;
        }

        var rr = Parse(rule);
        var exdates = ParseExceptions(exceptions);
        int generated = 0, yielded = 0;

        foreach (var occStart in Generate(start, rr))
        {
            if (rr.Until is { } until && occStart.Date > until.Date) yield break;
            if (rr.Count is { } count && generated >= count) yield break;
            generated++;
            if (occStart >= windowEnd) yield break;
            if (exdates.Contains(occStart)) continue;
            var occEnd = occStart + duration;
            if (occEnd > windowStart)
            {
                yield return new Occurrence(occStart, occEnd);
                if (++yielded >= maxOccurrences) yield break;
            }
        }
    }

    /// <summary>Number of occurrences (after exceptions), or null when the rule never ends.</summary>
    public static int? CountOccurrences(DateTime start, DateTime end, string? rule, string? exceptions)
    {
        if (string.IsNullOrWhiteSpace(rule)) return 1;
        if (Parse(rule).IsInfinite) return null;
        return Expand(start, end, rule, exceptions, DateTime.MinValue, DateTime.MaxValue, 100_000).Count();
    }

    /// <summary>Infinite candidate stream (bounded by a safety limit) in chronological order.</summary>
    private static IEnumerable<DateTime> Generate(DateTime start, RRule rr)
    {
        var time = start.TimeOfDay;
        const int safetyPeriods = 20_000;

        for (int k = 0; k < safetyPeriods; k++)
        {
            IEnumerable<DateTime> dates;
            switch (rr.Freq)
            {
                case "DAILY":
                    dates = new[] { start.Date.AddDays((double)k * rr.Interval) };
                    break;
                case "WEEKLY":
                {
                    // Syncfusion Scheduler's default first day of week is Sunday
                    var weekStart = start.Date.AddDays(-(int)start.DayOfWeek).AddDays(7.0 * k * rr.Interval);
                    var days = rr.ByDay.Count > 0 ? rr.ByDay.Select(d => d.Day).Distinct() : new[] { start.DayOfWeek };
                    dates = days.Select(d => weekStart.AddDays((int)d)).OrderBy(d => d);
                    break;
                }
                case "MONTHLY":
                {
                    var month = new DateTime(start.Year, start.Month, 1).AddMonths(k * rr.Interval);
                    dates = DatesInMonth(month, rr, start.Day);
                    break;
                }
                case "YEARLY":
                {
                    var year = start.Year + k * rr.Interval;
                    if (year > 9998) yield break;
                    var months = rr.ByMonth.Count > 0 ? rr.ByMonth.OrderBy(m => m) : new List<int> { start.Month }.AsEnumerable();
                    dates = months.SelectMany(m => DatesInMonth(new DateTime(year, m, 1), rr, start.Day)).ToList();
                    break;
                }
                default:
                    yield break;
            }

            foreach (var d in dates)
            {
                var occ = d + time;
                if (occ >= start) yield return occ;
            }
        }
    }

    private static IEnumerable<DateTime> DatesInMonth(DateTime monthStart, RRule rr, int defaultDay)
    {
        int dim = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var result = new List<DateTime>();

        if (rr.ByMonthDay.Count > 0)
        {
            foreach (var md in rr.ByMonthDay)
            {
                var day = md > 0 ? md : dim + md + 1;
                if (day >= 1 && day <= dim) result.Add(monthStart.AddDays(day - 1));
            }
        }
        else if (rr.ByDay.Count > 0)
        {
            var all = Enumerable.Range(0, dim).Select(i => monthStart.AddDays(i)).ToList();
            foreach (var (ord, dow) in rr.ByDay)
            {
                var matches = all.Where(d => d.DayOfWeek == dow).ToList();
                var pos = ord != 0 ? ord : rr.BySetPos ?? 0;
                if (pos == 0) result.AddRange(matches);
                else
                {
                    var idx = pos > 0 ? pos - 1 : matches.Count + pos;
                    if (idx >= 0 && idx < matches.Count) result.Add(matches[idx]);
                }
            }
        }
        else if (defaultDay <= dim)
        {
            result.Add(monthStart.AddDays(defaultDay - 1));
        }

        return result.Distinct().OrderBy(d => d);
    }

    private static List<int> ParseIntList(string? v) =>
        (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n != 0).ToList();
}
