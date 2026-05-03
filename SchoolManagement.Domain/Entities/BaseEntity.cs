using System;

namespace SchoolManagement.Domain.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public Guid? SchoolId { get; set; } // null for platform-level records

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

