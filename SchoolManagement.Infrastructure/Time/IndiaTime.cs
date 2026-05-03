using System;

namespace SchoolManagement.Infrastructure.Time;

/// <summary>School calendar dates in India (IST, Asia/Kolkata).</summary>
public static class IndiaTime
{
    public static TimeZoneInfo GetTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Kolkata", out var tz))
            return tz;
        return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    }

    public static DateOnly TodayDateOnly =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetTimeZone()).Date);
}
