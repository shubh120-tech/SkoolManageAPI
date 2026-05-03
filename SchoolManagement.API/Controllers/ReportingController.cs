using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Reporting.Dtos;
using SchoolManagement.Application.Reporting.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("reporting")]
[Authorize]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;

    public ReportingController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpGet("dashboard-summary")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> GetDashboardSummary()
    {
        var result = await _reportingService.GetDashboardSummaryAsync();
        return Ok(result);
    }

    [HttpGet("recent-activity")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>>> GetRecentActivity(
        [FromQuery] int limit = 10)
    {
        var result = await _reportingService.GetRecentActivityAsync(limit);
        return Ok(result);
    }

    [HttpGet("students")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<StudentListResultDto>>> GetStudents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var query = new StudentListQueryDto
        {
            Page = page,
            PageSize = pageSize,
            Search = search
        };

        var result = await _reportingService.GetStudentsAsync(query);
        return Ok(result);
    }

    [HttpGet("students/{studentId:guid}/overview")]
    [HasPermission("Reporting.Students.Overview")]
    public async Task<ActionResult<ApiResponse<StudentOverviewDto>>> GetStudentOverview(Guid studentId)
    {
        var result = await _reportingService.GetStudentOverviewAsync(studentId);
        return StatusCode(result.Success ? 200 : 404, result);
    }

    [HttpGet("staff/{staffId:guid}/payroll")]
    [HasPermission("Reporting.Staff.Payroll")]
    [HasPermission("Payroll.View")]
    [HasPermission("Payroll.View.All")]
    [HasPermission("Payroll.History.View")]
    [HasPermission("Payroll.History.View.All")]
    public async Task<ActionResult<ApiResponse<StaffPayrollSummaryDto>>> GetStaffPayroll(Guid staffId, [FromQuery] int year)
    {
        var result = await _reportingService.GetStaffPayrollSummaryAsync(staffId, year);
        return StatusCode(result.Success ? 200 : 404, result);
    }

    [HttpGet("payroll-list")]
    [HasPermission("Payroll.History.View.All")]
    [HasPermission("Payroll.Pay.All")]
    [HasPermission("Payroll.View.All")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<PayrollListResultDto>>> GetPayrollList(
        [FromQuery] int year,
        [FromQuery] int? month,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _reportingService.GetPayrollListAsync(year, month, page, pageSize);
        return Ok(result);
    }

    [HttpGet("global-search")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<GlobalSearchResultDto>>> GlobalSearch(
        [FromQuery] string query,
        [FromQuery] int limit = 10)
    {
        var result = await _reportingService.GlobalSearchAsync(query ?? string.Empty, limit);
        return Ok(result);
    }

    [HttpGet("students/{studentId:guid}/attendance-monthly")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<StudentAttendanceMonthlyDto>>> GetStudentAttendanceMonthly(
        Guid studentId,
        [FromQuery] int year)
    {
        var result = await _reportingService.GetStudentAttendanceMonthlyAsync(studentId, year);
        return Ok(result);
    }

    [HttpGet("students/{studentId:guid}/attendance-month-detail")]
    [HasPermission("Reporting.Students.View")]
    public async Task<ActionResult<ApiResponse<StudentAttendanceMonthDetailDto>>> GetStudentAttendanceMonthDetail(
        Guid studentId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await _reportingService.GetStudentAttendanceMonthDetailAsync(studentId, year, month);
        return Ok(result);
    }

    [HttpGet("staff/{staffId:guid}/attendance-monthly")]
    [HasPermission("Reporting.Staff.View")]
    public async Task<ActionResult<ApiResponse<StaffAttendanceMonthlyDto>>> GetStaffAttendanceMonthly(
        Guid staffId,
        [FromQuery] int year)
    {
        var result = await _reportingService.GetStaffAttendanceMonthlyAsync(staffId, year);
        return Ok(result);
    }

    [HttpGet("staff/{staffId:guid}/attendance-month-detail")]
    [HasPermission("Reporting.Staff.View")]
    public async Task<ActionResult<ApiResponse<StaffAttendanceMonthDetailDto>>> GetStaffAttendanceMonthDetail(
        Guid staffId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await _reportingService.GetStaffAttendanceMonthDetailAsync(staffId, year, month);
        return Ok(result);
    }

    [HttpGet("timetable")]
    [HasPermission("Timetable.View")]
    public async Task<ActionResult<ApiResponse<TimetableResultDto>>> GetTimetable(
        [FromQuery] Guid classId,
        [FromQuery] Guid sectionId)
    {
        var result = await _reportingService.GetTimetableForClassAsync(classId, sectionId);
        return Ok(result);
    }
}

