namespace Offload.Crm.Application.Common;

/// <summary>The app's local time zone (Europe/Bratislava, D-11) for "today" and date-range filters.</summary>
public static class AppTime
{
    public static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>UTC instant of local midnight at the start of <paramref name="date"/>.</summary>
    public static DateTime StartOfDayUtc(DateOnly date) => LocalToUtc(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// Converts a local (Europe/Bratislava) wall-clock time to UTC (D-30). A time in the spring-forward gap (e.g.
    /// 29.03.2026 02:30, which doesn't exist) is shifted forward by the gap (→ 03:30), as calendar apps do (BUG-029).
    /// An ambiguous autumn time (e.g. 25.10.2026 02:30) is taken as standard time (the second occurrence).
    /// </summary>
    public static DateTime LocalToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (!Zone.IsInvalidTime(unspecified))
        {
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
        }

        // Use the offset in effect before the gap: 02:30 at UTC+1 = 01:30 UTC = 03:30 at UTC+2.
        var offsetBefore = Zone.GetUtcOffset(unspecified.AddDays(-1));
        return DateTime.SpecifyKind(unspecified - offsetBefore, DateTimeKind.Utc);
    }

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Bratislava", "Central Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.Local;
    }
}
