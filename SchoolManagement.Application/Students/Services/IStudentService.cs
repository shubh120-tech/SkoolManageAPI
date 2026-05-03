using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Students.Dtos;

namespace SchoolManagement.Application.Students.Services;

public interface IStudentService
{
    Task<ApiResponse<StudentResponseDto>> AdmitStudentAsync(CreateStudentRequestDto request);
    Task<ApiResponse<object>> PromoteStudentAsync(Guid studentId, PromoteStudentRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<StudentResponseDto>>> GetStudentsAsync(Guid academicSessionId, bool showInactive);
    Task<ApiResponse<IReadOnlyCollection<StudentResponseDto>>> GetStudentsByClassAndSectionAsync(Guid classId, Guid sectionId, Guid? academicSessionId = null, bool showInactive = false);
    Task<ApiResponse<StudentResponseDto>> GetStudentByIdAsync(Guid studentId, bool showInactive = false);
    Task<ApiResponse<StudentResponseDto>> UpdateStudentAsync(Guid studentId, UpdateStudentRequestDto request);
    Task<ApiResponse<object>> SoftDeleteStudentAsync(Guid studentId);
}

