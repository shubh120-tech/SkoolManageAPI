using System;
using System.Data;
using System.Threading.Tasks;
using SchoolManagement.Application.Attendance.Dtos;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Application.Attendance.Services;

public interface IAttendanceService
{
    Task<ApiResponse<object>> MarkAttendanceDayAsync(MarkAttendanceDayRequestDto request);
    Task<ApiResponse<GetAttendanceDayResponseDto>> GetAttendanceDayAsync(Guid classId, Guid sectionId, DateTime date);
    Task<ApiResponse<object>> MarkStaffAttendanceDayAsync(MarkStaffAttendanceDayRequestDto request);
    Task<ApiResponse<GetStaffAttendanceDayResponseDto>> GetStaffAttendanceDayAsync(DateTime date);

    /// <summary>Upsert one staff member&apos;s status for a calendar day (used when leave is approved / withdrawal completed).</summary>
    Task<ApiResponse<object>> MergeStaffDayStatusAsync(
        Guid staffId,
        DateTime attendanceDate,
        string attendanceStatus,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);

    /// <summary>Remove a Leave row for one staff member on a date (future days after leave withdrawal).</summary>
    Task<ApiResponse<object>> DeleteStaffLeaveDayAsync(
        Guid staffId,
        DateTime attendanceDate,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);
}

