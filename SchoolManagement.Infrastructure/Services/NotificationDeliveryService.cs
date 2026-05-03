using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Notifications.Services;
using SchoolManagement.Application.Schools;
using SchoolManagement.Application.Schools.Services;

namespace SchoolManagement.Infrastructure.Services;

public class NotificationDeliveryService : INotificationDeliveryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEmailSender _emailSender;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly ISchoolFeatureService _schoolFeatureService;
    private readonly INotificationService _inAppNotifications;
    private readonly ILogger<NotificationDeliveryService> _logger;

    public NotificationDeliveryService(
        IDbConnectionFactory connectionFactory,
        IEmailSender emailSender,
        IWhatsAppSender whatsAppSender,
        ISchoolFeatureService schoolFeatureService,
        INotificationService inAppNotifications,
        ILogger<NotificationDeliveryService> logger)
    {
        _connectionFactory = connectionFactory;
        _emailSender = emailSender;
        _whatsAppSender = whatsAppSender;
        _schoolFeatureService = schoolFeatureService;
        _inAppNotifications = inAppNotifications;
        _logger = logger;
    }

    public async Task NotifyLeaveCreatedAsync(
        Guid schoolId,
        Guid requesterUserId,
        DateTime fromDate,
        DateTime toDate,
        string reason,
        string leaveType,
        bool isHalfDay,
        string? halfDaySession)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();

        var requester = await conn.QuerySingleOrDefaultAsync<UserContactRow>(
            @"SELECT u.id AS UserId, u.full_name AS FullName, u.email AS Email, s.mobile_no AS MobileNo
              FROM users u
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE u.id = @UserId AND u.school_id = @SchoolId AND u.is_deleted = FALSE
              LIMIT 1",
            new { UserId = requesterUserId, SchoolId = schoolId });

        if (requester is null) return;

        var approvers = (await conn.QueryAsync<UserContactRow>(
            @"SELECT DISTINCT u.id AS UserId, u.full_name AS FullName, u.email AS Email, s.mobile_no AS MobileNo
              FROM users u
              JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE u.school_id = @SchoolId
                AND u.is_deleted = FALSE
                AND (
                    r.name IN ('SchoolAdmin', 'Principal')
                    OR EXISTS (
                        SELECT 1 FROM user_permissions up
                        JOIN permissions p ON p.id = up.permission_id AND p.is_deleted = FALSE
                        WHERE up.user_id = u.id AND up.is_deleted = FALSE
                          AND p.name IN ('Leave.Approve', 'Leave.Approve.All')
                    )
                    OR EXISTS (
                        SELECT 1 FROM role_permissions rp
                        JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
                        WHERE rp.role_id = u.role_id AND rp.is_deleted = FALSE
                          AND p.name IN ('Leave.Approve', 'Leave.Approve.All')
                    )
                )",
            new { SchoolId = schoolId })).ToList();

        if (approvers.Count == 0) return;

        var dateText = isHalfDay
            ? $"{fromDate:dd MMM yyyy} ({halfDaySession ?? "HalfDay"})"
            : $"{fromDate:dd MMM yyyy} to {toDate:dd MMM yyyy}";
        var message = $"{requester.FullName} submitted a leave request ({leaveType}) for {dateText}. Reason: {reason}";
        var subject = $"Leave Request: {requester.FullName}";
        var html = $@"<p><strong>{Escape(requester.FullName)}</strong> submitted a leave request.</p>
