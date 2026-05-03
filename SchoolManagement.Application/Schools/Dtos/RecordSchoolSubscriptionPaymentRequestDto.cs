namespace SchoolManagement.Application.Schools.Dtos;

public class RecordSchoolSubscriptionPaymentRequestDto
{
    public Guid SchoolId { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? PaymentMode { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
}
