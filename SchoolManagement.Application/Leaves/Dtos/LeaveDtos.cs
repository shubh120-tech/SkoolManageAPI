using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Leaves.Dtos;

public class LeaveRequestCreateDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "General";
    public bool IsHalfDay { get; set; }
    public string? HalfDaySession { get; set; } // FirstHalf | SecondHalf (deprecated — rejected by API)
}

public class LeaveRequestListItemDto
{
    public Guid Id { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LeaveType { get; set; } = "General";
    public bool IsHalfDay { get; set; }
    public string? HalfDaySession { get; set; }
    public string Status { get; set; } = "Pending";
    public string ApplicantName { get; set; } = string.Empty;
    public string ApplicantRole { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? DecisionDate { get; set; }
    public string? AdminRemarks { get; set; }
    public string? WithdrawalStatus { get; set; }
    public DateTime? WithdrawalRequestedAt { get; set; }
}

public class LeaveRequestListResponseDto
{
    public IReadOnlyCollection<LeaveRequestListItemDto> Items { get; set; } = Array.Empty<LeaveRequestListItemDto>();
    public long TotalCount { get; set; }
}

public class LeaveDecisionRequestDto
{
    public string? Remarks { get; set; }
}

/// <summary>Admin approves withdrawal of an approved leave. Required Present/Absent for each date in the leave range that is today or earlier (IST).</summary>
public class ApproveWithdrawLeaveRequestDto
{
    public string? Remarks { get; set; }
    /// <summary>Key: yyyy-MM-dd (IST calendar date). Value: Present or Absent.</summary>
    public Dictionary<string, string>? StaffAttendanceByDate { get; set; }
}

