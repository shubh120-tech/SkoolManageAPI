using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Leaves.Dtos;
using SchoolManagement.Application.Leaves.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("leave-requests")]
[Authorize]
public class LeaveRequestsController : ControllerBase
{
    private readonly ILeaveService _leaveService;

    public LeaveRequestsController(ILeaveService leaveService)
    {
        _leaveService = leaveService;
    }

    [HttpPost]
    [HasPermission("Leave.Apply")]
    public async Task<ActionResult<ApiResponse<object>>> CreateLeave([FromBody] LeaveRequestCreateDto request)
    {
        var result = await _leaveService.CreateLeaveAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("my")]
    [HasPermission("Leave.ViewOwn")]
    public async Task<ActionResult<ApiResponse<LeaveRequestListResponseDto>>> GetMyLeaves(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null)
    {
        var result = await _leaveService.GetMyLeavesAsync(page, pageSize, status, year, month);
        return Ok(result);
    }

    [HttpGet]
    [HasPermission("Staff.Manage")]
    [HasPermission("Leave.Approve")]
    [HasPermission("Leave.View.All")]
    [HasPermission("Leave.Approve.All")]
    public async Task<ActionResult<ApiResponse<LeaveRequestListResponseDto>>> GetAllLeaves(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null)
    {
        var result = await _leaveService.GetAllLeavesAsync(page, pageSize, status, year, month);
        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Leave.Approve")]
    [HasPermission("Leave.Approve.All")]
    public async Task<ActionResult<ApiResponse<object>>> ApproveLeave(Guid id, [FromBody] LeaveDecisionRequestDto request)
    {
        var result = await _leaveService.ApproveLeaveAsync(id, request.Remarks);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Leave.Approve")]
    [HasPermission("Leave.Approve.All")]
    public async Task<ActionResult<ApiResponse<object>>> RejectLeave(Guid id, [FromBody] LeaveDecisionRequestDto request)
    {
        var result = await _leaveService.RejectLeaveAsync(id, request.Remarks);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    /// <summary>Staff requests to withdraw an approved leave (pending admin).</summary>
    [HttpPost("{id:guid}/request-withdraw")]
    [HasPermission("Leave.Apply")]
    public async Task<ActionResult<ApiResponse<object>>> RequestWithdrawLeave(Guid id)
    {
        var result = await _leaveService.RequestWithdrawLeaveAsync(id);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("{id:guid}/reject-withdraw")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Leave.Approve")]
    [HasPermission("Leave.Approve.All")]
    public async Task<ActionResult<ApiResponse<object>>> RejectWithdrawLeave(Guid id, [FromBody] LeaveDecisionRequestDto request)
    {
        var result = await _leaveService.RejectWithdrawLeaveAsync(id, request.Remarks);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("{id:guid}/approve-withdraw")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Leave.Approve")]
    [HasPermission("Leave.Approve.All")]
    public async Task<ActionResult<ApiResponse<object>>> ApproveWithdrawLeave(Guid id, [FromBody] ApproveWithdrawLeaveRequestDto? request)
    {
        var result = await _leaveService.ApproveWithdrawLeaveAsync(id, request ?? new ApproveWithdrawLeaveRequestDto());
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

