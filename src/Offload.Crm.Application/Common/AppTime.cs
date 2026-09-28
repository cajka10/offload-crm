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
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    /// <summary>Converts a local (Europe/Bratislava) wall-clock time to UTC.</summary>
    public static DateTime LocalToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

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
