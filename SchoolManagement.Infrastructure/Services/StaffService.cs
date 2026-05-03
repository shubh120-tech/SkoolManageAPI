using System;
using System.Net;
using System.Threading.Tasks;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Academic.Services;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;
using SchoolManagement.Application.Staff.Services;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Services;

public class StaffService : IStaffService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IAcademicService _academicService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<StaffService> _logger;
    private readonly IOptions<EmailOptions> _emailOptions;

    public StaffService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        IAcademicService academicService,
        IPasswordHasher passwordHasher,
        IEmailSender emailSender,
        ILogger<StaffService> logger,
        IOptions<EmailOptions> emailOptions)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _academicService = academicService;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _logger = logger;
        _emailOptions = emailOptions;
    }

    public async Task<ApiResponse<StaffResponseDto>> CreateStaffAsync(CreateStaffRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StaffResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var schoolId = _tenantContext.SchoolId.Value;
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            // Enforce global uniqueness: a staff/user email cannot exist in another school.
            var otherStaffSchool = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT school_id
FROM staff
WHERE is_deleted = FALSE
  AND email IS NOT NULL
  AND LOWER(email) = LOWER(@Email)
  AND school_id <> @SchoolId
LIMIT 1;", new { Email = email, SchoolId = schoolId });
            if (otherStaffSchool != null)
                return ApiResponse<StaffResponseDto>.Fail("This email is already registered in another school. Please use a different email.", ErrorCodes.Conflict);

            var otherUserSchool = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT school_id
FROM users
WHERE is_deleted = FALSE
  AND LOWER(email) = LOWER(@Email)
  AND school_id IS NOT NULL
  AND school_id <> @SchoolId
LIMIT 1;", new { Email = email, SchoolId = schoolId });
            if (otherUserSchool != null)
                return ApiResponse<StaffResponseDto>.Fail("This email is already registered in another school. Please use a different email.", ErrorCodes.Conflict);
        }

        var p = new DynamicParameters();
        var generatedCode = "STF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_staff_code", generatedCode);
        p.Add("p_full_name", request.FullName);
        p.Add("p_email", request.Email);
        p.Add("p_date_of_birth", request.DateOfBirth);
        p.Add("p_mobile_no", request.MobileNo);
        p.Add("p_is_teaching", request.IsTeaching);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);
        p.Add("p_address_line", request.AddressLine);
        p.Add("p_date_of_joining", request.DateOfJoining);

        var row = await conn.QueryFirstOrDefaultAsync<StaffCreateOutput>(
            "SELECT * FROM fn_staff_create(@p_school_id,@p_staff_code,@p_full_name,@p_email,@p_date_of_birth,@p_mobile_no,@p_is_teaching,@p_created_by,@p_address_line,@p_date_of_joining)", p);

        if (row == null)
            return ApiResponse<StaffResponseDto>.Fail("Staff create did not return output.", ErrorCodes.InternalError);

        var createdId = row.O_staff_id;
        var staffCode = row.O_staff_code ?? generatedCode;

        var newStaffLoginCreated = false;

        // Create login user for staff so they can sign in with email and mobile number as initial password.
        if (!string.IsNullOrWhiteSpace(request.Email) && !string.IsNullOrWhiteSpace(request.MobileNo))
        {
            // Find or create a default Teacher/Staff role for this school.
            var defaultRoleName = request.IsTeaching ? "Teacher" : "Staff";
            const string getRoleSql = @"
SELECT id
FROM roles
WHERE school_id = @SchoolId AND name = @RoleName AND is_deleted = FALSE
UNION
SELECT id
FROM roles
WHERE school_id IS NULL AND name = @RoleName AND is_deleted = FALSE
LIMIT 1;";

            var createdByUserId = _currentUser.UserId ?? Guid.Empty;

            var roleId = await conn.ExecuteScalarAsync<Guid?>(getRoleSql, new { SchoolId = schoolId, RoleName = defaultRoleName });

            if (roleId is null)
            {
                const string insertRoleSql = @"
INSERT INTO roles(
    id, school_id, is_deleted, created_at, created_by,
    name, description)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy,
    @RoleName, @RoleDescription)
RETURNING id;";

                roleId = await conn.ExecuteScalarAsync<Guid>(
                    insertRoleSql,
                    new
                    {
                        SchoolId = schoolId,
                        CreatedBy = createdByUserId,
                        RoleName = defaultRoleName,
                        RoleDescription = request.IsTeaching ? "Default teacher role" : "Default staff role"
                    });
            }

            const string existingUserSql = @"
