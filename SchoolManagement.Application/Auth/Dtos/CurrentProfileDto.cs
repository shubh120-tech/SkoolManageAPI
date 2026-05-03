using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Auth.Dtos;

public class CurrentProfileDto
{
    public Guid UserId { get; set; }
    public Guid? SchoolId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public string? SchoolCode { get; set; }
    public string? SchoolName { get; set; }

    // High-level type of entity this user represents
    public string? EntityType { get; set; } // SuperAdmin, SchoolAdmin, Staff, Student

    // Staff-specific fields
    public Guid? StaffId { get; set; }
    public string? StaffCode { get; set; }
    public string? StaffMobileNo { get; set; }

    // Class teacher assignment (first one when staff is a class teacher)
    public Guid? ClassTeacherClassId { get; set; }
    public Guid? ClassTeacherSectionId { get; set; }
    public Guid? ClassTeacherAcademicSessionId { get; set; }
    public IReadOnlyCollection<CurrentClassTeacherAssignmentDto> ClassTeacherAssignments { get; set; } = Array.Empty<CurrentClassTeacherAssignmentDto>();

    /// <summary>True if this staff user has at least one class-teacher assignment.</summary>
    public bool HasClassTeacherAssignment { get; set; }

    /// <summary>True if this staff appears on at least one timetable row in the school.</summary>
    public bool HasTimetableSlots { get; set; }

    // Student-specific fields
    public Guid? StudentId { get; set; }
    public string? AdmissionNo { get; set; }
    public string? ParentMobileNo { get; set; }

    /// <summary>Effective school-scoped feature flags (e.g. WhatsApp.Basic, WhatsApp.Premium). SuperAdmin users typically have no school.</summary>
    public Dictionary<string, bool>? SchoolFeatures { get; set; }
}

public class CurrentClassTeacherAssignmentDto
{
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public Guid AcademicSessionId { get; set; }
}

