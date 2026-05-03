namespace SchoolManagement.Application.Schools.Dtos;

public class CreateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public int MaxStudents { get; set; }
    public int MaxStaff { get; set; }
}
