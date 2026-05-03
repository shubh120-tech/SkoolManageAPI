using System;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Auth.Dtos;
using SchoolManagement.Application.Auth.Services;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools.Services;
using SchoolManagement.Application.Staff.Dtos;
using SchoolManagement.Infrastructure.Security;
using SchoolManagement.Infrastructure.Logging;

namespace SchoolManagement.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserService _currentUser;
    private readonly ISchoolFeatureService _schoolFeatureService;

    public AuthService(
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        IAuditLogger auditLogger,
        ICurrentUserService currentUser,
        ISchoolFeatureService schoolFeatureService)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _schoolFeatureService = schoolFeatureService;
    }

    public async Task<ApiResponse<LoginResponseDto>> LoginAsync(LoginRequestDto request, string ipAddress)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_email", request.Email);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_auth_get_user_for_login(@p_email)", p);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ApiResponse<LoginResponseDto>.Fail("Invalid credentials.", ErrorCodes.Unauthorized);
        }

        var dto = System.Text.Json.JsonSerializer.Deserialize<AuthUserLoginDto>(json)!;

        // If a staff user is assigned a custom role (e.g. Accountant), we still want them to behave as
        // Teacher/Staff in the UI routing. Determine "user type" from staff table instead of role name.
        if (dto.SchoolId.HasValue
            && !string.Equals(dto.RoleName, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dto.RoleName, "SchoolAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dto.RoleName, "Student", StringComparison.OrdinalIgnoreCase))
        {
            var email = await ResolveUserEmailAsync(conn, dto.UserId);
            var staffRole = await ResolveStaffRoleNameAsync(conn, dto.SchoolId.Value, email);
            if (!string.IsNullOrWhiteSpace(staffRole))
                dto.RoleName = staffRole;
        }

        if (!_passwordHasher.Verify(request.Password, dto.PasswordHash))
        {
            var pFail = new DynamicParameters();
            pFail.Add("p_user_id", dto.UserId);
            await conn.ExecuteAsync("CALL sp_auth_register_failed_login(@p_user_id)", pFail);
            return ApiResponse<LoginResponseDto>.Fail("Invalid credentials.", ErrorCodes.Unauthorized);
        }

        if (dto.IsLocked || dto.IsDeletedUser || !dto.SchoolIsActive || dto.LicenseExpired)
        {
            return ApiResponse<LoginResponseDto>.Fail("Account is restricted.", ErrorCodes.Forbidden);
        }

        var pSuccess = new DynamicParameters();
        pSuccess.Add("p_user_id", dto.UserId);
        await conn.ExecuteAsync("CALL sp_auth_register_successful_login(@p_user_id)", pSuccess);

        var tokenResult = await _jwtService.GenerateTokensAsync(
            dto.UserId,
            dto.SchoolId,
            dto.RoleName,
            dto.SchoolCode,
            dto.Permissions);

        await _auditLogger.LogAsync(dto.UserId, dto.SchoolId, "LOGIN", "User", dto.UserId,
            AuditLogger.SerializeDetails(new { ipAddress }));

        var response = new LoginResponseDto
        {
            UserId = dto.UserId,
            SchoolId = dto.SchoolId,
            Role = dto.RoleName,
            SchoolCode = dto.SchoolCode,
            Permissions = dto.Permissions,
            AccessToken = tokenResult.AccessToken,
            AccessTokenExpiresAt = tokenResult.AccessTokenExpiresAt,
            RefreshToken = tokenResult.RefreshToken,
            RefreshTokenExpiresAt = tokenResult.RefreshTokenExpiresAt,
            MustChangePassword = dto.MustChangePassword
        };

        return ApiResponse<LoginResponseDto>.Ok(response);
    }

    public async Task<ApiResponse<LoginResponseDto>> RefreshAsync(RefreshTokenRequestDto request, string ipAddress)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var hash = JwtService.HashToken(request.RefreshToken);
        var p = new DynamicParameters();
        p.Add("p_refresh_token_hash", hash);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_auth_rotate_refresh_token(@p_refresh_token_hash)", p);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ApiResponse<LoginResponseDto>.Fail("Invalid or expired refresh token.", ErrorCodes.Unauthorized);
        }

        var dto = System.Text.Json.JsonSerializer.Deserialize<AuthUserLoginDto>(json)!;

        if (dto.SchoolId.HasValue
            && !string.Equals(dto.RoleName, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dto.RoleName, "SchoolAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dto.RoleName, "Student", StringComparison.OrdinalIgnoreCase))
        {
            var email = await ResolveUserEmailAsync(conn, dto.UserId);
            var staffRole = await ResolveStaffRoleNameAsync(conn, dto.SchoolId.Value, email);
            if (!string.IsNullOrWhiteSpace(staffRole))
                dto.RoleName = staffRole;
        }

        var tokenResult = await _jwtService.GenerateTokensAsync(
            dto.UserId,
            dto.SchoolId,
            dto.RoleName,
            dto.SchoolCode,
            dto.Permissions);

        await _auditLogger.LogAsync(dto.UserId, dto.SchoolId, "REFRESH_TOKEN", "User", dto.UserId,
            AuditLogger.SerializeDetails(new { ipAddress }));

        var response = new LoginResponseDto
        {
            UserId = dto.UserId,
            SchoolId = dto.SchoolId,
            Role = dto.RoleName,
            SchoolCode = dto.SchoolCode,
            Permissions = dto.Permissions,
            AccessToken = tokenResult.AccessToken,
            AccessTokenExpiresAt = tokenResult.AccessTokenExpiresAt,
            RefreshToken = tokenResult.RefreshToken,
            RefreshTokenExpiresAt = tokenResult.RefreshTokenExpiresAt,
            MustChangePassword = dto.MustChangePassword
        };

        return ApiResponse<LoginResponseDto>.Ok(response);
    }

    private static async Task<string?> ResolveStaffRoleNameAsync(System.Data.IDbConnection conn, Guid schoolId, string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var isTeaching = await conn.ExecuteScalarAsync<bool?>(@"
SELECT COALESCE(is_teaching, FALSE)
FROM staff
WHERE school_id = @SchoolId
  AND is_deleted = FALSE
  AND LOWER(TRIM(email)) = LOWER(TRIM(@Email))
LIMIT 1;", new { SchoolId = schoolId, Email = email });

        if (isTeaching is null) return null;
        return isTeaching.Value ? "Teacher" : "Staff";
    }

    private static async Task<string?> ResolveUserEmailAsync(System.Data.IDbConnection conn, Guid userId)
    {
        return await conn.ExecuteScalarAsync<string?>(@"
SELECT email
FROM users
WHERE id = @UserId AND is_deleted = FALSE
LIMIT 1;", new { UserId = userId });
    }

    public async Task<ApiResponse<object>> LogoutAsync(string refreshToken, string ipAddress)
    {
        var hash = JwtService.HashToken(refreshToken);
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_refresh_token_hash", hash);
        await conn.ExecuteAsync("CALL sp_auth_revoke_refresh_token(@p_refresh_token_hash)", p);

        await _auditLogger.LogAsync(_currentUser.UserId, _currentUser.SchoolId, "LOGOUT", "User",
            _currentUser.UserId, AuditLogger.SerializeDetails(new { ipAddress }));

        return ApiResponse<object>.Ok(null, "Logged out.");
    }

    public async Task<ApiResponse<object>> ChangePasswordAsync(ChangePasswordRequestDto request)
    {
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("Unauthorized.", ErrorCodes.Unauthorized);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var pGet = new DynamicParameters();
        pGet.Add("p_user_id", _currentUser.UserId);
        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_auth_get_user_password_hash(@p_user_id)", pGet);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<object>.Fail("User not found.", ErrorCodes.NotFound);

        var dto = System.Text.Json.JsonSerializer.Deserialize<AuthUserPasswordDto>(json)!;
        if (!_passwordHasher.Verify(request.CurrentPassword, dto.PasswordHash))
            return ApiResponse<object>.Fail("Invalid current password.", ErrorCodes.ValidationError);

        var newHash = _passwordHasher.Hash(request.NewPassword);
        var pChange = new DynamicParameters();
        pChange.Add("p_user_id", _currentUser.UserId);
        pChange.Add("p_new_password_hash", newHash);
        await conn.ExecuteAsync("CALL sp_auth_change_password(@p_user_id,@p_new_password_hash)", pChange);

        await _auditLogger.LogAsync(_currentUser.UserId, _currentUser.SchoolId, "CHANGE_PASSWORD", "User",
            _currentUser.UserId, null);

        return ApiResponse<object>.Ok(null, "Password changed.");
    }

    public async Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequestDto request)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_email", request.Email);
        await conn.ExecuteAsync("CALL sp_auth_generate_reset_password_token(@p_email)", p);
        return ApiResponse<object>.Ok(null, "If the account exists, a reset link has been sent.");
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequestDto request)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var newHash = _passwordHasher.Hash(request.NewPassword);
        var p = new DynamicParameters();
        p.Add("p_user_id", request.UserId);
        p.Add("p_token", request.Token);
        p.Add("p_new_password_hash", newHash);
        var rows = await conn.ExecuteAsync("CALL sp_auth_reset_password(@p_user_id,@p_token,@p_new_password_hash)", p);
        if (rows == 0)
            return ApiResponse<object>.Fail("Invalid reset token.", ErrorCodes.ValidationError);

        return ApiResponse<object>.Ok(null, "Password reset successful.");
    }

    public async Task<ApiResponse<CurrentProfileDto>> GetCurrentProfileAsync()
    {
        if (_currentUser.UserId is null)
            return ApiResponse<CurrentProfileDto>.Fail("Unauthorized.", ErrorCodes.Unauthorized);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        // Load base user + role + school info
        const string userSql = @"
SELECT
    u.id AS UserId,
    u.school_id AS SchoolId,
    u.email AS Email,
    u.full_name AS FullName,
    r.name AS Role,
    s.code AS SchoolCode,
    s.name AS SchoolName
FROM users u
LEFT JOIN roles r ON r.id = u.role_id
LEFT JOIN schools s ON s.id = u.school_id
WHERE u.id = @UserId
  AND u.is_deleted = FALSE
LIMIT 1;";

        var userRow = await conn.QueryFirstOrDefaultAsync<CurrentProfileDto>(
            userSql,
            new { UserId = _currentUser.UserId });

        if (userRow is null)
            return ApiResponse<CurrentProfileDto>.Fail("User not found.", ErrorCodes.NotFound);

        var roleName = userRow.Role ?? string.Empty;

        // If staff user has a custom access role, normalize role for routing (Teacher/Staff).
        if (userRow.SchoolId.HasValue
            && !string.Equals(roleName, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(roleName, "SchoolAdmin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(roleName, "Student", StringComparison.OrdinalIgnoreCase))
        {
            var staffRole = await ResolveStaffRoleNameAsync(conn, userRow.SchoolId.Value, userRow.Email);
            if (!string.IsNullOrWhiteSpace(staffRole))
            {
                roleName = staffRole;
                userRow.Role = staffRole;
            }
        }

        // Default entity type based on role
        if (string.Equals(roleName, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            userRow.EntityType = "SuperAdmin";
        }
        else if (string.Equals(roleName, "SchoolAdmin", StringComparison.OrdinalIgnoreCase))
        {
            userRow.EntityType = "SchoolAdmin";
        }
        else if (string.Equals(roleName, "Teacher", StringComparison.OrdinalIgnoreCase))
        {
            userRow.EntityType = "Staff";
        }
        else if (string.Equals(roleName, "Student", StringComparison.OrdinalIgnoreCase))
        {
            userRow.EntityType = "Student";
        }

        // Enrich with staff details for teachers
        if (string.Equals(userRow.EntityType, "Staff", StringComparison.OrdinalIgnoreCase)
            && userRow.SchoolId.HasValue)
        {
            const string staffSql = @"
SELECT
    id AS StaffId,
    staff_code AS StaffCode,
    mobile_no AS StaffMobileNo
FROM staff
WHERE school_id = @SchoolId
  AND LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";

            var staffRow = await conn.QueryFirstOrDefaultAsync<CurrentProfileDto>(
                staffSql,
                new { SchoolId = userRow.SchoolId, Email = userRow.Email });

            if (staffRow is not null)
            {
                userRow.StaffId = staffRow.StaffId;
                userRow.StaffCode = staffRow.StaffCode;
                userRow.StaffMobileNo = staffRow.StaffMobileNo;

                const string assignmentsSql = @"
SELECT
    ct.class_id AS ClassId,
    ct.section_id AS SectionId,
    ct.academic_session_id AS AcademicSessionId
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.teacher_id = @TeacherId
  AND ct.is_deleted = FALSE
ORDER BY ct.created_at DESC;";
                var assignments = (await conn.QueryAsync<CurrentClassTeacherAssignmentDto>(
                    assignmentsSql,
                    new { SchoolId = userRow.SchoolId, TeacherId = staffRow.StaffId }))
                    .AsList();
                userRow.ClassTeacherAssignments = assignments;

                var assignmentJson = await conn.ExecuteScalarAsync<string>(
                    "SELECT fn_class_teacher_get_by_teacher(@p_school_id,@p_teacher_id)",
                    new { p_school_id = userRow.SchoolId, p_teacher_id = staffRow.StaffId });
                if (!string.IsNullOrWhiteSpace(assignmentJson) && assignmentJson != "null")
                {
                    try
                    {
                        var assignment = System.Text.Json.JsonSerializer.Deserialize<ClassTeacherAssignmentDto>(assignmentJson,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (assignment != null)
                        {
                            userRow.ClassTeacherClassId = assignment.ClassId;
                            userRow.ClassTeacherSectionId = assignment.SectionId;
                            userRow.ClassTeacherAcademicSessionId = assignment.AcademicSessionId;
                        }
                    }
                    catch { /* ignore */ }
                }
                else if (assignments.Count > 0)
                {
                    // Backward compatibility for UIs still using single-assignment fields.
                    userRow.ClassTeacherClassId = assignments[0].ClassId;
                    userRow.ClassTeacherSectionId = assignments[0].SectionId;
                    userRow.ClassTeacherAcademicSessionId = assignments[0].AcademicSessionId;
                }

                userRow.HasClassTeacherAssignment = assignments.Count > 0;
                userRow.HasTimetableSlots = await conn.ExecuteScalarAsync<bool>(
                    @"
SELECT EXISTS (
    SELECT 1
    FROM timetables t
    WHERE t.school_id = @SchoolId
      AND t.teacher_id = @TeacherId
      AND t.is_deleted = FALSE
    LIMIT 1
);",
                    new { SchoolId = userRow.SchoolId, TeacherId = staffRow.StaffId });
            }
        }

        // Enrich with student details for students
        if (string.Equals(userRow.EntityType, "Student", StringComparison.OrdinalIgnoreCase)
            && userRow.SchoolId.HasValue)
        {
            const string studentSql = @"
SELECT
    id AS StudentId,
    admission_no AS AdmissionNo,
    parent_mobile_no AS ParentMobileNo
FROM students
WHERE school_id = @SchoolId
  AND LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";

            var studentRow = await conn.QueryFirstOrDefaultAsync<CurrentProfileDto>(
                studentSql,
                new { SchoolId = userRow.SchoolId, Email = userRow.Email });

            if (studentRow is not null)
            {
                userRow.StudentId = studentRow.StudentId;
                userRow.AdmissionNo = studentRow.AdmissionNo;
                userRow.ParentMobileNo = studentRow.ParentMobileNo;
            }
        }

        if (userRow.SchoolId.HasValue)
        {
            var eff = await _schoolFeatureService.GetEffectiveFeaturesAsync(userRow.SchoolId.Value);
            userRow.SchoolFeatures = eff.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        return ApiResponse<CurrentProfileDto>.Ok(userRow, "Profile loaded.");
    }

    public async Task<ApiResponse<object>> UpdateProfileAsync(UpdateProfileRequestDto request)
    {
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("Unauthorized.", ErrorCodes.Unauthorized);

        if (string.IsNullOrWhiteSpace(request.FullName))
            return ApiResponse<object>.Fail("Full name is required.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_user_id", _currentUser.UserId);
        p.Add("p_full_name", request.FullName.Trim());

        const string sql = @"
UPDATE users
SET full_name = @p_full_name,
    updated_at = NOW(),
    updated_by = @p_user_id
WHERE id = @p_user_id
  AND is_deleted = FALSE";

        var affected = await conn.ExecuteAsync(sql, p);
        if (affected == 0)
            return ApiResponse<object>.Fail("User not found.", ErrorCodes.NotFound);

        await _auditLogger.LogAsync(_currentUser.UserId, _currentUser.SchoolId, "UPDATE_PROFILE", "User",
            _currentUser.UserId, AuditLogger.SerializeDetails(new { request.FullName }));

        return ApiResponse<object>.Ok(null, "Profile updated.");
    }

    private sealed class AuthUserLoginDto
    {
        public Guid UserId { get; set; }
        public Guid? SchoolId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string? SchoolCode { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
        public bool MustChangePassword { get; set; }
        public bool IsDeletedUser { get; set; }
        public bool SchoolIsActive { get; set; }
        public bool LicenseExpired { get; set; }
        public string[] Permissions { get; set; } = Array.Empty<string>();
    }

    private sealed class AuthUserPasswordDto
    {
        public string PasswordHash { get; set; } = string.Empty;
    }
}

