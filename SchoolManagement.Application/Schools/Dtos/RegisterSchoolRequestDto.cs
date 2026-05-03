namespace SchoolManagement.Application.Schools.Dtos;

public class RegisterSchoolRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string? AdminMobile { get; set; }
    public string? AddressLine { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public DateTime SubscriptionStartDate { get; set; }
    public DateTime SubscriptionEndDate { get; set; }
    public bool AutoRenew { get; set; }
}

