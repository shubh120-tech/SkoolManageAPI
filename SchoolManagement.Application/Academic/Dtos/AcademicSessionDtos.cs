using System;

namespace SchoolManagement.Application.Academic.Dtos;

public class AcademicSessionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
}

public class CreateAcademicSessionRequestDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// When set, duplicates all classes and sections from this session into the new session (same names/capacities).
    /// </summary>
    public Guid? CopyClassStructureFromSessionId { get; set; }
}

public class UpdateAcademicSessionRequestDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
}

