using System;

namespace SchoolManagement.Application.Fees.Dtos;

public class RecordFeePaymentRequestDto
{
    public Guid StudentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
}

public class FeeHeadDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
}

public class CreateFeeHeadRequestDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; } = true;
}

public class UpdateFeeHeadRequestDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; } = true;
}

public class SetClassFeeStructureRequestDto
{
    public Guid ClassId { get; set; }
    public Guid[] FeeHeadIds { get; set; } = Array.Empty<Guid>();
    public decimal[] Amounts { get; set; } = Array.Empty<decimal>();
    /// <summary>Same length as FeeHeadIds. True = optional (e.g. Transport), false = mandatory (e.g. Tuition, Admission).</summary>
    public bool[] IsOptional { get; set; } = Array.Empty<bool>();
}

public class AssignStudentFeesFromClassRequestDto
{
    public Guid StudentId { get; set; }
    public Guid ClassId { get; set; }
    public DateTime DueDate { get; set; }
}

public class AddOptionalFeeRequestDto
{
    public Guid StudentId { get; set; }
    public Guid ClassId { get; set; }
    public Guid FeeHeadId { get; set; }
    public DateTime DueDate { get; set; }
}

/// <summary>Add any fee head to a student with chosen amount (no class structure required).</summary>
public class AddStudentFeeRequestDto
{
    public Guid StudentId { get; set; }
    public Guid FeeHeadId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
}

public class SetFeeAssignmentDiscountRequestDto
{
    public decimal DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }
}

public class StudentFeeSummaryDto
{
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Pending { get; set; }
}

/// <summary>One student row for GET /fees/class-section/pending (students with pending &gt; 0).</summary>
public class ClassSectionStudentFeePendingDto
{
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public decimal TotalDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Pending { get; set; }
}

public class UpdateFeeAssignmentRequestDto
{
    public decimal Amount { get; set; }
}

/// <summary>Single row in the payments list (GET /fees/payments).</summary>
public class PaymentListItemDto
{
    public Guid Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public DateTime PaymentDate { get; set; }
}

/// <summary>Response for GET /fees/payments (paginated list + totals).</summary>
public class PaymentsListResponseDto
{
    public bool Success { get; set; } = true;
    public System.Collections.Generic.List<PaymentListItemDto> Data { get; set; } = new();
    public long TotalCount { get; set; }
    public decimal TotalCollected { get; set; }
}

