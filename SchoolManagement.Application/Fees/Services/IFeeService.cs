using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Fees.Dtos;

namespace SchoolManagement.Application.Fees.Services;

public interface IFeeService
{
    Task<ApiResponse<object>> RecordFeePaymentAsync(RecordFeePaymentRequestDto request);
    Task<PaymentsListResponseDto> GetPaymentsAsync(Guid schoolId, int page, int pageSize, string? search, Guid? studentId, int? year, int? month, bool restrictToAssignedClasses = false);
    Task<ApiResponse<FeeHeadDto>> CreateFeeHeadAsync(CreateFeeHeadRequestDto request);
    Task<ApiResponse<FeeHeadDto>> UpdateFeeHeadAsync(Guid feeHeadId, UpdateFeeHeadRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<FeeHeadDto>>> GetFeeHeadsAsync(bool showInactive);
    Task<ApiResponse<object>> SoftDeleteFeeHeadAsync(Guid feeHeadId);

    Task<ApiResponse<object>> SetClassFeeStructureAsync(SetClassFeeStructureRequestDto request);
    Task<ApiResponse<object>> GetClassFeeStructureAsync(Guid classId);

    Task<ApiResponse<object>> AssignStudentFeesFromClassAsync(AssignStudentFeesFromClassRequestDto request);
    Task<ApiResponse<object>> GetStudentFeeAssignmentsAsync(Guid studentId);
    Task<ApiResponse<StudentFeeSummaryDto>> GetStudentFeeSummaryAsync(Guid studentId);
    Task<ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>> GetClassSectionFeePendingAsync(
        Guid classId,
        Guid sectionId,
        Guid academicSessionId);
    Task<ApiResponse<object>> AddOptionalFeeAsync(AddOptionalFeeRequestDto request);
    Task<ApiResponse<object>> AddStudentFeeAsync(AddStudentFeeRequestDto request);
    Task<ApiResponse<object>> SetFeeAssignmentDiscountAsync(Guid assignmentId, SetFeeAssignmentDiscountRequestDto request);
    Task<ApiResponse<object>> RemoveFeeAssignmentAsync(Guid assignmentId);
    Task<ApiResponse<object>> UpdateFeeAssignmentAsync(Guid assignmentId, UpdateFeeAssignmentRequestDto request);
}