SELECT id
FROM users
WHERE LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";

            var existingUserId = await conn.ExecuteScalarAsync<Guid?>(
                existingUserSql,
                new { SchoolId = schoolId, Email = request.Email });

            if (existingUserId is not null)
            {
                // If that user belongs to another school, block creation.
                var existingUserSchool = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT school_id
FROM users
WHERE id = @Id
LIMIT 1;", new { Id = existingUserId.Value });
                if (existingUserSchool != null && existingUserSchool.Value != schoolId)
                    return ApiResponse<StaffResponseDto>.Fail("This email is already registered in another school. Please use a different email.", ErrorCodes.Conflict);
            }

            if (existingUserId is null)
            {
                var passwordHash = _passwordHasher.Hash(request.MobileNo!);

                const string insertUserSql = @"
INSERT INTO users(
    id, school_id, is_deleted, created_at, created_by,
    email, password_hash, full_name, role_id,
    is_locked, failed_login_attempts, must_change_password, is_soft_deleted_user)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy,
    @Email, @PasswordHash, @FullName, @RoleId,
    FALSE, 0, TRUE, FALSE);";

                await conn.ExecuteAsync(
                    insertUserSql,
                    new
                    {
                        SchoolId = schoolId,
                        CreatedBy = createdByUserId,
                        Email = request.Email!.Trim(),
                        PasswordHash = passwordHash,
                        FullName = request.FullName,
                        RoleId = roleId.Value
                    });
                newStaffLoginCreated = true;
            }
        }

        // Create salary structure if provided
        if (request.Basic.HasValue && request.Allowances.HasValue && request.Deductions.HasValue)
        {
            const string salarySql = @"
INSERT INTO salary_structures (
    id, school_id, is_deleted, created_at, created_by,
    staff_id, basic, allowances, deductions)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy,
    @StaffId, @Basic, @Allowances, @Deductions
);";

            await conn.ExecuteAsync(salarySql, new
            {
                SchoolId = _tenantContext.SchoolId,
                CreatedBy = _currentUser.UserId ?? Guid.Empty,
                StaffId = createdId,
                Basic = request.Basic.Value,
                Allowances = request.Allowances.Value,
                Deductions = request.Deductions.Value
            });
        }

        // Create bank details if provided
        if (!string.IsNullOrWhiteSpace(request.BankAccountNo)
            && !string.IsNullOrWhiteSpace(request.BankIfsc)
            && !string.IsNullOrWhiteSpace(request.BankName))
        {
            const string bankSql = @"
INSERT INTO staff_bank_details (
    id, school_id, is_deleted, created_at, created_by,
    staff_id, account_holder_name, account_no, ifsc_code, bank_name)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy,
    @StaffId, @AccountHolderName, @AccountNo, @Ifsc, @BankName
)
ON CONFLICT (school_id, staff_id) DO UPDATE
SET account_holder_name = EXCLUDED.account_holder_name,
    account_no = EXCLUDED.account_no,
    ifsc_code = EXCLUDED.ifsc_code,
    bank_name = EXCLUDED.bank_name,
    updated_at = NOW(),
    updated_by = EXCLUDED.created_by;";

            await conn.ExecuteAsync(bankSql, new
            {
                SchoolId = _tenantContext.SchoolId,
                CreatedBy = _currentUser.UserId ?? Guid.Empty,
                StaffId = createdId,
                AccountHolderName = string.IsNullOrWhiteSpace(request.BankAccountHolderName)
                    ? request.FullName
                    : request.BankAccountHolderName.Trim(),
                AccountNo = request.BankAccountNo!.Trim(),
                Ifsc = request.BankIfsc!.Trim(),
                BankName = request.BankName!.Trim()
            });
        }

        if (request.IsTeaching && request.SubjectIds is { Length: > 0 })
        {
            var setResult = await SetStaffSubjectsAsync(createdId, request.SubjectIds);
            if (!setResult.Success) return ApiResponse<StaffResponseDto>.Fail(setResult.Message ?? "Failed to set subjects.", setResult.ErrorCode ?? ErrorCodes.BusinessRule);
        }
        if (request.IsTeaching && request.ClassId.HasValue && request.SectionId.HasValue && request.AcademicSessionId.HasValue)
        {
            var assignResult = await _academicService.AssignClassTeacherAsync(createdId, request.ClassId.Value, request.SectionId.Value, request.AcademicSessionId.Value);
            if (!assignResult.Success) return ApiResponse<StaffResponseDto>.Fail(assignResult.Message ?? "Failed to assign class teacher.", assignResult.ErrorCode ?? ErrorCodes.BusinessRule);
        }

        var dto = new StaffResponseDto
        {
            Id = createdId,
            StaffCode = staffCode,
            FullName = request.FullName,
            Email = request.Email,
            DateOfBirth = request.DateOfBirth,
            MobileNo = request.MobileNo,
            AddressLine = request.AddressLine,
            DateOfJoining = request.DateOfJoining,
            IsTeaching = request.IsTeaching
        };

        var loginSetupAttempted =
            !string.IsNullOrWhiteSpace(request.Email) && !string.IsNullOrWhiteSpace(request.MobileNo);

        if (request.SendWelcomeEmail && !string.IsNullOrWhiteSpace(request.Email))
            await TrySendStaffWelcomeEmailAsync(request, newStaffLoginCreated, loginSetupAttempted);
        else if (!request.SendWelcomeEmail && !string.IsNullOrWhiteSpace(request.Email))
            _logger.LogInformation("Welcome email skipped (SendWelcomeEmail=false) for {Email}", request.Email!.Trim());

        return ApiResponse<StaffResponseDto>.Ok(dto, "Staff created.");
    }

    /// <summary>Best-effort welcome email; staff creation must not fail if SMTP is unavailable.</summary>
    private async Task TrySendStaffWelcomeEmailAsync(
        CreateStaffRequestDto request,
        bool newLoginCreated,
        bool loginSetupAttempted)
    {
        var email = request.Email!.Trim();
        var opts = _emailOptions.Value;
        if (!opts.Enabled)
        {
            _logger.LogWarning(
                "Welcome email not sent to {Email}: Email:Enabled is false. Turn it on in appsettings.json or set ASPNETCORE_ENVIRONMENT=Development (appsettings.Development.json enables it) and configure SMTP.",
                email);
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.FromEmail))
        {
            _logger.LogWarning(
                "Welcome email not sent to {Email}: Email:FromEmail is empty. Set it in appsettings or user secrets.",
                email);
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.Host))
        {
            _logger.LogWarning("Welcome email not sent to {Email}: Email:Host is empty.", email);
            return;
        }

        try
        {
            var subject = "Welcome to your school portal";
            string body;
            var name = WebUtility.HtmlEncode(request.FullName);
            var emailEnc = WebUtility.HtmlEncode(email);

            if (newLoginCreated && !string.IsNullOrWhiteSpace(request.MobileNo))
            {
                var mobileEnc = WebUtility.HtmlEncode(request.MobileNo.Trim());
                body =
                    $"<p>Hello {name},</p>" +
                    "<p>Your staff account has been created. You can sign in to the school portal using:</p>" +
                    "<ul>" +
                    $"<li><strong>Email:</strong> {emailEnc}</li>" +
                    $"<li><strong>Temporary password:</strong> your registered mobile number ({mobileEnc})</li>" +
                    "</ul>" +
                    "<p>You will be asked to change your password after you first sign in.</p>" +
                    "<p>If you did not expect this message, please contact your school administrator.</p>";
            }
            else if (newLoginCreated)
            {
                body =
                    $"<p>Hello {name},</p>" +
                    "<p>Your staff account has been created.</p>" +
                    $"<p><strong>Sign-in email:</strong> {emailEnc}</p>" +
                    "<p>Contact your school administrator for your initial password, or use &quot;Forgot password&quot; on the login page if applicable.</p>";
            }
            else if (loginSetupAttempted)
            {
                body =
                    $"<p>Hello {name},</p>" +
                    "<p>You have been added as staff in the school management system.</p>" +
                    $"<p>A user account already exists for <strong>{emailEnc}</strong>. Sign in with your existing password, or use &quot;Forgot password&quot; if needed.</p>";
            }
            else
            {
                body =
                    $"<p>Hello {name},</p>" +
                    "<p>You have been added as staff in the school management system.</p>" +
                    "<p>Portal login is created when both email and mobile number are on file. Please contact your school administrator if you need access.</p>";
            }

            await _emailSender.SendAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Welcome email could not be sent to {Email}", email);
        }
    }

        public async Task<ApiResponse<StaffResponseDto>> UpdateStaffAsync(Guid staffId, UpdateStaffRequestDto request)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<StaffResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var p = new DynamicParameters();
            p.Add("p_school_id", _tenantContext.SchoolId);
            p.Add("p_staff_id", staffId);
            p.Add("p_staff_code", request.StaffCode);
            p.Add("p_full_name", request.FullName);
            p.Add("p_email", request.Email);
            p.Add("p_date_of_birth", request.DateOfBirth);
            p.Add("p_mobile_no", request.MobileNo);
            p.Add("p_is_teaching", request.IsTeaching);
            p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);
            p.Add("p_address_line", request.AddressLine);
            p.Add("p_date_of_joining", request.DateOfJoining);

            await conn.ExecuteAsync("CALL sp_staff_update(@p_school_id,@p_staff_id,@p_staff_code,@p_full_name,@p_email,@p_date_of_birth,@p_mobile_no,@p_is_teaching,@p_user_id,@p_address_line,@p_date_of_joining)", p);

            // Update salary structure if provided
            if (request.Basic.HasValue && request.Allowances.HasValue && request.Deductions.HasValue)
            {
                const string salarySql = @"
UPDATE salary_structures
SET basic = @Basic,
    allowances = @Allowances,
    deductions = @Deductions,
    updated_at = NOW(),
    updated_by = @UpdatedBy
WHERE school_id = @SchoolId
  AND staff_id = @StaffId
  AND is_deleted = FALSE;

INSERT INTO salary_structures (
    id, school_id, is_deleted, created_at, created_by,
    staff_id, basic, allowances, deductions)
SELECT
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @UpdatedBy,
    @StaffId, @Basic, @Allowances, @Deductions
WHERE NOT EXISTS (
    SELECT 1 FROM salary_structures
    WHERE school_id = @SchoolId
      AND staff_id = @StaffId
      AND is_deleted = FALSE
);";

                await conn.ExecuteAsync(salarySql, new
                {
                    SchoolId = _tenantContext.SchoolId,
                    StaffId = staffId,
                    Basic = request.Basic.Value,
                    Allowances = request.Allowances.Value,
                    Deductions = request.Deductions.Value,
                    UpdatedBy = _currentUser.UserId ?? Guid.Empty
                });
            }

            // Update bank details if provided
            if (!string.IsNullOrWhiteSpace(request.BankAccountNo)
                && !string.IsNullOrWhiteSpace(request.BankIfsc)
                && !string.IsNullOrWhiteSpace(request.BankName))
            {
                const string bankSql = @"
INSERT INTO staff_bank_details (
    id, school_id, is_deleted, created_at, created_by,
    staff_id, account_holder_name, account_no, ifsc_code, bank_name)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @UserId,
    @StaffId, @AccountHolderName, @AccountNo, @Ifsc, @BankName
)
ON CONFLICT (school_id, staff_id) DO UPDATE
SET account_holder_name = EXCLUDED.account_holder_name,
    account_no = EXCLUDED.account_no,
    ifsc_code = EXCLUDED.ifsc_code,
    bank_name = EXCLUDED.bank_name,
    updated_at = NOW(),
    updated_by = EXCLUDED.created_by;";

                await conn.ExecuteAsync(bankSql, new
                {
                    SchoolId = _tenantContext.SchoolId,
                    StaffId = staffId,
                    UserId = _currentUser.UserId ?? Guid.Empty,
                    AccountHolderName = string.IsNullOrWhiteSpace(request.BankAccountHolderName)
                        ? request.FullName
                        : request.BankAccountHolderName.Trim(),
                    AccountNo = request.BankAccountNo!.Trim(),
                    Ifsc = request.BankIfsc!.Trim(),
                    BankName = request.BankName!.Trim()
                });
            }

            if (request.IsTeaching && request.SubjectIds != null)
            {
                var setResult = await SetStaffSubjectsAsync(staffId, request.SubjectIds);
                if (!setResult.Success) return ApiResponse<StaffResponseDto>.Fail(setResult.Message ?? "Failed to set subjects.", setResult.ErrorCode ?? ErrorCodes.BusinessRule);
            }
            if (request.IsTeaching && request.ClassId.HasValue && request.SectionId.HasValue && request.AcademicSessionId.HasValue)
            {
                var assignResult = await _academicService.AssignClassTeacherAsync(staffId, request.ClassId.Value, request.SectionId.Value, request.AcademicSessionId.Value);
                if (!assignResult.Success)
                    return ApiResponse<StaffResponseDto>.Fail(assignResult.Message ?? "Failed to assign class teacher.", assignResult.ErrorCode ?? ErrorCodes.BusinessRule);
            }

            var dto = new StaffResponseDto
            {
                Id = staffId,
                StaffCode = request.StaffCode,
                FullName = request.FullName,
                Email = request.Email,
                DateOfBirth = request.DateOfBirth,
                MobileNo = request.MobileNo,
                AddressLine = request.AddressLine,
                DateOfJoining = request.DateOfJoining,
                IsTeaching = request.IsTeaching
            };

            return ApiResponse<StaffResponseDto>.Ok(dto, "Staff updated.");
        }

        public async Task<ApiResponse<IReadOnlyCollection<StaffResponseDto>>> GetStaffAsync(bool showInactive)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<IReadOnlyCollection<StaffResponseDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();

            var json = await conn.ExecuteScalarAsync<string>(
                "SELECT fn_staff_get_all(@p_school_id,@p_include_deleted)",
                new
                {
                    p_school_id = _tenantContext.SchoolId,
                    p_include_deleted = showInactive
                });

            var items = string.IsNullOrWhiteSpace(json)
                ? Array.Empty<StaffResponseDto>()
                : JsonSerializer.Deserialize<IReadOnlyCollection<StaffResponseDto>>(json) ?? Array.Empty<StaffResponseDto>();

            return ApiResponse<IReadOnlyCollection<StaffResponseDto>>.Ok(items, "Staff fetched.");
        }

        public async Task<ApiResponse<StaffResponseDto>> GetStaffByIdAsync(Guid staffId, bool showInactive = false)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<StaffResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();

            const string sql = @"
