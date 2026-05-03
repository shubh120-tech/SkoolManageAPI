namespace SchoolManagement.Application.Schools.Dtos;

public class UpdateSchoolRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AdminName { get; set; }
    public string? AdminEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? AddressLine { get; set; }
}
