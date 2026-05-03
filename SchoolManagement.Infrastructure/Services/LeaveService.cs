using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Attendance.Services;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Leaves.Dtos;
using SchoolManagement.Application.Leaves.Services;
using SchoolManagement.Infrastructure.Time;

namespace SchoolManagement.Infrastructure.Services;

public class LeaveService : ILeaveService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationDeliveryService _notificationDelivery;
    private readonly IAttendanceService _attendanceService;

    public LeaveService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        INotificationDeliveryService notificationDelivery,
        IAttendanceService attendanceService)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notificationDelivery = notificationDelivery;
        _attendanceService = attendanceService;
    }

    public async Task<ApiResponse<object>> CreateLeaveAsync(LeaveRequestCreateDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        if (request.IsHalfDay)
            return ApiResponse<object>.Fail("Half-day leave is not supported. Use full-day dates only.", ErrorCodes.BusinessRule);

        if (request.ToDate.Date < request.FromDate.Date)
            return ApiResponse<object>.Fail("To date cannot be before from date.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
INSERT INTO leave_requests(
    id, school_id, is_deleted, created_at, created_by,
    from_date, to_date, reason, leave_type, is_half_day, half_day_session, status)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @UserId,
    @FromDate, @ToDate, @Reason, @LeaveType, FALSE, NULL, 'Pending')";

        await conn.ExecuteAsync(sql, new
        {
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId.Value,
            FromDate = request.FromDate.Date,
            ToDate = request.ToDate.Date,
            Reason = request.Reason,
            LeaveType = request.LeaveType
        });

        await _notificationDelivery.NotifyLeaveCreatedAsync(
            _tenantContext.SchoolId.Value,
            _currentUser.UserId.Value,
            request.FromDate.Date,
            request.ToDate.Date,
            request.Reason,
            request.LeaveType,
            false,
            null);

        return ApiResponse<object>.Ok(null, "Leave request submitted.");
    }

    public async Task<ApiResponse<LeaveRequestListResponseDto>> GetMyLeavesAsync(int page, int pageSize, string? status, int? year, int? month)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<LeaveRequestListResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (_currentUser.UserId is null)
            return ApiResponse<LeaveRequestListResponseDto>.Fail("User context missing.", ErrorCodes.BusinessRule);

        var p = Math.Max(1, page);
        var ps = Math.Max(1, pageSize);
        var offset = (p - 1) * ps;

        using var conn = await _connectionFactory.CreateConnectionAsync();

        const string baseWhere = @"
WHERE lr.school_id = @SchoolId
  AND lr.is_deleted = FALSE
  AND lr.created_by = @UserId
  AND (@Status IS NULL OR lr.status = @Status)
  AND (@Year IS NULL OR EXTRACT(YEAR FROM lr.from_date) = @Year)
  AND (@Month IS NULL OR EXTRACT(MONTH FROM lr.from_date) = @Month)";

        var totalsSql = $@"
SELECT COUNT(*)::bigint AS TotalCount
FROM leave_requests lr
{baseWhere}";

        var totalCount = await conn.ExecuteScalarAsync<long>(totalsSql, new
        {
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId.Value,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Year = year,
            Month = month
        });

        var listSql = $@"
SELECT lr.id AS Id,
       lr.from_date AS FromDate,
       lr.to_date AS ToDate,
       lr.reason AS Reason,
       lr.leave_type AS LeaveType,
       lr.is_half_day AS IsHalfDay,
       lr.half_day_session AS HalfDaySession,
       lr.status AS Status,
       lr.created_at AS CreatedAt,
       lr.decision_date AS DecisionDate,
       lr.admin_remarks AS AdminRemarks,
       lr.withdrawal_status AS WithdrawalStatus,
       lr.withdrawal_requested_at AS WithdrawalRequestedAt
FROM leave_requests lr
{baseWhere}
ORDER BY lr.created_at DESC
LIMIT @PageSize OFFSET @Offset";

        var items = (await conn.QueryAsync<LeaveRequestListItemDto>(listSql, new
        {
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId.Value,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Year = year,
            Month = month,
            PageSize = ps,
            Offset = offset
        })).AsList();

        var response = new LeaveRequestListResponseDto
        {
            Items = items,
            TotalCount = totalCount
        };
        return ApiResponse<LeaveRequestListResponseDto>.Ok(response);
    }

    public async Task<ApiResponse<LeaveRequestListResponseDto>> GetAllLeavesAsync(int page, int pageSize, string? status, int? year, int? month)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<LeaveRequestListResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var p = Math.Max(1, page);
        var ps = Math.Max(1, pageSize);
        var offset = (p - 1) * ps;

        using var conn = await _connectionFactory.CreateConnectionAsync();

        const string baseWhere = @"
