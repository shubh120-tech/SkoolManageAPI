namespace SchoolManagement.Application.Schools.Dtos;

public class SuperAdminExpenseResponseDto
{
    public Guid Id { get; set; }
    public string ExpenseType { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string? Vendor { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
}
