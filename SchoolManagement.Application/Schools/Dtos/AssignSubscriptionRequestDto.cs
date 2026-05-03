namespace SchoolManagement.Application.Schools.Dtos;

public class AssignSubscriptionRequestDto
{
    public Guid SubscriptionPlanId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool AutoRenew { get; set; }
}