WHERE lr.school_id = @SchoolId
  AND lr.is_deleted = FALSE
  AND (@Status IS NULL OR lr.status = @Status)
  AND (@Year IS NULL OR EXTRACT(YEAR FROM lr.from_date) = @Year)
  AND (@Month IS NULL OR EXTRACT(MONTH FROM lr.from_date) = @Month)";

        var totalsSql = $@"
SELECT COUNT(*)::bigint AS TotalCount
FROM leave_requests lr
{baseWhere}";

        var totalCount = await conn.ExecuteScalarAsync<long>(totalsSql, new
        {
            SchoolId = _tenantContext.SchoolId,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Year = year,
            Month = month
        });

        var listSql = $@"
SELECT lr.id AS Id,
       lr.from_date AS FromDate,
       lr.to_date AS ToDate,
       lr.reason AS Reason,
       lr.leave_type AS LeaveType,
       lr.is_half_day AS IsHalfDay,
       lr.half_day_session AS HalfDaySession,
       lr.status AS Status,
       lr.created_at AS CreatedAt,
       lr.decision_date AS DecisionDate,
       lr.admin_remarks AS AdminRemarks,
       lr.withdrawal_status AS WithdrawalStatus,
       lr.withdrawal_requested_at AS WithdrawalRequestedAt,
       COALESCE(u.full_name, '') AS ApplicantName,
       COALESCE(r.name, '') AS ApplicantRole
FROM leave_requests lr
LEFT JOIN users u ON u.id = lr.created_by
LEFT JOIN roles r ON r.id = u.role_id
{baseWhere}
ORDER BY lr.created_at DESC
LIMIT @PageSize OFFSET @Offset";

        var items = (await conn.QueryAsync<LeaveRequestListItemDto>(listSql, new
        {
            SchoolId = _tenantContext.SchoolId,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Year = year,
            Month = month,
            PageSize = ps,
            Offset = offset
        })).AsList();

        var response = new LeaveRequestListResponseDto
        {
            Items = items,
            TotalCount = totalCount
        };
        return ApiResponse<LeaveRequestListResponseDto>.Ok(response);
    }

    public async Task<ApiResponse<object>> ApproveLeaveAsync(Guid id, string? remarks)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();

        const string selectSql = @"
SELECT lr.from_date AS FromDate, lr.to_date AS ToDate, lr.created_by AS CreatedBy
FROM leave_requests lr
WHERE lr.id = @Id AND lr.school_id = @SchoolId AND lr.is_deleted = FALSE AND lr.status = 'Pending'
FOR UPDATE";

        var row = await conn.QuerySingleOrDefaultAsync<LeaveApproveRow>(selectSql, new { Id = id, SchoolId = _tenantContext.SchoolId }, tx);
        if (row is null)
        {
            tx.Rollback();
            return ApiResponse<object>.Fail("Leave not found or already decided.", ErrorCodes.BusinessRule);
        }

        const string updateSql = @"
UPDATE leave_requests
SET status = 'Approved',
    approver_id = @ApproverId,
    decision_date = NOW(),
    admin_remarks = @Remarks,
    updated_at = NOW(),
    updated_by = @ApproverId
WHERE id = @Id AND school_id = @SchoolId";

        await conn.ExecuteAsync(updateSql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            ApproverId = _currentUser.UserId.Value,
            Remarks = remarks
        }, tx);

        var staffId = await GetStaffIdForUserAsync(conn, row.CreatedBy, _tenantContext.SchoolId.Value, tx);
        if (staffId is null)
        {
            tx.Rollback();
            return ApiResponse<object>.Fail(
                "Cannot sync attendance: applicant user is not linked to a staff record (match users.email to staff.email).",
                ErrorCodes.BusinessRule);
        }

        foreach (var day in EachCalendarDate(row.FromDate, row.ToDate))
        {
            var merge = await _attendanceService.MergeStaffDayStatusAsync(
                staffId.Value,
                day,
                "Leave",
                conn,
                tx);
            if (!merge.Success)
            {
                tx.Rollback();
                return merge;
            }
        }

        tx.Commit();

        await _notificationDelivery.NotifyLeaveDecisionAsync(
            _tenantContext.SchoolId.Value,
            id,
            "Approved",
            remarks,
            _currentUser.UserId.Value);

        return ApiResponse<object>.Ok(null, "Leave approved.");
    }

    public async Task<ApiResponse<object>> RejectLeaveAsync(Guid id, string? remarks)
    {
        return await SetStatusAsync(id, "Rejected", remarks);
    }

    public async Task<ApiResponse<object>> RequestWithdrawLeaveAsync(Guid id)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE leave_requests