SELECT
    s.id AS Id,
    s.staff_code AS StaffCode,
    s.full_name AS FullName,
    s.email AS Email,
    s.date_of_birth AS DateOfBirth,
    s.mobile_no AS MobileNo,
    s.address_line AS AddressLine,
    s.date_of_joining AS DateOfJoining,
    s.is_teaching AS IsTeaching
FROM staff s
WHERE s.school_id = @SchoolId
  AND s.id = @StaffId
  AND (@IncludeDeleted OR s.is_deleted = FALSE)
LIMIT 1;";

            var dto = await conn.QuerySingleOrDefaultAsync<StaffResponseDto>(sql, new
            {
                SchoolId = _tenantContext.SchoolId,
                StaffId = staffId,
                IncludeDeleted = showInactive
            });

            if (dto == null)
                return ApiResponse<StaffResponseDto>.Fail("Staff not found.", ErrorCodes.NotFound);

            return ApiResponse<StaffResponseDto>.Ok(dto, "Staff fetched.");
        }

        public async Task<ApiResponse<object>> SoftDeleteStaffAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var p = new DynamicParameters();
            p.Add("p_school_id", _tenantContext.SchoolId);
            p.Add("p_staff_id", staffId);
            p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

            await conn.ExecuteAsync("CALL sp_staff_soft_delete(@p_school_id,@p_staff_id,@p_user_id)", p);

            return ApiResponse<object>.Ok(null, "Staff deleted.");
        }

        public async Task<ApiResponse<object>> SetStaffSubjectsAsync(Guid staffId, Guid[] subjectIds)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var p = new DynamicParameters();
            p.Add("p_school_id", _tenantContext.SchoolId);
            p.Add("p_staff_id", staffId);
            p.Add("p_subject_ids", subjectIds);
            p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

            await conn.ExecuteAsync("CALL sp_staff_set_subjects(@p_school_id,@p_staff_id,@p_subject_ids,@p_user_id)", p);

            return ApiResponse<object>.Ok(null, "Staff subjects updated.");
        }

        public async Task<ApiResponse<object>> GetStaffSubjectsAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var p = new DynamicParameters();
            p.Add("p_school_id", _tenantContext.SchoolId);
            p.Add("p_staff_id", staffId);

            var json = await conn.ExecuteScalarAsync<string>(
                "SELECT fn_staff_get_subjects(@p_school_id,@p_staff_id)",
                p);

            var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<object>(json);

            return ApiResponse<object>.Ok(data, "Staff subjects fetched.");
        }

        public async Task<ApiResponse<StaffBankDetailsDto?>> GetStaffBankDetailsAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<StaffBankDetailsDto?>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
