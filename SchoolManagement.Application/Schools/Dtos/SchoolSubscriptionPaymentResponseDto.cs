using System;

namespace SchoolManagement.Application.Schools.Dtos;

public class SchoolSubscriptionPaymentResponseDto
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public Guid SchoolSubscriptionId { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? PaymentMode { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
}
