using System;

namespace SchoolManagement.Application.Schools.Dtos;

public class SchoolResponseDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsSoftDeleted { get; set; }
    public string? AdminName { get; set; }
    public string? ContactPhone { get; set; }
    public string? AddressLine { get; set; }
    public string? SubscriptionName { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal DueAmount { get; set; }
}

