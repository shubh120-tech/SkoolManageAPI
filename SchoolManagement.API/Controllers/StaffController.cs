using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;
using SchoolManagement.Application.Staff.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("staff")]
[Authorize]
public class StaffController : ControllerBase
{
    private readonly IStaffService _staffService;

    public StaffController(IStaffService staffService)
    {
        _staffService = staffService;
    }

    [HttpGet]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Attendance.Staff.Manage")]
    [HasPermission("Payroll.History.View.All")]
    [HasPermission("Payroll.View.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<StaffResponseDto>>>> GetStaff(
        [FromQuery] bool showInactive = false)
    {
        var result = await _staffService.GetStaffAsync(showInactive);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Attendance.Staff.Manage")]
    [HasPermission("Payroll.History.View.All")]
    [HasPermission("Payroll.View.All")]
    public async Task<ActionResult<ApiResponse<StaffResponseDto>>> GetStaffById(Guid staffId, [FromQuery] bool showInactive = false)
    {
        var result = await _staffService.GetStaffByIdAsync(staffId, showInactive);
        var status = result.Success ? 200 : (string.Equals(result.ErrorCode, ErrorCodes.NotFound, StringComparison.Ordinal) ? 404 : 400);
        return StatusCode(status, result);
    }

    [HttpPost]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<StaffResponseDto>>> CreateStaff([FromBody] CreateStaffRequestDto request)
    {
        var result = await _staffService.CreateStaffAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("{staffId:guid}")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<StaffResponseDto>>> UpdateStaff(Guid staffId, [FromBody] UpdateStaffRequestDto request)
    {
        var result = await _staffService.UpdateStaffAsync(staffId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("{staffId:guid}")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> SoftDeleteStaff(Guid staffId)
    {
        var result = await _staffService.SoftDeleteStaffAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("{staffId:guid}/subjects")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> SetStaffSubjects(Guid staffId, [FromBody] Guid[] subjectIds)
    {
        var result = await _staffService.SetStaffSubjectsAsync(staffId, subjectIds);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/subjects")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> GetStaffSubjects(Guid staffId)
    {
        var result = await _staffService.GetStaffSubjectsAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/class-teacher-assignment")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<ClassTeacherAssignmentDto?>>> GetStaffClassTeacherAssignment(Guid staffId)
    {
        var result = await _staffService.GetStaffClassTeacherAssignmentAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/class-teacher-assignments")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ClassTeacherAssignmentDto>>>> GetStaffClassTeacherAssignments(Guid staffId)
    {
        var result = await _staffService.GetStaffClassTeacherAssignmentsAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/bank-details")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<StaffBankDetailsDto?>>> GetStaffBankDetails(Guid staffId)
    {
        var result = await _staffService.GetStaffBankDetailsAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/salary-structure")]
    [HasPermission("Staff.View")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<StaffSalaryStructureDto?>>> GetStaffSalaryStructure(Guid staffId)
    {
        var result = await _staffService.GetStaffSalaryStructureAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{staffId:guid}/permissions")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<string>>>> GetStaffPermissions(Guid staffId)
    {
        var result = await _staffService.GetStaffPermissionsAsync(staffId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPut("{staffId:guid}/permissions")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateStaffPermissions(Guid staffId, [FromBody] UpdateStaffPermissionsRequest request)
    {
        var permissions = request?.Permissions ?? Array.Empty<string>();
        var result = await _staffService.UpdateStaffPermissionsAsync(staffId, permissions);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

