using System;

namespace SchoolManagement.Application.Abstractions;

public interface ITenantContext
{
    Guid? SchoolId { get; }
    string? SchoolCode { get; }
}

