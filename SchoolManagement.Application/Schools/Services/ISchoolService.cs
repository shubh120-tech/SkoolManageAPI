using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools.Dtos;

namespace SchoolManagement.Application.Schools.Services;

public interface ISchoolService
{
    Task<ApiResponse<SchoolResponseDto>> RegisterSchoolAsync(RegisterSchoolRequestDto request);
    Task<ApiResponse<SchoolResponseDto>> UpdateSchoolAsync(Guid schoolId, UpdateSchoolRequestDto request);
    Task<ApiResponse<PagedResult<SubscriptionPlanResponseDto>>> GetSubscriptionPlansAsync(int page = 1, int pageSize = 20);
    Task<ApiResponse<SubscriptionPlanResponseDto>> CreateSubscriptionPlanAsync(CreateSubscriptionPlanRequestDto request);
    Task<ApiResponse<SubscriptionPlanResponseDto>> UpdateSubscriptionPlanAsync(Guid planId, UpdateSubscriptionPlanRequestDto request);
    Task<ApiResponse<object>> AssignSubscriptionAsync(Guid schoolId, AssignSubscriptionRequestDto request);
    Task<ApiResponse<object>> RecordSubscriptionPaymentAsync(RecordSchoolSubscriptionPaymentRequestDto request);
    Task<ApiResponse<object>> SendSubscriptionReminderAsync(SubscriptionReminderRequestDto request);
    Task<ApiResponse<PagedResult<SchoolSubscriptionPaymentResponseDto>>> GetSubscriptionPaymentsAsync(Guid? schoolId = null, int page = 1, int pageSize = 20);
    Task<ApiResponse<PagedResult<SuperAdminExpenseResponseDto>>> GetExpensesAsync(int page = 1, int pageSize = 20, string? search = null, int? month = null, int? year = null);
    Task<ApiResponse<SuperAdminExpenseResponseDto>> CreateExpenseAsync(CreateSuperAdminExpenseRequestDto request);
    Task<ApiResponse<SuperAdminExpenseSummaryDto>> GetExpenseSummaryAsync(int? month = null, int? year = null);
    Task<ApiResponse<SuperAdminDashboardSummaryDto>> GetDashboardSummaryAsync();
    Task<ApiResponse<object>> ActivateSchoolAsync(Guid schoolId);
    Task<ApiResponse<object>> DeactivateSchoolAsync(Guid schoolId);
    Task<ApiResponse<object>> SoftDeleteSchoolAsync(Guid schoolId);
    Task<ApiResponse<PagedResult<SchoolResponseDto>>> GetSchoolsAsync(int page = 1, int pageSize = 20);
    Task<ApiResponse<SchoolResponseDto>> GetCurrentSchoolAsync();
}

