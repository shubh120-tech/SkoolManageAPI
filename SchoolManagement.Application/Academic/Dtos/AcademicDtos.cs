using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Academic.Dtos;

public class ClassDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public Guid AcademicSessionId { get; set; }
}

public class CreateClassRequestDto
{
    public Guid AcademicSessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
}

public class UpdateClassRequestDto
{
    public Guid AcademicSessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
}

public class SectionDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
}

public class CreateSectionRequestDto
{
    public Guid ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
}

public class UpdateSectionRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
}

