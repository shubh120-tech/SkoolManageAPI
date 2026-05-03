namespace SchoolManagement.Application.Schools.Dtos;

public class UpdateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public int MaxStudents { get; set; }
    public int MaxStaff { get; set; }
    public bool IsActive { get; set; } = true;
}
