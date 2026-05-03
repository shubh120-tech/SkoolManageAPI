using System.Collections.Generic;

namespace SchoolManagement.Application.Access.Dtos;

public sealed class UpdateRolePermissionsRequestDto
{
    public IReadOnlyCollection<string> Permissions { get; set; } = new List<string>();
}

