using System;

namespace SchoolManagement.Application.Common;

public static class TimeZones
{
    // Windows name: "India Standard Time"
    // Linux name: "Asia/Kolkata"
    private static readonly TimeZoneInfo IstZone =
        TimeZoneInfo.FromSerializedString(
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata"
            ).ToSerializedString()
        );

    /// <summary>Current time in IST.</summary>
    public static DateTime NowIst() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IstZone);

    /// <summary>Convert any DateTime (local/UTC) to IST.</summary>
    public static DateTime ToIst(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return TimeZoneInfo.ConvertTimeFromUtc(value, IstZone);

        return TimeZoneInfo.ConvertTime(value, IstZone);
    }
}

