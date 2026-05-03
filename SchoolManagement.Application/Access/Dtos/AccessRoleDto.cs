using System;

namespace SchoolManagement.Application.Access.Dtos;

public sealed class AccessRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; } // school_id is NULL
}

