using System;

namespace SchoolManagement.Application.Access.Dtos;

public sealed class AccessStaffUserDto
{
    public Guid StaffId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsTeaching { get; set; }

    public Guid? UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? RoleName { get; set; }
}

