using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Attendance.Dtos;
using SchoolManagement.Application.Attendance.Services;
using SchoolManagement.Application.Common;
using SchoolManagement.Infrastructure.Time;

namespace SchoolManagement.Infrastructure.Services;

public class AttendanceService : IAttendanceService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public AttendanceService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<object>> MarkAttendanceDayAsync(MarkAttendanceDayRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var dayDate = DateOnly.FromDateTime(request.AttendanceDate.Date);
        if (dayDate < IndiaTime.TodayDateOnly && !_currentUser.Permissions.Contains("CanEditPastAttendance"))
            return ApiResponse<object>.Fail("Editing attendance for a past date requires CanEditPastAttendance.", ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var role = _currentUser.Role ?? string.Empty;
        var isStaffOrTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase);
        if (isStaffOrTeacher)
        {
            var allowed = await IsClassSectionAllowedForCurrentStaffAsync(conn, request.ClassId, request.SectionId);
            if (!allowed)
                return ApiResponse<object>.Fail("Forbidden: class/section not assigned to current user.", ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_section_id", request.SectionId);
        var attendanceDateUtc = DateTime.SpecifyKind(request.AttendanceDate.Date, DateTimeKind.Utc);
        p.Add("p_attendance_date", attendanceDateUtc);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        var json = JsonSerializer.Serialize(NormalizeStudentRecordsForProc(request.Records));
        p.Add("p_records_json", json);

        await conn.ExecuteAsync("CALL sp_attendance_mark_class_day(@p_school_id,@p_class_id,@p_section_id,@p_attendance_date,@p_created_by,@p_records_json)", p);

        return ApiResponse<object>.Ok(null, "Attendance recorded.");
    }

    public async Task<ApiResponse<GetAttendanceDayResponseDto>> GetAttendanceDayAsync(Guid classId, Guid sectionId, DateTime date)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<GetAttendanceDayResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var role = _currentUser.Role ?? string.Empty;
        var isStaffOrTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase);
        if (isStaffOrTeacher)
        {
            var allowed = await IsClassSectionAllowedForCurrentStaffAsync(conn, classId, sectionId);
            if (!allowed)
                return ApiResponse<GetAttendanceDayResponseDto>.Ok(new GetAttendanceDayResponseDto { Records = new List<StudentAttendanceDto>() }, "No attendance for unassigned class/section.");
        }

        var dateOnly = date.Date;
        var p = new DynamicParameters();
        p.Add("SchoolId", _tenantContext.SchoolId);
        p.Add("ClassId", classId);
        p.Add("SectionId", sectionId);
        p.Add("Date", dateOnly, dbType: DbType.Date);
        const string sql = @"
            SELECT ar.student_id AS ""StudentId"", ar.is_present AS ""IsPresent"", ar.attendance_status AS ""Status""
            FROM attendance_records ar
            WHERE ar.attendance_day_id = (
                SELECT ad.id FROM attendance_days ad
                WHERE ad.school_id = @SchoolId AND ad.class_id = @ClassId AND ad.section_id = @SectionId
                  AND ad.attendance_date = @Date AND ad.is_deleted = FALSE
                LIMIT 1
            )
            AND ar.is_deleted = FALSE
            ORDER BY ar.student_id";
        var rows = await conn.QueryAsync<StudentAttendanceDto>(sql, p);
        var records = rows?.ToList() ?? new List<StudentAttendanceDto>();
        return ApiResponse<GetAttendanceDayResponseDto>.Ok(new GetAttendanceDayResponseDto { Records = records }, "Attendance fetched.");
    }

    public async Task<ApiResponse<object>> MarkStaffAttendanceDayAsync(MarkStaffAttendanceDayRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var dayDate = DateOnly.FromDateTime(request.AttendanceDate.Date);
        if (dayDate < IndiaTime.TodayDateOnly && !_currentUser.Permissions.Contains("CanEditPastAttendance"))
            return ApiResponse<object>.Fail("Editing staff attendance for a past date requires CanEditPastAttendance.", ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        var attendanceDateUtc = DateTime.SpecifyKind(request.AttendanceDate.Date, DateTimeKind.Utc);
        p.Add("p_attendance_date", attendanceDateUtc);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        var json = JsonSerializer.Serialize(NormalizeStaffRecordsForProc(request.Records));
        p.Add("p_records_json", json);

        await conn.ExecuteAsync("CALL sp_staff_attendance_mark_day(@p_school_id,@p_attendance_date,@p_created_by,@p_records_json)", p);

        return ApiResponse<object>.Ok(null, "Staff attendance recorded.");
    }

    public async Task<ApiResponse<GetStaffAttendanceDayResponseDto>> GetStaffAttendanceDayAsync(DateTime date)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<GetStaffAttendanceDayResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var dateOnly = date.Date;
        var p = new DynamicParameters();
        p.Add("SchoolId", _tenantContext.SchoolId);
        p.Add("Date", dateOnly, dbType: DbType.Date);
        const string sql = @"
            SELECT sar.staff_id AS ""StaffId"", sar.is_present AS ""IsPresent"", sar.attendance_status AS ""Status""
            FROM staff_attendance_records sar
            WHERE sar.attendance_day_id = (
                SELECT sad.id FROM staff_attendance_days sad
                WHERE sad.school_id = @SchoolId
                  AND sad.attendance_date = @Date
                  AND sad.is_deleted = FALSE
                LIMIT 1
            )
            AND sar.is_deleted = FALSE
            ORDER BY sar.staff_id";
        var rows = await conn.QueryAsync<StaffAttendanceDto>(sql, p);
        var records = rows?.ToList() ?? new List<StaffAttendanceDto>();
        return ApiResponse<GetStaffAttendanceDayResponseDto>.Ok(new GetStaffAttendanceDayResponseDto { Records = records }, "Staff attendance fetched.");
    }

    private async Task<bool> IsClassSectionAllowedForCurrentStaffAsync(IDbConnection conn, Guid classId, Guid sectionId)
    {
        if (_tenantContext.SchoolId is null || _currentUser.UserId is null)
            return false;

        const string userSql = @"
SELECT u.email
FROM users u
WHERE u.id = @UserId
  AND u.school_id = @SchoolId
  AND u.is_deleted = FALSE
LIMIT 1;";
        var email = await conn.ExecuteScalarAsync<string?>(userSql, new
        {
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId.Value
        });
        if (string.IsNullOrWhiteSpace(email)) return false;

        const string staffSql = @"
SELECT s.id
FROM staff s
WHERE s.school_id = @SchoolId
  AND LOWER(s.email) = LOWER(@Email)
  AND s.is_deleted = FALSE
LIMIT 1;";
        var staffId = await conn.ExecuteScalarAsync<Guid?>(staffSql, new
        {
            SchoolId = _tenantContext.SchoolId.Value,
            Email = email
        });
        if (staffId == null) return false;

        const string assignmentSql = @"
SELECT 1
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.teacher_id = @StaffId
  AND ct.class_id = @ClassId
  AND ct.section_id = @SectionId
  AND ct.is_deleted = FALSE
LIMIT 1;";
        var matched = await conn.ExecuteScalarAsync<int?>(assignmentSql, new
        {
            SchoolId = _tenantContext.SchoolId.Value,
            StaffId = staffId.Value,
            ClassId = classId,
            SectionId = sectionId
        });
        return matched.HasValue;
    }

    public async Task<ApiResponse<object>> MergeStaffDayStatusAsync(
        Guid staffId,
        DateTime attendanceDate,
        string attendanceStatus,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        var st = attendanceStatus?.Trim();
        if (string.IsNullOrEmpty(st) ||
            !string.Equals(st, "Present", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(st, "Absent", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(st, "Leave", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail("attendanceStatus must be Present, Absent, or Leave.", ErrorCodes.BusinessRule);

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        var attendanceDateUtc = DateTime.SpecifyKind(attendanceDate.Date, DateTimeKind.Utc);
        p.Add("p_attendance_date", attendanceDateUtc);
        p.Add("p_staff_id", staffId);
        p.Add("p_attendance_status", st);
        p.Add("p_created_by", _currentUser.UserId.Value);
        const string sql =
            "CALL sp_staff_attendance_merge_staff_status(@p_school_id,@p_attendance_date,@p_staff_id,@p_attendance_status,@p_created_by)";

        if (connection is not null)
        {
            await connection.ExecuteAsync(sql, p, transaction);
            return ApiResponse<object>.Ok(null, "Staff attendance updated.");
        }

        using var conn = await _connectionFactory.CreateConnectionAsync();
        await conn.ExecuteAsync(sql, p);
        return ApiResponse<object>.Ok(null, "Staff attendance updated.");
    }

    public async Task<ApiResponse<object>> DeleteStaffLeaveDayAsync(
        Guid staffId,
        DateTime attendanceDate,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        var attendanceDateUtc = DateTime.SpecifyKind(attendanceDate.Date, DateTimeKind.Utc);
        p.Add("p_attendance_date", attendanceDateUtc);
        p.Add("p_staff_id", staffId);
        const string sql = "CALL sp_staff_attendance_delete_staff_day_if_leave(@p_school_id,@p_attendance_date,@p_staff_id)";

        if (connection is not null)
        {
            await connection.ExecuteAsync(sql, p, transaction);
            return ApiResponse<object>.Ok(null, "Staff leave row removed when present.");
        }

        using var conn = await _connectionFactory.CreateConnectionAsync();
        await conn.ExecuteAsync(sql, p);
        return ApiResponse<object>.Ok(null, "Staff leave row removed when present.");
    }

    private static List<object> NormalizeStudentRecordsForProc(List<StudentAttendanceDto>? records)
    {
        var list = records ?? new List<StudentAttendanceDto>();
        return list.Select(r =>
        {
            var status = r.AttendanceStatus ?? r.Status;
            if (!string.IsNullOrWhiteSpace(status))
                return (object)new { r.StudentId, AttendanceStatus = status.Trim() };
            return new { r.StudentId, r.IsPresent };
        }).ToList();
    }

    private static List<object> NormalizeStaffRecordsForProc(List<StaffAttendanceDto>? records)
    {
        var list = records ?? new List<StaffAttendanceDto>();
        return list.Select(r =>
        {
            var status = r.AttendanceStatus ?? r.Status;
            if (!string.IsNullOrWhiteSpace(status))
                return (object)new { r.StaffId, AttendanceStatus = status.Trim() };
            return new { r.StaffId, r.IsPresent };
        }).ToList();
    }
}

