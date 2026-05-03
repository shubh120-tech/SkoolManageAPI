using System;

namespace SchoolManagement.Domain.Entities;

public class School : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsSoftDeleted { get; set; }
    public DateTime? LicenseStartDate { get; set; }
    public DateTime? LicenseEndDate { get; set; }
    public int MaxStudents { get; set; }
    public int MaxStaff { get; set; }
}

