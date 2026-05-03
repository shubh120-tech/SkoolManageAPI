using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Access.Dtos;
using SchoolManagement.Application.Access.Services;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Infrastructure.Services;

public sealed class AccessService : IAccessService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public AccessService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<IReadOnlyCollection<AccessRoleDto>>> GetRolesAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<AccessRoleDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;
        var currentRole = (_currentUser.Role ?? string.Empty).Trim();
        var isSuperAdmin = string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

        var rows = await conn.QueryAsync<AccessRoleDto>(@"
SELECT
    id AS Id,
    name AS Name,
    description AS Description,
    (school_id IS NULL) AS IsSystem
FROM roles
WHERE is_deleted = FALSE
  AND (school_id = @SchoolId OR school_id IS NULL)
  AND (@IsSuperAdmin = TRUE OR name <> 'SuperAdmin')
ORDER BY (school_id IS NULL) DESC, name;", new { SchoolId = schoolId, IsSuperAdmin = isSuperAdmin });

        return ApiResponse<IReadOnlyCollection<AccessRoleDto>>.Ok(rows.AsList(), "Roles fetched.");
    }

    public async Task<ApiResponse<AccessRoleDto>> CreateRoleAsync(CreateRoleRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<AccessRoleDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var name = (request?.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return ApiResponse<AccessRoleDto>.Fail("Role name is required.", ErrorCodes.Validation);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;
        var createdBy = _currentUser.UserId ?? Guid.Empty;

        // Unique per school (case-insensitive)
        var exists = await conn.ExecuteScalarAsync<int>(@"
SELECT 1
FROM roles
WHERE school_id = @SchoolId
  AND is_deleted = FALSE
  AND LOWER(name) = LOWER(@Name)
LIMIT 1;", new { SchoolId = schoolId, Name = name });
        if (exists == 1)
            return ApiResponse<AccessRoleDto>.Fail("Role name already exists.", ErrorCodes.Conflict);

        var id = await conn.ExecuteScalarAsync<Guid>(@"
INSERT INTO roles (id, school_id, is_deleted, created_at, created_by, name, description)
VALUES (md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy, @Name, @Description)
RETURNING id;", new
        {
            SchoolId = schoolId,
            CreatedBy = createdBy,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description!.Trim()
        });

        return ApiResponse<AccessRoleDto>.Ok(new AccessRoleDto
        {
            Id = id,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description!.Trim(),
            IsSystem = false
        }, "Role created.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<AccessPermissionDto>>> GetPermissionsCatalogAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<AccessPermissionDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;

        var rows = await conn.QueryAsync<AccessPermissionDto>(@"
SELECT name AS Name, description AS Description
FROM permissions
WHERE is_deleted = FALSE
  AND (school_id = @SchoolId OR school_id IS NULL)
ORDER BY name;", new { SchoolId = schoolId });

        return ApiResponse<IReadOnlyCollection<AccessPermissionDto>>.Ok(rows.AsList(), "Permissions fetched.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<string>>> GetRolePermissionsAsync(Guid roleId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<string>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;

        // Ensure role belongs to this school or is system role
        var roleSchoolId = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT school_id
FROM roles
WHERE id = @RoleId AND is_deleted = FALSE
LIMIT 1;", new { RoleId = roleId });
        if (roleSchoolId != null && roleSchoolId.Value != schoolId)
            return ApiResponse<IReadOnlyCollection<string>>.Fail("Role not found.", ErrorCodes.NotFound);

        var list = (await conn.QueryAsync<string>(@"
SELECT p.name
FROM role_permissions rp
INNER JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
WHERE rp.role_id = @RoleId
  AND rp.is_deleted = FALSE
ORDER BY p.name;", new { RoleId = roleId })).AsList();

        return ApiResponse<IReadOnlyCollection<string>>.Ok(list, "Role permissions fetched.");
    }

    public async Task<ApiResponse<object>> UpdateRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissions)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;
        var updatedBy = _currentUser.UserId ?? Guid.Empty;

        var roleRow = await conn.QueryFirstOrDefaultAsync<(Guid? SchoolId, string Name)>(@"
SELECT school_id AS SchoolId, name AS Name
FROM roles
WHERE id = @RoleId AND is_deleted = FALSE
LIMIT 1;", new { RoleId = roleId });

        if (roleRow == default)
            return ApiResponse<object>.Fail("Role not found.", ErrorCodes.NotFound);
        if (roleRow.SchoolId == null)
            return ApiResponse<object>.Fail("System roles cannot be edited. Create a custom role instead.", ErrorCodes.Forbidden);
        if (roleRow.SchoolId.Value != schoolId)
            return ApiResponse<object>.Fail("Role not found.", ErrorCodes.NotFound);

        // Soft-delete current role perms
        await conn.ExecuteAsync(@"
UPDATE role_permissions
SET is_deleted = TRUE, updated_at = NOW(), updated_by = @UpdatedBy
WHERE role_id = @RoleId;", new { RoleId = roleId, UpdatedBy = updatedBy });

        if (permissions == null || permissions.Count == 0)
            return ApiResponse<object>.Ok(null, "Role permissions updated.");

        // Insert perms (only from catalog for this school or platform)
        foreach (var raw in permissions)
        {
            var name = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var permId = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT id
FROM permissions
WHERE is_deleted = FALSE
  AND name = @Name
  AND (school_id = @SchoolId OR school_id IS NULL)
ORDER BY school_id DESC NULLS LAST
LIMIT 1;", new { Name = name, SchoolId = schoolId });
            if (permId is null) continue;

            await conn.ExecuteAsync(@"
INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
VALUES (md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy, @RoleId, @PermissionId)
ON CONFLICT (role_id, permission_id) DO UPDATE
SET is_deleted = FALSE, updated_at = NOW(), updated_by = @CreatedBy;", new
            {
                SchoolId = schoolId,
                CreatedBy = updatedBy,
                RoleId = roleId,
                PermissionId = permId.Value
            });
        }

        return ApiResponse<object>.Ok(null, "Role permissions updated.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<AccessStaffUserDto>>> GetStaffUsersAsync(bool showInactive)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<AccessStaffUserDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;

        var rows = await conn.QueryAsync<AccessStaffUserDto>(@"
SELECT
    s.id AS StaffId,
    s.full_name AS FullName,
    NULLIF(TRIM(s.email), '') AS Email,
    COALESCE(s.is_teaching, FALSE) AS IsTeaching,
    u.id AS UserId,
    u.role_id AS RoleId,
    r.name AS RoleName
FROM staff s
LEFT JOIN users u
  ON u.school_id = s.school_id
 AND u.is_deleted = FALSE
 AND LOWER(TRIM(u.email)) = LOWER(TRIM(COALESCE(s.email,'')))
LEFT JOIN roles r
  ON r.id = u.role_id
 AND r.is_deleted = FALSE
WHERE s.school_id = @SchoolId
  AND s.is_deleted = FALSE
ORDER BY s.full_name;", new { SchoolId = schoolId });

        var list = rows.AsList();
        if (!showInactive)
        {
            // staff.is_deleted used as "inactive" here; keep shape.
        }

        return ApiResponse<IReadOnlyCollection<AccessStaffUserDto>>.Ok(list, "Staff users fetched.");
    }

    public async Task<ApiResponse<object>> AssignStaffRoleAsync(Guid staffId, Guid roleId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;

        // Ensure role is in this school or system role. Also protect against privilege escalation:
        // School admins must NOT be able to assign SuperAdmin/SchoolAdmin roles to other users.
        var roleRow = await conn.QueryFirstOrDefaultAsync<(Guid? SchoolId, string Name)>(@"
SELECT school_id AS SchoolId, name AS Name
FROM roles
WHERE id = @RoleId AND is_deleted = FALSE
LIMIT 1;", new { RoleId = roleId });

        if (roleRow == default)
            return ApiResponse<object>.Fail("Role not found.", ErrorCodes.NotFound);
        if (roleRow.SchoolId != null && roleRow.SchoolId.Value != schoolId)
            return ApiResponse<object>.Fail("Role not found.", ErrorCodes.NotFound);

        var currentRole = (_currentUser.Role ?? string.Empty).Trim();
        var isSuperAdmin = string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        if (!isSuperAdmin)
        {
            // Allowed assignments for SchoolAdmin: custom roles of this school, and the baseline system roles Teacher/Staff.
            // Disallow assigning SchoolAdmin/SuperAdmin/Student to prevent cross-privilege escalation.
            var name = (roleRow.Name ?? string.Empty).Trim();
            var isSystem = roleRow.SchoolId == null;
            if (isSystem)
            {
                if (!string.Equals(name, "Teacher", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "Staff", StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponse<object>.Fail("You cannot assign this system role.", ErrorCodes.Forbidden);
                }
            }
        }

        // Find linked login user for staff (email-based)
        var userId = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT u.id
FROM users u
INNER JOIN staff s
  ON s.school_id = u.school_id
 AND s.id = @StaffId
 AND s.is_deleted = FALSE
 AND LOWER(TRIM(u.email)) = LOWER(TRIM(COALESCE(s.email,'')))
WHERE u.school_id = @SchoolId
  AND u.is_deleted = FALSE
LIMIT 1;", new { StaffId = staffId, SchoolId = schoolId });

        if (userId is null)
            return ApiResponse<object>.Fail("No login user linked to this staff. Ensure staff has an email and a user exists.", ErrorCodes.BusinessRule);

        await conn.ExecuteAsync(@"
UPDATE users
SET role_id = @RoleId, updated_at = NOW(), updated_by = @UpdatedBy
WHERE id = @UserId;", new { UserId = userId.Value, RoleId = roleId, UpdatedBy = _currentUser.UserId ?? Guid.Empty });

        return ApiResponse<object>.Ok(null, "Role assigned.");
    }
}

