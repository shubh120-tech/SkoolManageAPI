using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Attendance.Dtos;
using SchoolManagement.Application.Attendance.Services;
using SchoolManagement.Application.Common;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;

    public AttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet("day")]
    [HasPermission("Attendance.Student.Manage")]
    public async Task<ActionResult<ApiResponse<GetAttendanceDayResponseDto>>> GetAttendanceDay([FromQuery] Guid classId, [FromQuery] Guid sectionId, [FromQuery] DateTime date)
    {
        var result = await _attendanceService.GetAttendanceDayAsync(classId, sectionId, date);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("day")]
    [HasPermission("Attendance.Student.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAttendanceDay([FromBody] MarkAttendanceDayRequestDto request)
    {
        var result = await _attendanceService.MarkAttendanceDayAsync(request);
        if (!result.Success && string.Equals(result.ErrorCode, ErrorCodes.Forbidden, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("staff/day")]
    [HasPermission("Attendance.Staff.Manage")]
    public async Task<ActionResult<ApiResponse<GetStaffAttendanceDayResponseDto>>> GetStaffAttendanceDay([FromQuery] DateTime date)
    {
        var result = await _attendanceService.GetStaffAttendanceDayAsync(date);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("staff/day")]
    [HasPermission("Attendance.Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> MarkStaffAttendanceDay([FromBody] MarkStaffAttendanceDayRequestDto request)
    {
        var result = await _attendanceService.MarkStaffAttendanceDayAsync(request);
        if (!result.Success && string.Equals(result.ErrorCode, ErrorCodes.Forbidden, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, result);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

