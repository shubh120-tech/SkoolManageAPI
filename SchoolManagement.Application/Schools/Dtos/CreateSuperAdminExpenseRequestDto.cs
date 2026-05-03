namespace SchoolManagement.Application.Schools.Dtos;

public class CreateSuperAdminExpenseRequestDto
{
    public string ExpenseType { get; set; } = string.Empty;
    public string Frequency { get; set; } = "Monthly";
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string? Vendor { get; set; }
    public string? Remarks { get; set; }
}
