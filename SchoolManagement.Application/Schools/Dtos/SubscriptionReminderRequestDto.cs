namespace SchoolManagement.Application.Schools.Dtos;

public class SubscriptionReminderRequestDto
{
    public Guid SchoolId { get; set; }
    public string? Message { get; set; }
}
