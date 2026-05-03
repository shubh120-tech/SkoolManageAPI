using System;

namespace SchoolManagement.Application.Students.Dtos;

public class CreateStudentRequestDto
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;

    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public Guid AcademicSessionId { get; set; }

    public DateOnly DateOfBirth { get; set; }
    public string? Email { get; set; }

    public string? ParentMobileNo { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? BloodGroup { get; set; }
    public string? AadharNo { get; set; }
    public string? UdiseNo { get; set; }
    public string? FatherAadharNo { get; set; }
    public string? MotherAadharNo { get; set; }
    public string? FatherOccupation { get; set; }
    public string? MotherOccupation { get; set; }
    public string? PenNo { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
}

public class StudentResponseDto
{
    public Guid Id { get; set; }
    public string AdmissionNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public Guid AcademicSessionId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? ParentMobileNo { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? AddressLine { get; set; }
    public string? BloodGroup { get; set; }
    public string? AadharNo { get; set; }
    public int? RollNo { get; set; }
    public string? UdiseNo { get; set; }
    public string? FatherAadharNo { get; set; }
    public string? MotherAadharNo { get; set; }
    public string? FatherOccupation { get; set; }
    public string? MotherOccupation { get; set; }
    public string? PenNo { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
}

public class UpdateStudentRequestDto
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? ParentMobileNo { get; set; }
    public string? AddressLine { get; set; }
    public Guid ClassId { get; set; }
    public Guid SectionId { get; set; }
    public string? BloodGroup { get; set; }
    public string? AadharNo { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? UdiseNo { get; set; }
    public string? FatherAadharNo { get; set; }
    public string? MotherAadharNo { get; set; }
    public string? FatherOccupation { get; set; }
    public string? MotherOccupation { get; set; }
    public string? PenNo { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
}

public class PromoteStudentRequestDto
{
    public Guid ToClassId { get; set; }
    public Guid ToSectionId { get; set; }
    public Guid ToAcademicSessionId { get; set; }
}

