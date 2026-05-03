using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Payroll.Dtos;

namespace SchoolManagement.Application.Payroll.Services;

public interface IPayrollService
{
    Task<ApiResponse<object>> GeneratePayrollAsync(GeneratePayrollRequestDto request);
    Task<ApiResponse<object>> RecordStaffPaymentAsync(StaffPaymentRequestDto request);
    Task<ApiResponse<StaffSalaryPaymentHistoryResultDto>> GetStaffSalaryPaymentHistoryAsync(
        int? year,
        int? month,
        Guid? staffId,
        int page,
        int pageSize,
        string? search);
}