<p><strong>Type:</strong> {Escape(leaveType)}<br/>
<strong>Dates:</strong> {Escape(dateText)}<br/>
<strong>Reason:</strong> {Escape(reason)}</p>";

        foreach (var approver in approvers)
        {
            if (approver.UserId == requesterUserId)
                continue;

            await _inAppNotifications.CreateAsync(
                approver.UserId,
                schoolId,
                subject,
                message,
                requesterUserId,
                "/leave-requests");

            await TryBestEffortNotifyAsync(approver, subject, html, message, schoolId);
        }
    }

    public async Task NotifyLeaveDecisionAsync(
        Guid schoolId,
        Guid leaveId,
        string decisionStatus,
        string? remarks,
        Guid decidedByUserId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var leave = await conn.QuerySingleOrDefaultAsync<LeaveDecisionRow>(
            @"SELECT lr.id AS LeaveId,
                     lr.from_date AS FromDate,
                     lr.to_date AS ToDate,
                     lr.leave_type AS LeaveType,
                     lr.is_half_day AS IsHalfDay,
                     lr.half_day_session AS HalfDaySession,
                     lr.reason AS Reason,
                     u.id AS RequesterUserId,
                     u.full_name AS RequesterFullName,
                     u.email AS RequesterEmail,
                     s.mobile_no AS RequesterMobileNo
              FROM leave_requests lr
              JOIN users u ON u.id = lr.created_by AND u.is_deleted = FALSE
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE lr.id = @LeaveId AND lr.school_id = @SchoolId AND lr.is_deleted = FALSE
              LIMIT 1",
            new { LeaveId = leaveId, SchoolId = schoolId });

        if (leave is null) return;

        var dateText = leave.IsHalfDay
            ? $"{leave.FromDate:dd MMM yyyy} ({leave.HalfDaySession ?? "HalfDay"})"
            : $"{leave.FromDate:dd MMM yyyy} to {leave.ToDate:dd MMM yyyy}";
        var status = decisionStatus?.Trim() ?? "Updated";
        var note = string.IsNullOrWhiteSpace(remarks) ? string.Empty : $" Remarks: {remarks}";
        var text = $"Your leave request ({leave.LeaveType}) for {dateText} is {status}.{note}";
        var subject = $"Leave {status}";
        var html = $@"<p>Your leave request is <strong>{Escape(status)}</strong>.</p>
<p><strong>Type:</strong> {Escape(leave.LeaveType)}<br/>
<strong>Dates:</strong> {Escape(dateText)}<br/>
<strong>Reason:</strong> {Escape(leave.Reason)}</p>
{(string.IsNullOrWhiteSpace(remarks) ? string.Empty : $"<p><strong>Remarks:</strong> {Escape(remarks!)}</p>")}";

        await _inAppNotifications.CreateAsync(
            leave.RequesterUserId,
            schoolId,
            subject,
            text,
            decidedByUserId,
            "/leaves");

        await TryBestEffortNotifyAsync(
            new UserContactRow
            {
                UserId = leave.RequesterUserId,
                FullName = leave.RequesterFullName,
                Email = leave.RequesterEmail,
                MobileNo = leave.RequesterMobileNo
            },
            subject,
            html,
            text,
            schoolId);
    }

    public async Task NotifyLeaveWithdrawRequestedAsync(Guid schoolId, Guid leaveId, Guid requesterUserId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();

        var leave = await conn.QuerySingleOrDefaultAsync<LeaveWithdrawRow>(
            @"SELECT lr.from_date AS FromDate,
                     lr.to_date AS ToDate,
                     lr.leave_type AS LeaveType,
                     lr.is_half_day AS IsHalfDay,
                     lr.half_day_session AS HalfDaySession,
                     lr.reason AS Reason,
                     u.id AS RequesterUserId,
                     u.full_name AS RequesterFullName,
                     u.email AS RequesterEmail,
                     s.mobile_no AS RequesterMobileNo
              FROM leave_requests lr
              JOIN users u ON u.id = lr.created_by AND u.is_deleted = FALSE
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE lr.id = @LeaveId AND lr.school_id = @SchoolId AND lr.is_deleted = FALSE
              LIMIT 1",
            new { LeaveId = leaveId, SchoolId = schoolId });

        if (leave is null) return;

        var dateText = leave.IsHalfDay
            ? $"{leave.FromDate:dd MMM yyyy} ({leave.HalfDaySession ?? "HalfDay"})"
            : $"{leave.FromDate:dd MMM yyyy} to {leave.ToDate:dd MMM yyyy}";
        var subject = $"Withdraw leave requested: {leave.RequesterFullName}";
        var message =
            $"{leave.RequesterFullName} asked to withdraw an approved leave ({leave.LeaveType}) for {dateText}. Open Leave requests to approve or reject.";
        var html = $@"<p><strong>{Escape(leave.RequesterFullName)}</strong> requested to withdraw an <strong>approved</strong> leave.</p>
<p><strong>Type:</strong> {Escape(leave.LeaveType)}<br/>
<strong>Dates:</strong> {Escape(dateText)}<br/>
<strong>Original reason:</strong> {Escape(leave.Reason)}</p>
<p>Review this in <strong>Leave requests</strong> in the school admin portal.</p>";

        var approvers = (await conn.QueryAsync<UserContactRow>(
            @"SELECT DISTINCT u.id AS UserId, u.full_name AS FullName, u.email AS Email, s.mobile_no AS MobileNo
              FROM users u
              JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE u.school_id = @SchoolId
                AND u.is_deleted = FALSE
                AND (
                    r.name IN ('SchoolAdmin', 'Principal')
                    OR EXISTS (
                        SELECT 1 FROM user_permissions up
                        JOIN permissions p ON p.id = up.permission_id AND p.is_deleted = FALSE
                        WHERE up.user_id = u.id AND up.is_deleted = FALSE
                          AND p.name IN ('Leave.Approve', 'Leave.Approve.All')
                    )
                    OR EXISTS (
                        SELECT 1 FROM role_permissions rp
                        JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
                        WHERE rp.role_id = u.role_id AND rp.is_deleted = FALSE
                          AND p.name IN ('Leave.Approve', 'Leave.Approve.All')
                    )
                )",
            new { SchoolId = schoolId })).ToList();

        foreach (var approver in approvers)
        {
            if (approver.UserId == requesterUserId)
                continue;

            await _inAppNotifications.CreateAsync(
                approver.UserId,
                schoolId,
                subject,
                message,
                requesterUserId,
                "/leave-requests");

            await TryBestEffortNotifyAsync(approver, subject, html, message, schoolId);
        }
    }

    public async Task NotifyLeaveWithdrawalDecisionAsync(
        Guid schoolId,
        Guid leaveId,
        bool withdrawalApproved,
        string? remarks,
        Guid decidedByUserId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var leave = await conn.QuerySingleOrDefaultAsync<LeaveDecisionRow>(
            @"SELECT lr.id AS LeaveId,
                     lr.from_date AS FromDate,
                     lr.to_date AS ToDate,
                     lr.leave_type AS LeaveType,
                     lr.is_half_day AS IsHalfDay,
                     lr.half_day_session AS HalfDaySession,
                     lr.reason AS Reason,
                     u.id AS RequesterUserId,
                     u.full_name AS RequesterFullName,
                     u.email AS RequesterEmail,
                     s.mobile_no AS RequesterMobileNo
              FROM leave_requests lr
              JOIN users u ON u.id = lr.created_by AND u.is_deleted = FALSE
              LEFT JOIN staff s ON s.school_id = u.school_id AND LOWER(s.email) = LOWER(u.email) AND s.is_deleted = FALSE
              WHERE lr.id = @LeaveId AND lr.school_id = @SchoolId AND lr.is_deleted = FALSE
              LIMIT 1",
            new { LeaveId = leaveId, SchoolId = schoolId });

        if (leave is null) return;

        var dateText = leave.IsHalfDay
            ? $"{leave.FromDate:dd MMM yyyy} ({leave.HalfDaySession ?? "HalfDay"})"
            : $"{leave.FromDate:dd MMM yyyy} to {leave.ToDate:dd MMM yyyy}";
        var outcome = withdrawalApproved ? "approved" : "rejected";
        var note = string.IsNullOrWhiteSpace(remarks) ? string.Empty : $" Remarks: {remarks}";
        var text =
            $"Your request to withdraw approved leave ({leave.LeaveType}, {dateText}) was {outcome}.{note}";
        var subject = withdrawalApproved ? "Leave withdraw approved" : "Leave withdraw rejected";
        var html = $@"<p>Your request to withdraw an approved leave was <strong>{Escape(outcome)}</strong>.</p>
<p><strong>Type:</strong> {Escape(leave.LeaveType)}<br/>
<strong>Dates:</strong> {Escape(dateText)}</p>
{(string.IsNullOrWhiteSpace(remarks) ? string.Empty : $"<p><strong>Admin remarks:</strong> {Escape(remarks!)}</p>")}";

        await _inAppNotifications.CreateAsync(
            leave.RequesterUserId,
            schoolId,
            subject,
            text,
            decidedByUserId,
            "/leaves");

        await TryBestEffortNotifyAsync(
            new UserContactRow
            {
                UserId = leave.RequesterUserId,
                FullName = leave.RequesterFullName,
                Email = leave.RequesterEmail,
                MobileNo = leave.RequesterMobileNo
            },
            subject,
            html,
            text,
            schoolId);
    }

    private async Task TryBestEffortNotifyAsync(
        UserContactRow recipient,
        string subject,
        string htmlBody,
        string whatsappMessage,
        Guid schoolId)
    {
        if (!string.IsNullOrWhiteSpace(recipient.Email))
        {
            try
            {
                await _emailSender.SendAsync(recipient.Email!, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email send failed (best effort). User={UserId}, Email={Email}", recipient.UserId, recipient.Email);
            }
        }

        if (!string.IsNullOrWhiteSpace(recipient.MobileNo)
            && await _schoolFeatureService.IsFeatureEnabledAsync(schoolId, SchoolFeatureKeys.WhatsAppPremium))
        {
            try
            {
                await _whatsAppSender.SendTextAsync(recipient.MobileNo!, whatsappMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp send failed (best effort). User={UserId}, Mobile={Mobile}", recipient.UserId, recipient.MobileNo);
            }
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private sealed class UserContactRow
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? MobileNo { get; set; }
    }

    private sealed class LeaveDecisionRow
    {
        public Guid LeaveId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string LeaveType { get; set; } = string.Empty;
        public bool IsHalfDay { get; set; }
        public string? HalfDaySession { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid RequesterUserId { get; set; }
        public string RequesterFullName { get; set; } = string.Empty;
        public string? RequesterEmail { get; set; }
        public string? RequesterMobileNo { get; set; }
    }

    private sealed class LeaveWithdrawRow
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string LeaveType { get; set; } = string.Empty;
        public bool IsHalfDay { get; set; }
        public string? HalfDaySession { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid RequesterUserId { get; set; }
        public string RequesterFullName { get; set; } = string.Empty;
        public string? RequesterEmail { get; set; }
        public string? RequesterMobileNo { get; set; }
    }
}

