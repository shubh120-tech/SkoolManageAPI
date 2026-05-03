using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Access.Dtos;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Application.Access.Services;

public interface IAccessService
{
    Task<ApiResponse<IReadOnlyCollection<AccessRoleDto>>> GetRolesAsync();
    Task<ApiResponse<AccessRoleDto>> CreateRoleAsync(CreateRoleRequestDto request);

    Task<ApiResponse<IReadOnlyCollection<AccessPermissionDto>>> GetPermissionsCatalogAsync();
    Task<ApiResponse<IReadOnlyCollection<string>>> GetRolePermissionsAsync(Guid roleId);
    Task<ApiResponse<object>> UpdateRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions);

    Task<ApiResponse<IReadOnlyCollection<AccessStaffUserDto>>> GetStaffUsersAsync(bool showInactive);
    Task<ApiResponse<object>> AssignStaffRoleAsync(Guid staffId, Guid roleId);
}

