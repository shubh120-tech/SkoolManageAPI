using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Application.Academic.Services;

public interface IAcademicSessionService
{
    Task<ApiResponse<AcademicSessionDto>> CreateAsync(CreateAcademicSessionRequestDto request);
    Task<ApiResponse<AcademicSessionDto>> UpdateAsync(Guid sessionId, UpdateAcademicSessionRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<AcademicSessionDto>>> GetAllAsync();
    Task<ApiResponse<AcademicSessionDto?>> GetActiveAsync();
}

