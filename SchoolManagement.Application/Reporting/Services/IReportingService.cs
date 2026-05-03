using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Reporting.Dtos;

namespace SchoolManagement.Application.Reporting.Services;

public interface IReportingService
{
    Task<ApiResponse<StudentListResultDto>> GetStudentsAsync(StudentListQueryDto query);
    Task<ApiResponse<StudentOverviewDto>> GetStudentOverviewAsync(Guid studentId);
    Task<ApiResponse<StaffPayrollSummaryDto>> GetStaffPayrollSummaryAsync(Guid staffId, int year);
    Task<ApiResponse<PayrollListResultDto>> GetPayrollListAsync(int year, int? month, int page, int pageSize);
    Task<ApiResponse<TimetableResultDto>> GetTimetableForClassAsync(Guid classId, Guid sectionId);
    Task<ApiResponse<DashboardSummaryDto>> GetDashboardSummaryAsync();
    Task<ApiResponse<GlobalSearchResultDto>> GlobalSearchAsync(string query, int limit);
    Task<ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>> GetRecentActivityAsync(int limit);
    Task<ApiResponse<StudentAttendanceMonthlyDto>> GetStudentAttendanceMonthlyAsync(Guid studentId, int year);
    Task<ApiResponse<StudentAttendanceMonthDetailDto>> GetStudentAttendanceMonthDetailAsync(Guid studentId, int year, int month);
    Task<ApiResponse<StaffAttendanceMonthlyDto>> GetStaffAttendanceMonthlyAsync(Guid staffId, int year);
    Task<ApiResponse<StaffAttendanceMonthDetailDto>> GetStaffAttendanceMonthDetailAsync(Guid staffId, int year, int month);
}

