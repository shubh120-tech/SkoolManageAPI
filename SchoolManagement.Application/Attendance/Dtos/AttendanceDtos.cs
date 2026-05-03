using System;

namespace SchoolManagement.Application.Attendance.Dtos;

public class StudentAttendanceDto
{
    public Guid StudentId { get; set; }
    public bool IsPresent { get; set; }
    /// <summary>Optional: Present | Absent | Leave. When set, overrides <see cref="IsPresent"/> for marking.</summary>
    public string? Status { get; set; }
    /// <summary>Alias for JSON clients using AttendanceStatus.</summary>
    public string? AttendanceStatus { get; set; }
}

public class MarkAttendanceDayRequestDto
{
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public DateTime AttendanceDate { get; set; }
    public List<StudentAttendanceDto> Records { get; set; } = new();
}

/// <summary>Response for GET attendance/day (existing records for a class/section/date).</summary>
public class GetAttendanceDayResponseDto
{
    public List<StudentAttendanceDto> Records { get; set; } = new();
}

public class StaffAttendanceDto
{
    public Guid StaffId { get; set; }
    public bool IsPresent { get; set; }
    /// <summary>Optional: Present | Absent | Leave. When set, overrides <see cref="IsPresent"/> for marking.</summary>
    public string? Status { get; set; }
    public string? AttendanceStatus { get; set; }
}

public class MarkStaffAttendanceDayRequestDto
{
    public DateTime AttendanceDate { get; set; }
    public List<StaffAttendanceDto> Records { get; set; } = new();
}

public class GetStaffAttendanceDayResponseDto
{
    public List<StaffAttendanceDto> Records { get; set; } = new();
}

