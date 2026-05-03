using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Access.Dtos;
using SchoolManagement.Application.Access.Services;
using SchoolManagement.Application.Common;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("access")]
[Authorize]
public sealed class AccessController : ControllerBase
{
    private readonly IAccessService _accessService;

    public AccessController(IAccessService accessService)
    {
        _accessService = accessService;
    }

    [HttpGet("roles")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> GetRoles()
    {
        var result = await _accessService.GetRolesAsync();
        return Ok(result);
    }

    [HttpPost("roles")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> CreateRole([FromBody] CreateRoleRequestDto request)
    {
        var result = await _accessService.CreateRoleAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("permissions")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> GetPermissionsCatalog()
    {
        var result = await _accessService.GetPermissionsCatalogAsync();
        return Ok(result);
    }

    [HttpGet("roles/{roleId:guid}/permissions")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> GetRolePermissions(Guid roleId)
    {
        var result = await _accessService.GetRolePermissionsAsync(roleId);
        return Ok(result);
    }

    [HttpPut("roles/{roleId:guid}/permissions")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateRolePermissions(Guid roleId, [FromBody] UpdateRolePermissionsRequestDto request)
    {
        var permissions = request?.Permissions ?? Array.Empty<string>();
        var result = await _accessService.UpdateRolePermissionsAsync(roleId, permissions);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("staff-users")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> GetStaffUsers([FromQuery] bool showInactive = false)
    {
        var result = await _accessService.GetStaffUsersAsync(showInactive);
        return Ok(result);
    }

    [HttpPut("staff/{staffId:guid}/role")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> AssignStaffRole(Guid staffId, [FromBody] AssignStaffRoleRequestDto request)
    {
        var result = await _accessService.AssignStaffRoleAsync(staffId, request?.RoleId ?? Guid.Empty);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