SET withdrawal_status = 'Pending',
    withdrawal_requested_at = NOW(),
    withdrawal_decided_at = NULL,
    withdrawal_decider_id = NULL,
    withdrawal_remarks = NULL,
    updated_at = NOW(),
    updated_by = @UserId
WHERE id = @Id
  AND school_id = @SchoolId
  AND is_deleted = FALSE
  AND status = 'Approved'
  AND created_by = @UserId
  AND (withdrawal_status IS NULL OR withdrawal_status = 'Rejected')
  AND from_date >= @TodayIndia";

        var n = await conn.ExecuteAsync(sql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId.Value,
            TodayIndia = IndiaTime.TodayDateOnly,
        });
        if (n == 0)
        {
            return ApiResponse<object>.Fail(
                "Cannot request withdraw: leave may have already started (India calendar), is not approved, withdrawal is already pending, or this is not your request.",
                ErrorCodes.BusinessRule);
        }

        await _notificationDelivery.NotifyLeaveWithdrawRequestedAsync(
            _tenantContext.SchoolId.Value,
            id,
            _currentUser.UserId.Value);

        return ApiResponse<object>.Ok(null, "Withdraw leave request sent to admin.");
    }

    public async Task<ApiResponse<object>> RejectWithdrawLeaveAsync(Guid id, string? remarks)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE leave_requests
SET withdrawal_status = 'Rejected',
    withdrawal_decided_at = NOW(),
    withdrawal_decider_id = @DeciderId,
    withdrawal_remarks = @Remarks,
    updated_at = NOW(),
    updated_by = @DeciderId
