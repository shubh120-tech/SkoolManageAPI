using System;
using System.Threading.Tasks;

namespace SchoolManagement.Application.Abstractions;

public interface INotificationDeliveryService
{
    Task NotifyLeaveCreatedAsync(
        Guid schoolId,
        Guid requesterUserId,
        DateTime fromDate,
        DateTime toDate,
        string reason,
        string leaveType,
        bool isHalfDay,
        string? halfDaySession);

    Task NotifyLeaveDecisionAsync(
        Guid schoolId,
        Guid leaveId,
        string decisionStatus,
        string? remarks,
        Guid decidedByUserId);

    /// <summary>Notifies school admins / leave approvers that a staff member requested to withdraw an approved leave.</summary>
    Task NotifyLeaveWithdrawRequestedAsync(Guid schoolId, Guid leaveId, Guid requesterUserId);

    /// <summary>Notifies the applicant when an admin approves or rejects the withdraw request.</summary>
    Task NotifyLeaveWithdrawalDecisionAsync(
        Guid schoolId,
        Guid leaveId,
        bool withdrawalApproved,
        string? remarks,
        Guid decidedByUserId);
}

