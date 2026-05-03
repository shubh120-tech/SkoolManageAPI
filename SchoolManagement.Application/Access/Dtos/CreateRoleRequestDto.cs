namespace SchoolManagement.Application.Access.Dtos;

public sealed class CreateRoleRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