WHERE id = @Id
  AND school_id = @SchoolId
  AND is_deleted = FALSE
  AND withdrawal_status = 'Pending'";

        var n = await conn.ExecuteAsync(sql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            DeciderId = _currentUser.UserId.Value,
            Remarks = remarks
        });
        if (n == 0)
            return ApiResponse<object>.Fail("Leave not found or no pending withdrawal.", ErrorCodes.BusinessRule);

        await _notificationDelivery.NotifyLeaveWithdrawalDecisionAsync(
            _tenantContext.SchoolId.Value,
            id,
            withdrawalApproved: false,
            remarks,
            _currentUser.UserId.Value);

        return ApiResponse<object>.Ok(null, "Withdraw leave rejected; leave remains approved.");
    }

    public async Task<ApiResponse<object>> ApproveWithdrawLeaveAsync(Guid id, ApproveWithdrawLeaveRequestDto? request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        request ??= new ApproveWithdrawLeaveRequestDto();
        var map = request.StaffAttendanceByDate ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();

        const string selectSql = @"
SELECT lr.from_date AS FromDate, lr.to_date AS ToDate, lr.created_by AS CreatedBy
FROM leave_requests lr
WHERE lr.id = @Id AND lr.school_id = @SchoolId AND lr.is_deleted = FALSE
  AND lr.status = 'Approved' AND lr.withdrawal_status = 'Pending'
FOR UPDATE";

        var row = await conn.QuerySingleOrDefaultAsync<LeaveApproveRow>(selectSql, new { Id = id, SchoolId = _tenantContext.SchoolId }, tx);
        if (row is null)
        {
            tx.Rollback();
            return ApiResponse<object>.Fail("Leave not found or no pending withdrawal.", ErrorCodes.BusinessRule);
        }

        var staffId = await GetStaffIdForUserAsync(conn, row.CreatedBy, _tenantContext.SchoolId.Value, tx);
        if (staffId is null)
        {
            tx.Rollback();
            return ApiResponse<object>.Fail("Applicant is not linked to a staff record.", ErrorCodes.BusinessRule);
        }

        var today = IndiaTime.TodayDateOnly;
        foreach (var dayDate in EachCalendarDateOnly(row.FromDate, row.ToDate))
        {
            if (dayDate > today)
                continue;

            var key = dayDate.ToString("yyyy-MM-dd");
            if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                tx.Rollback();
                return ApiResponse<object>.Fail(
                    $"StaffAttendanceByDate must include Present or Absent for {key} (today or past dates in IST).",
                    ErrorCodes.BusinessRule);
            }

            var v = raw.Trim();
            if (!string.Equals(v, "Present", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(v, "Absent", StringComparison.OrdinalIgnoreCase))
            {
                tx.Rollback();
                return ApiResponse<object>.Fail($"Invalid status for {key}: use Present or Absent.", ErrorCodes.BusinessRule);
            }

            var status = string.Equals(v, "Present", StringComparison.OrdinalIgnoreCase) ? "Present" : "Absent";
            var merge = await _attendanceService.MergeStaffDayStatusAsync(
                staffId.Value,
                dayDate.ToDateTime(TimeOnly.MinValue),
                status,
                conn,
                tx);
            if (!merge.Success)
            {
                tx.Rollback();
                return merge;
            }
        }

        foreach (var dayDate in EachCalendarDateOnly(row.FromDate, row.ToDate))
        {
            if (dayDate <= today)
                continue;

            var del = await _attendanceService.DeleteStaffLeaveDayAsync(
                staffId.Value,
                dayDate.ToDateTime(TimeOnly.MinValue),
                conn,
                tx);
            if (!del.Success)
            {
                tx.Rollback();
                return del;
            }
        }

        const string updateSql = @"
UPDATE leave_requests
SET status = 'Withdrawn',
    withdrawal_status = 'Approved',
    withdrawal_decided_at = NOW(),
    withdrawal_decider_id = @DeciderId,
    withdrawal_remarks = @Remarks,
    updated_at = NOW(),
    updated_by = @DeciderId
WHERE id = @Id AND school_id = @SchoolId";

        await conn.ExecuteAsync(updateSql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            DeciderId = _currentUser.UserId.Value,
            Remarks = request.Remarks
        }, tx);

        tx.Commit();

        await _notificationDelivery.NotifyLeaveWithdrawalDecisionAsync(
            _tenantContext.SchoolId.Value,
            id,
            withdrawalApproved: true,
            request.Remarks,
            _currentUser.UserId.Value);

        return ApiResponse<object>.Ok(null, "Leave withdrawn; staff attendance updated.");
    }

    private async Task<ApiResponse<object>> SetStatusAsync(Guid id, string newStatus, string? remarks)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
UPDATE leave_requests
SET status = @Status,
    approver_id = @ApproverId,
    decision_date = NOW(),
    admin_remarks = @Remarks,
    updated_at = NOW(),
    updated_by = @ApproverId
WHERE id = @Id
  AND school_id = @SchoolId
  AND is_deleted = FALSE
  AND status = 'Pending'";

        var affected = await conn.ExecuteAsync(sql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            Status = newStatus,
            ApproverId = _currentUser.UserId.Value,
            Remarks = remarks
        });

        if (affected == 0)
            return ApiResponse<object>.Fail("Leave not found or already decided.", ErrorCodes.BusinessRule);

        await _notificationDelivery.NotifyLeaveDecisionAsync(
            _tenantContext.SchoolId.Value,
            id,
            newStatus,
            remarks,
            _currentUser.UserId.Value);

        return ApiResponse<object>.Ok(null, $"Leave {newStatus.ToLowerInvariant()}.");
    }

    private static async Task<Guid?> GetStaffIdForUserAsync(
        IDbConnection conn,
        Guid userId,
        Guid schoolId,
        IDbTransaction? tx)
    {
        const string sql = @"
SELECT s.id
FROM staff s
INNER JOIN users u ON u.id = @UserId
WHERE s.school_id = @SchoolId
  AND s.is_deleted = FALSE
  AND u.school_id = @SchoolId
  AND LOWER(s.email) = LOWER(u.email)
LIMIT 1";
        return await conn.ExecuteScalarAsync<Guid?>(sql, new { UserId = userId, SchoolId = schoolId }, tx);
    }

    private static IEnumerable<DateTime> EachCalendarDate(DateTime from, DateTime to)
    {
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            yield return d;
    }

    private static IEnumerable<DateOnly> EachCalendarDateOnly(DateTime from, DateTime to)
    {
        var a = DateOnly.FromDateTime(from.Date);
        var b = DateOnly.FromDateTime(to.Date);
        for (var d = a; d <= b; d = d.AddDays(1))
            yield return d;
    }

    private sealed class LeaveApproveRow
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
