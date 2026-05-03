using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;

namespace SchoolManagement.Application.Staff.Services;

public interface IStaffService
{
    Task<ApiResponse<StaffResponseDto>> CreateStaffAsync(CreateStaffRequestDto request);
    Task<ApiResponse<StaffResponseDto>> UpdateStaffAsync(Guid staffId, UpdateStaffRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<StaffResponseDto>>> GetStaffAsync(bool showInactive);
    Task<ApiResponse<StaffResponseDto>> GetStaffByIdAsync(Guid staffId, bool showInactive = false);
    Task<ApiResponse<object>> SoftDeleteStaffAsync(Guid staffId);
    Task<ApiResponse<object>> SetStaffSubjectsAsync(Guid staffId, Guid[] subjectIds);
    Task<ApiResponse<object>> GetStaffSubjectsAsync(Guid staffId);
    Task<ApiResponse<IReadOnlyCollection<SubjectDto>>> GetSubjectsAsync();
    Task<ApiResponse<SubjectDto>> CreateSubjectAsync(CreateSubjectRequestDto request);
    Task<ApiResponse<SubjectDto>> UpdateSubjectAsync(Guid subjectId, UpdateSubjectRequestDto request);
    Task<ApiResponse<object>> SoftDeleteSubjectAsync(Guid subjectId);
    Task<ApiResponse<ClassTeacherAssignmentDto?>> GetStaffClassTeacherAssignmentAsync(Guid staffId);
    Task<ApiResponse<IReadOnlyCollection<ClassTeacherAssignmentDto>>> GetStaffClassTeacherAssignmentsAsync(Guid staffId);
    Task<ApiResponse<StaffBankDetailsDto?>> GetStaffBankDetailsAsync(Guid staffId);
    Task<ApiResponse<StaffSalaryStructureDto?>> GetStaffSalaryStructureAsync(Guid staffId);
    Task<ApiResponse<IReadOnlyCollection<string>>> GetStaffPermissionsAsync(Guid staffId);
    Task<ApiResponse<object>> UpdateStaffPermissionsAsync(Guid staffId, IReadOnlyCollection<string> permissions);
}

