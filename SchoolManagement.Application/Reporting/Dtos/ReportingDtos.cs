using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Reporting.Dtos;

public class StudentListQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
}

public class StudentListItemDto
{
    public Guid Id { get; set; }
    public string AdmissionNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public string? Email { get; set; }
    public string? ClassName { get; set; }
    public string? SectionName { get; set; }
    public string? MobileNo { get; set; }
    public decimal PendingAmount { get; set; }
}

public class StudentListResultDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyCollection<StudentListItemDto> Items { get; set; } = Array.Empty<StudentListItemDto>();
}

public class StudentOverviewDto
{
    public Guid Id { get; set; }
    public string AdmissionNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public string? ClassName { get; set; }
    public string? SectionName { get; set; }
    public string? ParentMobileNo { get; set; }
    public decimal AttendancePercentage { get; set; }
    public decimal TotalFeeDue { get; set; }
    public decimal TotalFeePaid { get; set; }
    public decimal Outstanding { get; set; }
}

public class StaffPayrollItemDto
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime GeneratedOn { get; set; }
    public decimal PreviousPending { get; set; }
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal PendingAmount { get; set; }
}

public class StaffPayrollSummaryDto
{
    public Guid StaffId { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int Year { get; set; }
    public IReadOnlyCollection<StaffPayrollItemDto> Payroll { get; set; } = Array.Empty<StaffPayrollItemDto>();
}

public class PayrollListItemDto
{
    /// <summary>Null when staff has no generated payroll for the selected month/year.</summary>
    public Guid? PayrollRecordId { get; set; }
    public Guid StaffId { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal Deduction { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime? GeneratedOn { get; set; }
    public decimal PreviousPending { get; set; }
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal PendingAmount { get; set; }
}

public class PayrollListResultDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyCollection<PayrollListItemDto> Items { get; set; } = Array.Empty<PayrollListItemDto>();
}

public class TimetableSlotDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public int DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class TimetableResultDto
{
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public IReadOnlyCollection<TimetableSlotDto> Entries { get; set; } = Array.Empty<TimetableSlotDto>();
}

public class StudentAttendanceMonthlyItemDto
{
    public int Month { get; set; }
    public int PresentDays { get; set; }
    public int TotalDays { get; set; }
    public int AbsentDays { get; set; }
    public decimal Percentage { get; set; }
}

public class StudentAttendanceMonthlyDto
{
    public Guid StudentId { get; set; }
    public int Year { get; set; }
    public IReadOnlyCollection<StudentAttendanceMonthlyItemDto> Items { get; set; } = Array.Empty<StudentAttendanceMonthlyItemDto>();
}

public class StudentAttendanceDayDto
{
    public DateTime Date { get; set; }
    public bool IsPresent { get; set; }
    public string? Status { get; set; }
    public Guid? ClassId { get; set; }
    public Guid? SectionId { get; set; }
}

public class StudentAttendanceMonthDetailDto
{
    public Guid StudentId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyCollection<StudentAttendanceDayDto> Items { get; set; } = Array.Empty<StudentAttendanceDayDto>();
}

public class StaffAttendanceMonthlyItemDto
{
    public int Month { get; set; }
    public int PresentDays { get; set; }
    public int TotalDays { get; set; }
    public int AbsentDays { get; set; }
    public decimal Percentage { get; set; }
}

public class StaffAttendanceMonthlyDto
{
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public IReadOnlyCollection<StaffAttendanceMonthlyItemDto> Items { get; set; } = Array.Empty<StaffAttendanceMonthlyItemDto>();
}

public class StaffAttendanceDayDto
{
    public DateTime Date { get; set; }
    public bool IsPresent { get; set; }
    public string? Status { get; set; }
}

public class StaffAttendanceMonthDetailDto
{
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyCollection<StaffAttendanceDayDto> Items { get; set; } = Array.Empty<StaffAttendanceDayDto>();
}

public class DashboardSummaryDto
{
    public int TotalStudents { get; set; }
    public int TotalStaff { get; set; }
    public decimal TotalCollection { get; set; }
    public decimal MonthlyCollection { get; set; }
    public decimal TotalPending { get; set; }
    public decimal TodayAttendancePercentage { get; set; }
    public int TodayPresentCount { get; set; }
    public int TodayMarkedCount { get; set; }
}

public class StaffSearchItemDto
{
    public Guid Id { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? MobileNo { get; set; }
    public bool IsTeaching { get; set; }
}

public class GlobalSearchResultDto
{
    public IReadOnlyCollection<StudentListItemDto> Students { get; set; } = Array.Empty<StudentListItemDto>();
    public IReadOnlyCollection<StaffSearchItemDto> Staff { get; set; } = Array.Empty<StaffSearchItemDto>();
}

public class RecentActivityItemDto
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

