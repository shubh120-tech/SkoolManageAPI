using System;
using SchoolManagement.Application.Abstractions;

namespace SchoolManagement.Infrastructure.MultiTenancy;

public class TenantContext : ITenantContext
{
    public Guid? SchoolId { get; set; }
    public string? SchoolCode { get; set; }
}

