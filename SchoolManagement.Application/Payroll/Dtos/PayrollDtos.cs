using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Payroll.Dtos;

public class GeneratePayrollRequestDto
{
    /// <summary>
    /// When set, generate payroll only for this staff member.
    /// When null and GenerateForAllStaff is true, generate for all staff with salary structures.
    /// </summary>
    public Guid? StaffId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public bool GenerateForAllStaff { get; set; }
}

public class StaffPaymentRequestDto
{
    public Guid PayrollRecordId { get; set; }
    public Guid StaffId { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>One recorded salary payment (staff_payments row) with staff and payroll period context.</summary>
public class StaffSalaryPaymentHistoryItemDto
{
    public Guid Id { get; set; }
    public Guid PayrollRecordId { get; set; }
    public Guid StaffId { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class StaffSalaryPaymentHistoryResultDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<StaffSalaryPaymentHistoryItemDto> Items { get; set; } = Array.Empty<StaffSalaryPaymentHistoryItemDto>();
}

