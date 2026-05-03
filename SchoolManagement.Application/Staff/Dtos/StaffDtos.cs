using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Staff.Dtos;

public class SubjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public class CreateSubjectRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public class UpdateSubjectRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public class ClassTeacherAssignmentDto
{
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public Guid AcademicSessionId { get; set; }
}

public class CreateStaffRequestDto
{
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsTeaching { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MobileNo { get; set; }
    public string? AddressLine { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public Guid? ClassId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? AcademicSessionId { get; set; }
    public Guid[]? SubjectIds { get; set; }
    public decimal? Basic { get; set; }
    public decimal? Allowances { get; set; }
    public decimal? Deductions { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountHolderName { get; set; }
    /// <summary>When true (default), send welcome / login instructions to <see cref="Email"/> after create.</summary>
    public bool SendWelcomeEmail { get; set; } = true;
}

public class StaffResponseDto
{
    public Guid Id { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MobileNo { get; set; }
    public string? AddressLine { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public bool IsTeaching { get; set; }
}

public class StaffBankDetailsDto
{
    public string AccountHolderName { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string IfscCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
}

public class UpdateStaffRequestDto
{
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsTeaching { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MobileNo { get; set; }
    public string? AddressLine { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public Guid? ClassId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? AcademicSessionId { get; set; }
    public Guid[]? SubjectIds { get; set; }
    public decimal? Basic { get; set; }
    public decimal? Allowances { get; set; }
    public decimal? Deductions { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountHolderName { get; set; }
}

public class StaffSalaryStructureDto
{
    public decimal Basic { get; set; }
    public decimal Allowances { get; set; }
    public decimal Deductions { get; set; }
}

public class UpdateStaffPermissionsRequest
{
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
}

