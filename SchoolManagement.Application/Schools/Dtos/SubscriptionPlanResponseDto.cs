using System;

namespace SchoolManagement.Application.Schools.Dtos;

public class SubscriptionPlanResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public int MaxStudents { get; set; }
    public int MaxStaff { get; set; }
    public bool IsActive { get; set; }
}