SELECT
    account_holder_name AS AccountHolderName,
    account_no AS AccountNo,
    ifsc_code AS IfscCode,
    bank_name AS BankName
FROM staff_bank_details
WHERE school_id = @SchoolId
  AND staff_id = @StaffId
  AND is_deleted = FALSE;";

            var dto = await conn.QuerySingleOrDefaultAsync<StaffBankDetailsDto>(sql, new
            {
                SchoolId = _tenantContext.SchoolId,
                StaffId = staffId
            });

            return ApiResponse<StaffBankDetailsDto?>.Ok(dto, dto == null ? "Bank details not found." : "Bank details fetched.");
        }

        public async Task<ApiResponse<StaffSalaryStructureDto?>> GetStaffSalaryStructureAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<StaffSalaryStructureDto?>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
SELECT
    basic AS Basic,
    allowances AS Allowances,
    deductions AS Deductions
FROM salary_structures
WHERE school_id = @SchoolId
  AND staff_id = @StaffId
  AND is_deleted = FALSE
LIMIT 1;";

            var dto = await conn.QuerySingleOrDefaultAsync<StaffSalaryStructureDto>(sql, new
            {
                SchoolId = _tenantContext.SchoolId,
                StaffId = staffId
            });

            return ApiResponse<StaffSalaryStructureDto?>.Ok(dto, dto == null ? "Salary structure not found." : "Salary structure fetched.");
        }

        public async Task<ApiResponse<IReadOnlyCollection<SubjectDto>>> GetSubjectsAsync()
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<IReadOnlyCollection<SubjectDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var list = (await conn.QueryAsync<SubjectDto>(
                "SELECT id AS Id, name AS Name, code AS Code FROM subjects WHERE school_id = @SchoolId AND is_deleted = FALSE ORDER BY name",
                new { SchoolId = _tenantContext.SchoolId })).AsList();
            return ApiResponse<IReadOnlyCollection<SubjectDto>>.Ok(list, "Subjects fetched.");
        }

        public async Task<ApiResponse<SubjectDto>> CreateSubjectAsync(CreateSubjectRequestDto request)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<SubjectDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
            if (string.IsNullOrWhiteSpace(request.Name))
                return ApiResponse<SubjectDto>.Fail("Subject name is required.", ErrorCodes.Validation);

            var userId = _currentUser.UserId ?? Guid.Empty;
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                INSERT INTO subjects (id, school_id, is_deleted, created_at, created_by, name, code)
                VALUES (md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy, @Name, @Code)
                RETURNING id AS Id, name AS Name, code AS Code";
            var dto = await conn.QuerySingleOrDefaultAsync<SubjectDto>(sql, new
            {
                SchoolId = _tenantContext.SchoolId,
                CreatedBy = userId,
                Name = request.Name.Trim(),
                Code = string.IsNullOrWhiteSpace(request.Code) ? (object)DBNull.Value : request.Code.Trim()
            });
            if (dto == null)
                return ApiResponse<SubjectDto>.Fail("Failed to create subject.", ErrorCodes.BusinessRule);
            return ApiResponse<SubjectDto>.Ok(dto, "Subject created.");
        }

        public async Task<ApiResponse<SubjectDto>> UpdateSubjectAsync(Guid subjectId, UpdateSubjectRequestDto request)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<SubjectDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
            if (string.IsNullOrWhiteSpace(request.Name))
                return ApiResponse<SubjectDto>.Fail("Subject name is required.", ErrorCodes.Validation);

            var userId = _currentUser.UserId ?? Guid.Empty;
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE subjects
                SET name = @Name, code = @Code, updated_at = NOW(), updated_by = @UpdatedBy
                WHERE id = @SubjectId AND school_id = @SchoolId AND is_deleted = FALSE";
            var rows = await conn.ExecuteAsync(sql, new
            {
                SubjectId = subjectId,
                SchoolId = _tenantContext.SchoolId,
                Name = request.Name.Trim(),
                Code = string.IsNullOrWhiteSpace(request.Code) ? (object)DBNull.Value : request.Code.Trim(),
                UpdatedBy = userId
            });
            if (rows == 0)
                return ApiResponse<SubjectDto>.Fail("Subject not found or cannot be updated.", ErrorCodes.BusinessRule);
            return ApiResponse<SubjectDto>.Ok(new SubjectDto { Id = subjectId, Name = request.Name.Trim(), Code = request.Code?.Trim() }, "Subject updated.");
        }

        public async Task<ApiResponse<object>> SoftDeleteSubjectAsync(Guid subjectId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            var userId = _currentUser.UserId ?? Guid.Empty;
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
                UPDATE subjects
                SET is_deleted = TRUE, updated_at = NOW(), updated_by = @UpdatedBy
                WHERE id = @SubjectId AND school_id = @SchoolId AND is_deleted = FALSE";
            var rows = await conn.ExecuteAsync(sql, new { SubjectId = subjectId, SchoolId = _tenantContext.SchoolId, UpdatedBy = userId });
            if (rows == 0)
                return ApiResponse<object>.Fail("Subject not found or already deleted.", ErrorCodes.BusinessRule);
            return ApiResponse<object>.Ok(null, "Subject deleted.");
        }

        public async Task<ApiResponse<ClassTeacherAssignmentDto?>> GetStaffClassTeacherAssignmentAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<ClassTeacherAssignmentDto?>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            var json = await conn.ExecuteScalarAsync<string>(
                "SELECT fn_class_teacher_get_by_teacher(@p_school_id,@p_teacher_id)",
                new { p_school_id = _tenantContext.SchoolId, p_teacher_id = staffId });

            ClassTeacherAssignmentDto? data = null;
            if (!string.IsNullOrWhiteSpace(json) && json != "null")
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    data = JsonSerializer.Deserialize<ClassTeacherAssignmentDto>(json, options);
                }
                catch
                {
                    // ignore deserialize errors
                }
            }
            return ApiResponse<ClassTeacherAssignmentDto?>.Ok(data, "Class teacher assignment fetched.");
        }

        public async Task<ApiResponse<IReadOnlyCollection<ClassTeacherAssignmentDto>>> GetStaffClassTeacherAssignmentsAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<IReadOnlyCollection<ClassTeacherAssignmentDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
