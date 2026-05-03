using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;

namespace SchoolManagement.Application.Academic.Services;

public interface IAcademicService
{
    Task<ApiResponse<ClassDto>> CreateClassAsync(CreateClassRequestDto request);
    Task<ApiResponse<ClassDto>> UpdateClassAsync(Guid classId, UpdateClassRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<ClassDto>>> GetClassesAsync(Guid? academicSessionId = null);

    Task<ApiResponse<SectionDto>> CreateSectionAsync(CreateSectionRequestDto request);
    Task<ApiResponse<SectionDto>> UpdateSectionAsync(Guid sectionId, UpdateSectionRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<SectionDto>>> GetSectionsByClassAsync(Guid classId, Guid? academicSessionId = null);

    Task<ApiResponse<object>> AssignClassTeacherAsync(Guid teacherId, Guid classId, Guid sectionId, Guid academicSessionId);
    Task<ApiResponse<object>> UnassignClassTeacherAsync(Guid classId, Guid sectionId, Guid academicSessionId);
    Task<ApiResponse<object>> GetClassTeacherAsync(Guid classId, Guid sectionId, Guid academicSessionId);

    Task<ApiResponse<IReadOnlyCollection<SubjectDto>>> GetClassSubjectsAsync(Guid classId);
    Task<ApiResponse<object>> SetClassSubjectsAsync(Guid classId, Guid[] subjectIds);
}