SELECT
    ct.class_id AS ""ClassId"",
    ct.section_id AS ""SectionId"",
    ct.academic_session_id AS ""AcademicSessionId""
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.teacher_id = @StaffId
  AND ct.is_deleted = FALSE
ORDER BY ct.created_at DESC;";

            var rows = await conn.QueryAsync<ClassTeacherAssignmentDto>(sql, new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                StaffId = staffId
            });
            var list = rows?.ToList() ?? new List<ClassTeacherAssignmentDto>();
            return ApiResponse<IReadOnlyCollection<ClassTeacherAssignmentDto>>.Ok(list, "Class teacher assignments fetched.");
        }

        public async Task<ApiResponse<IReadOnlyCollection<string>>> GetStaffPermissionsAsync(Guid staffId)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<IReadOnlyCollection<string>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();

            const string getUserSql = @"
SELECT u.id
FROM users u
INNER JOIN staff s ON s.school_id = u.school_id AND LOWER(TRIM(COALESCE(s.email,''))) = LOWER(TRIM(u.email))
WHERE s.id = @StaffId AND s.school_id = @SchoolId AND s.is_deleted = FALSE AND u.is_deleted = FALSE
LIMIT 1;";
            var userId = await conn.ExecuteScalarAsync<Guid?>(getUserSql, new { StaffId = staffId, SchoolId = _tenantContext.SchoolId });
            if (userId == null)
                return ApiResponse<IReadOnlyCollection<string>>.Ok(Array.Empty<string>(), "No login user linked to this staff; permissions list empty.");

            const string getPermsSql = @"
SELECT p.name
FROM user_permissions up
JOIN permissions p ON p.id = up.permission_id AND p.is_deleted = FALSE
WHERE up.user_id = @UserId AND up.is_deleted = FALSE
ORDER BY p.name;";
            var list = (await conn.QueryAsync<string>(getPermsSql, new { UserId = userId })).AsList();
            return ApiResponse<IReadOnlyCollection<string>>.Ok(list, "Permissions fetched.");
        }

        public async Task<ApiResponse<object>> UpdateStaffPermissionsAsync(Guid staffId, IReadOnlyCollection<string> permissions)
        {
            if (_tenantContext.SchoolId is null)
                return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

            using var conn = await _connectionFactory.CreateConnectionAsync();

            // Guardrail: never allow teachers to be granted school-wide payroll/staff admin permissions via per-user overrides.
            // Teachers should get access via their role only (safe baseline).
            var isTeacher = await conn.ExecuteScalarAsync<bool>(@"
SELECT COALESCE(is_teaching, FALSE)
FROM staff
WHERE id = @StaffId AND school_id = @SchoolId AND is_deleted = FALSE
LIMIT 1;", new { StaffId = staffId, SchoolId = _tenantContext.SchoolId.Value });

            const string getUserSql = @"
SELECT u.id
FROM users u
INNER JOIN staff s ON s.school_id = u.school_id AND LOWER(TRIM(COALESCE(s.email,''))) = LOWER(TRIM(u.email))
WHERE s.id = @StaffId AND s.school_id = @SchoolId AND s.is_deleted = FALSE AND u.is_deleted = FALSE
LIMIT 1;";
            var userId = await conn.ExecuteScalarAsync<Guid?>(getUserSql, new { StaffId = staffId, SchoolId = _tenantContext.SchoolId });
            if (userId == null)
                return ApiResponse<object>.Fail("No login user linked to this staff. Ensure staff has an email that matches a user.", ErrorCodes.BusinessRule);

            var updatedBy = _currentUser.UserId ?? Guid.Empty;
            var schoolId = _tenantContext.SchoolId.Value;

            IReadOnlyCollection<string> effectivePermissions = permissions ?? Array.Empty<string>();
            if (isTeacher)
            {
                // Allowlist teacher overrides. Keep this intentionally small.
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Fees.View",
                    "Payroll.View",
                    "Payroll.History.View",
                    "Profile.Manage",
                    "Leave.Apply",
                    "Leave.ViewOwn",
                    "Communication.Announcements.View",
                    "Timetable.View",
                    "Reporting.Students.View"
                };
                effectivePermissions = effectivePermissions
                    .Where(p => !string.IsNullOrWhiteSpace(p) && allowed.Contains(p.Trim()))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            const string softDeleteSql = @"
UPDATE user_permissions
SET is_deleted = TRUE, updated_at = NOW(), updated_by = @UpdatedBy
WHERE user_id = @UserId;";
            await conn.ExecuteAsync(softDeleteSql, new { UserId = userId, UpdatedBy = updatedBy });

            if (effectivePermissions != null && effectivePermissions.Count > 0)
            {
                const string getPermIdSql = @"
SELECT id FROM permissions
WHERE name = @Name AND (school_id = @SchoolId OR school_id IS NULL) AND is_deleted = FALSE
ORDER BY school_id DESC NULLS LAST
LIMIT 1;";
                foreach (var name in effectivePermissions)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var permId = await conn.ExecuteScalarAsync<Guid?>(getPermIdSql, new { Name = name.Trim(), SchoolId = schoolId });
                    if (permId == null) continue;
                    const string insertSql = @"
INSERT INTO user_permissions (id, school_id, is_deleted, created_at, created_by, user_id, permission_id)
VALUES (md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy, @UserId, @PermissionId)
ON CONFLICT (user_id, permission_id) DO UPDATE SET is_deleted = FALSE, updated_at = NOW(), updated_by = @CreatedBy;";
                    await conn.ExecuteAsync(insertSql, new { SchoolId = schoolId, CreatedBy = updatedBy, UserId = userId, PermissionId = permId });
                }
            }

            return ApiResponse<object>.Ok(null, "Permissions updated.");
        }

    /// <summary>Result row returned by PostgreSQL CALL sp_staff_create (OUT parameters as columns).</summary>
    private sealed class StaffCreateOutput
    {
        public Guid O_staff_id { get; set; }
        public string? O_staff_code { get; set; }
    }
}

