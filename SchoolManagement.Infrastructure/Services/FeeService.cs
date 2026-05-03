using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Fees.Dtos;
using SchoolManagement.Application.Fees.Services;

namespace SchoolManagement.Infrastructure.Services;

public class FeeService : IFeeService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public FeeService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<object>> RecordFeePaymentAsync(RecordFeePaymentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var restrictToAssignedClasses = ShouldRestrictFeePaymentsToAssignedClasses();
        if (restrictToAssignedClasses)
        {
            var canAccess = await IsStudentInAssignedClassAsync(conn, request.StudentId);
            if (!canAccess)
                return ApiResponse<object>.Fail("You can only record payments for students in your assigned class/section.", ErrorCodes.BusinessRule);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", request.StudentId);
        p.Add("p_receipt_number", request.ReceiptNumber);
        p.Add("p_amount_paid", request.AmountPaid);
        p.Add("p_payment_date", request.PaymentDate);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        try
        {
            await conn.ExecuteAsync(
                "CALL sp_fee_record_payment(@p_school_id,@p_student_id,@p_receipt_number,@p_amount_paid,@p_payment_date,@p_created_by)",
                p);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return ApiResponse<object>.Fail(
                "This receipt number is already used for this school. Leave receipt number blank to auto-generate, or use a different number.",
                ErrorCodes.BusinessRule);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            return ApiResponse<object>.Fail(ex.MessageText, ErrorCodes.BusinessRule);
        }

        return ApiResponse<object>.Ok(null, "Fee payment recorded.");
    }

    public async Task<PaymentsListResponseDto> GetPaymentsAsync(Guid schoolId, int page, int pageSize, string? search, Guid? studentId, int? year, int? month, bool restrictToAssignedClasses = false)
    {
        var searchTerm = search ?? string.Empty;
        var p = Math.Max(1, page);
        var ps = Math.Max(1, pageSize);
        var offset = (p - 1) * ps;

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var effectiveRestrictScope = restrictToAssignedClasses || ShouldRestrictFeePaymentsToAssignedClasses();
        var assignedStaffId = effectiveRestrictScope ? await ResolveCurrentStaffIdAsync(conn) : null;
        if (effectiveRestrictScope && assignedStaffId == null)
        {
            return new PaymentsListResponseDto
            {
                Data = new List<PaymentListItemDto>(),
                TotalCount = 0,
                TotalCollected = 0
            };
        }

        const string totalsSql = @"
            SELECT COUNT(*)::bigint AS TotalCount, COALESCE(SUM(fp.amount_paid), 0) AS TotalCollected
            FROM fee_payments fp
            LEFT JOIN students s ON s.id = fp.student_id AND s.school_id = fp.school_id
            WHERE fp.school_id = @SchoolId
              AND (fp.is_deleted = false OR fp.is_deleted IS NULL)
              AND (@StudentId IS NULL OR fp.student_id = @StudentId)
              AND (@Year IS NULL OR EXTRACT(YEAR FROM fp.payment_date) = @Year)
              AND (@Month IS NULL OR EXTRACT(MONTH FROM fp.payment_date) = @Month)
              AND (@Search = '' OR fp.receipt_number ILIKE '%' || @Search || '%'
                   OR (COALESCE(s.first_name,'') || ' ' || COALESCE(s.last_name,'')) ILIKE '%' || @Search || '%'
                   OR COALESCE(s.admission_no,'') ILIKE '%' || @Search || '%')
              AND (
                    @RestrictScope = FALSE
                    OR EXISTS (
                        SELECT 1
                        FROM class_teachers ct
                        WHERE ct.school_id = fp.school_id
                          AND ct.is_deleted = FALSE
                          AND ct.teacher_id = @StaffId
                          AND ct.class_id = s.class_id
                          AND ct.section_id = s.section_id
                    )
                  )";
        var totals = await conn.QueryFirstOrDefaultAsync<(long TotalCount, decimal TotalCollected)>(totalsSql,
            new
            {
                SchoolId = schoolId,
                Search = searchTerm,
                StudentId = studentId,
                Year = year,
                Month = month,
                RestrictScope = effectiveRestrictScope,
                StaffId = assignedStaffId
            });

        const string listSql = @"
            SELECT fp.id AS Id, fp.receipt_number AS ReceiptNumber, fp.student_id AS StudentId,
                   TRIM(COALESCE(s.first_name,'') || ' ' || COALESCE(s.last_name,'')) AS StudentName,
                   COALESCE(s.admission_no,'') AS AdmissionNo, fp.amount_paid AS AmountPaid,
                   'cash' AS PaymentMethod, fp.payment_date AS PaymentDate
            FROM fee_payments fp
            LEFT JOIN students s ON s.id = fp.student_id AND s.school_id = fp.school_id
            WHERE fp.school_id = @SchoolId
              AND (fp.is_deleted = false OR fp.is_deleted IS NULL)
              AND (@StudentId IS NULL OR fp.student_id = @StudentId)
              AND (@Year IS NULL OR EXTRACT(YEAR FROM fp.payment_date) = @Year)
              AND (@Month IS NULL OR EXTRACT(MONTH FROM fp.payment_date) = @Month)
              AND (@Search = '' OR fp.receipt_number ILIKE '%' || @Search || '%'
                   OR (COALESCE(s.first_name,'') || ' ' || COALESCE(s.last_name,'')) ILIKE '%' || @Search || '%'
                   OR COALESCE(s.admission_no,'') ILIKE '%' || @Search || '%')
              AND (
                    @RestrictScope = FALSE
                    OR EXISTS (
                        SELECT 1
                        FROM class_teachers ct
                        WHERE ct.school_id = fp.school_id
                          AND ct.is_deleted = FALSE
                          AND ct.teacher_id = @StaffId
                          AND ct.class_id = s.class_id
                          AND ct.section_id = s.section_id
                    )
                  )
            ORDER BY fp.payment_date DESC
            LIMIT @PageSize OFFSET @Offset";
        var items = (await conn.QueryAsync<PaymentListItemDto>(listSql,
            new
            {
                SchoolId = schoolId,
                Search = searchTerm,
                StudentId = studentId,
                Year = year,
                Month = month,
                RestrictScope = effectiveRestrictScope,
                StaffId = assignedStaffId,
                PageSize = ps,
                Offset = offset
            })).AsList();

        return new PaymentsListResponseDto
        {
            Data = items,
            TotalCount = totals.TotalCount,
            TotalCollected = totals.TotalCollected
        };
    }

    private static bool IsTeacherOrStaffRole(string? role) =>
        role != null && (role.Equals("Teacher", StringComparison.OrdinalIgnoreCase)
                         || role.Equals("Staff", StringComparison.OrdinalIgnoreCase));

    private bool IsTeacherOrStaff() => IsTeacherOrStaffRole(_currentUser.Role);

    private bool HasFeesManageAll() => _currentUser.Permissions.Contains("Fees.Manage.All");

    private bool HasFeesViewAll() => _currentUser.Permissions.Contains("Fees.View.All");

    private bool HasFeesPaymentsAll() => _currentUser.Permissions.Contains("Fees.Payments.All");

    /// <summary>
    /// Teacher/Staff with only scoped <c>Fees.Manage</c> (no <c>Fees.Manage.All</c>) may only change structure and assignments for assigned classes/students.
    /// </summary>
    private bool ShouldEnforceScopedFeesManage() =>
        IsTeacherOrStaff()
        && _currentUser.Permissions.Contains("Fees.Manage")
        && !HasFeesManageAll();

    /// <summary>
    /// Scoped fee view: cannot load class fee structure for arbitrary classes.
    /// </summary>
    private bool ShouldEnforceScopedFeesClassRead() =>
        IsTeacherOrStaff()
        && !HasFeesManageAll()
        && !HasFeesViewAll()
        && (_currentUser.Permissions.Contains("Fees.View") || _currentUser.Permissions.Contains("Fees.Manage"));

    /// <summary>
    /// Scoped student fee reads (assignments / summary).
    /// </summary>
    private bool ShouldEnforceScopedFeesStudentRead() =>
        IsTeacherOrStaff()
        && !HasFeesViewAll()
        && !HasFeesManageAll()
        && _currentUser.Permissions.Contains("Fees.View");

    /// <summary>
    /// List pending fees by class/section: teachers without View.All / Manage.All / Payments.All only for assigned slots.
    /// </summary>
    private bool ShouldEnforceScopedClassSectionPendingList() =>
        IsTeacherOrStaff()
        && !HasFeesViewAll()
        && !HasFeesManageAll()
        && !HasFeesPaymentsAll();

    private bool ShouldRestrictFeePaymentsToAssignedClasses()
    {
        if (!IsTeacherOrStaff()) return false;
        // Global fees roles: full school payment access
        if (HasFeesManageAll() || HasFeesPaymentsAll()) return false;

        if (_currentUser.Permissions.Contains("Fees.Payments")
            || _currentUser.Permissions.Contains("Fees.Manage"))
            return true;

        return false;
    }

    private async Task<bool> IsAssignedToClassAsync(IDbConnection conn, Guid classId)
    {
        var staffId = await ResolveCurrentStaffIdAsync(conn);
        if (staffId == null || _tenantContext.SchoolId == null) return false;

        const string sql = @"
SELECT 1
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.is_deleted = FALSE
  AND ct.teacher_id = @StaffId
  AND ct.class_id = @ClassId
LIMIT 1;";
        var allowed = await conn.ExecuteScalarAsync<int?>(
            sql,
            new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                StaffId = staffId.Value,
                ClassId = classId
            });

        return allowed.HasValue && allowed.Value == 1;
    }

    private async Task<bool> IsAssignedToClassSectionAsync(IDbConnection conn, Guid classId, Guid sectionId)
    {
        var staffId = await ResolveCurrentStaffIdAsync(conn);
        if (staffId == null || _tenantContext.SchoolId == null) return false;

        const string sql = @"
SELECT 1
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.is_deleted = FALSE
  AND ct.teacher_id = @StaffId
  AND ct.class_id = @ClassId
  AND ct.section_id = @SectionId
LIMIT 1;";
        var allowed = await conn.ExecuteScalarAsync<int?>(
            sql,
            new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                StaffId = staffId.Value,
                ClassId = classId,
                SectionId = sectionId
            });

        return allowed.HasValue && allowed.Value == 1;
    }

    private async Task<bool> StudentBelongsToClassAsync(IDbConnection conn, Guid studentId, Guid classId)
    {
        if (_tenantContext.SchoolId == null) return false;

        const string sql = @"
SELECT 1
FROM students s
WHERE s.id = @StudentId
  AND s.school_id = @SchoolId
  AND s.is_deleted = FALSE
  AND s.class_id = @ClassId
LIMIT 1;";
        var ok = await conn.ExecuteScalarAsync<int?>(
            sql,
            new { StudentId = studentId, SchoolId = _tenantContext.SchoolId.Value, ClassId = classId });

        return ok.HasValue && ok.Value == 1;
    }

    private async Task<Guid?> GetStudentIdForFeeAssignmentAsync(IDbConnection conn, Guid assignmentId)
    {
        if (_tenantContext.SchoolId == null) return null;

        const string sql = @"
SELECT student_id
FROM student_fee_assignments
WHERE id = @AssignmentId
  AND school_id = @SchoolId
  AND (is_deleted = FALSE OR is_deleted IS NULL)
LIMIT 1;";
        return await conn.ExecuteScalarAsync<Guid?>(
            sql,
            new { AssignmentId = assignmentId, SchoolId = _tenantContext.SchoolId.Value });
    }

    private async Task<bool> IsStudentInAssignedClassAsync(IDbConnection conn, Guid studentId)
    {
        var staffId = await ResolveCurrentStaffIdAsync(conn);
        if (staffId == null || _tenantContext.SchoolId == null) return false;

        const string sql = @"
SELECT 1
FROM students s
WHERE s.id = @StudentId
  AND s.school_id = @SchoolId
  AND s.is_deleted = FALSE
  AND EXISTS (
      SELECT 1
      FROM class_teachers ct
      WHERE ct.school_id = s.school_id
        AND ct.teacher_id = @StaffId
        AND ct.class_id = s.class_id
        AND ct.section_id = s.section_id
        AND ct.is_deleted = FALSE
  )
LIMIT 1;";
        var allowed = await conn.ExecuteScalarAsync<int?>(
            sql,
            new
            {
                StudentId = studentId,
                SchoolId = _tenantContext.SchoolId.Value,
                StaffId = staffId.Value
            });

        return allowed.HasValue && allowed.Value == 1;
    }

    private async Task<Guid?> ResolveCurrentStaffIdAsync(IDbConnection conn)
    {
        if (_tenantContext.SchoolId == null || _currentUser.UserId == null)
            return null;

        const string userSql = @"
SELECT email
FROM users
WHERE id = @UserId
  AND school_id = @SchoolId
  AND is_deleted = FALSE
LIMIT 1;";
        var email = await conn.ExecuteScalarAsync<string?>(
            userSql,
            new
            {
                UserId = _currentUser.UserId.Value,
                SchoolId = _tenantContext.SchoolId.Value
            });

        if (string.IsNullOrWhiteSpace(email))
            return null;

        const string staffSql = @"
SELECT id
FROM staff
WHERE school_id = @SchoolId
  AND LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";
        return await conn.ExecuteScalarAsync<Guid?>(
            staffSql,
            new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                Email = email
            });
    }

    public async Task<ApiResponse<FeeHeadDto>> CreateFeeHeadAsync(CreateFeeHeadRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<FeeHeadDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (ShouldEnforceScopedFeesManage())
            return ApiResponse<FeeHeadDto>.Fail(
                "You do not have permission to create school-wide fee heads. Ask a school administrator or request Fees.Manage.All.",
                ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_name", request.Name);
        p.Add("p_is_recurring", request.IsRecurring);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_fee_head_create(@p_school_id,@p_name,@p_is_recurring,@p_created_by)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<FeeHeadDto>.Fail("Fee head creation returned no data.", ErrorCodes.BusinessRule);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<FeeHeadDto>(json, options);
        if (dto == null)
            return ApiResponse<FeeHeadDto>.Fail("Fee head creation failed.", ErrorCodes.BusinessRule);

        return ApiResponse<FeeHeadDto>.Ok(dto, "Fee head created.");
    }

    public async Task<ApiResponse<FeeHeadDto>> UpdateFeeHeadAsync(Guid feeHeadId, UpdateFeeHeadRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<FeeHeadDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (ShouldEnforceScopedFeesManage())
            return ApiResponse<FeeHeadDto>.Fail(
                "You do not have permission to edit fee heads. Ask a school administrator or request Fees.Manage.All.",
                ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_fee_head_id", feeHeadId);
        p.Add("p_name", request.Name);
        p.Add("p_is_recurring", request.IsRecurring);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_fee_head_update(@p_school_id,@p_fee_head_id,@p_name,@p_is_recurring,@p_user_id)", p);

        var dto = new FeeHeadDto
        {
            Id = feeHeadId,
            Name = request.Name,
            IsRecurring = request.IsRecurring
        };

        return ApiResponse<FeeHeadDto>.Ok(dto, "Fee head updated.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<FeeHeadDto>>> GetFeeHeadsAsync(bool showInactive)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<FeeHeadDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_fee_heads_get_all(@p_school_id,@p_include_deleted)",
            new
            {
                p_school_id = _tenantContext.SchoolId,
                p_include_deleted = showInactive
            });

        var items = string.IsNullOrWhiteSpace(json)
            ? Array.Empty<FeeHeadDto>()
            : JsonSerializer.Deserialize<IReadOnlyCollection<FeeHeadDto>>(json) ?? Array.Empty<FeeHeadDto>();

        return ApiResponse<IReadOnlyCollection<FeeHeadDto>>.Ok(items, "Fee heads fetched.");
    }

    public async Task<ApiResponse<object>> SoftDeleteFeeHeadAsync(Guid feeHeadId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (ShouldEnforceScopedFeesManage())
            return ApiResponse<object>.Fail(
                "You do not have permission to delete fee heads. Ask a school administrator or request Fees.Manage.All.",
                ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_fee_head_id", feeHeadId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_fee_head_soft_delete(@p_school_id,@p_fee_head_id,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Fee head deleted.");
    }

    public async Task<ApiResponse<object>> SetClassFeeStructureAsync(SetClassFeeStructureRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (request.FeeHeadIds.Length != request.Amounts.Length)
            return ApiResponse<object>.Fail("FeeHeadIds and Amounts length mismatch.", ErrorCodes.Validation);

        var isOptional = request.IsOptional?.Length == request.FeeHeadIds.Length
            ? request.IsOptional
            : new bool[request.FeeHeadIds.Length];

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            if (!await IsAssignedToClassAsync(conn, request.ClassId))
                return ApiResponse<object>.Fail(
                    "You can only set fee structure for classes you are assigned to as class teacher.",
                    ErrorCodes.Forbidden);
        }
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_fee_head_ids", request.FeeHeadIds);
        p.Add("p_amounts", request.Amounts);
        p.Add("p_is_optional", isOptional);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_fee_structure_set(@p_school_id,@p_class_id,@p_fee_head_ids,@p_amounts,@p_is_optional,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Class fee structure set.");
    }

    public async Task<ApiResponse<object>> GetClassFeeStructureAsync(Guid classId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesClassRead())
        {
            if (!await IsAssignedToClassAsync(conn, classId))
                return ApiResponse<object>.Fail(
                    "You can only view fee structure for classes you are assigned to as class teacher.",
                    ErrorCodes.Forbidden);
        }

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_class_fee_structures_get(@p_school_id,@p_class_id)",
            new
            {
                p_school_id = _tenantContext.SchoolId,
                p_class_id = classId
            });

        var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<object>(json);

        return ApiResponse<object>.Ok(data, "Class fee structure fetched.");
    }

    public async Task<ApiResponse<object>> AssignStudentFeesFromClassAsync(AssignStudentFeesFromClassRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            if (!await IsStudentInAssignedClassAsync(conn, request.StudentId))
                return ApiResponse<object>.Fail(
                    "You can only assign fees for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
            if (!await StudentBelongsToClassAsync(conn, request.StudentId, request.ClassId))
                return ApiResponse<object>.Fail("Student does not belong to the selected class.", ErrorCodes.Validation);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", request.StudentId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_due_date", request.DueDate.Date, dbType: DbType.Date);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_fee_assign_from_class(@p_school_id,@p_student_id,@p_class_id,@p_due_date,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Student fee assignments created from class.");
    }

    public async Task<ApiResponse<object>> GetStudentFeeAssignmentsAsync(Guid studentId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesStudentRead())
        {
            if (!await IsStudentInAssignedClassAsync(conn, studentId))
                return ApiResponse<object>.Fail(
                    "You can only view fee assignments for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_student_fee_assignments_get(@p_school_id,@p_student_id)",
            new
            {
                p_school_id = _tenantContext.SchoolId,
                p_student_id = studentId
            });

        var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<object>(json);

        return ApiResponse<object>.Ok(data, "Student fee assignments fetched.");
    }

    public async Task<ApiResponse<StudentFeeSummaryDto>> GetStudentFeeSummaryAsync(Guid studentId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentFeeSummaryDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesStudentRead())
        {
            if (!await IsStudentInAssignedClassAsync(conn, studentId))
                return ApiResponse<StudentFeeSummaryDto>.Fail(
                    "You can only view fee summary for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_student_fee_summary_get(@p_school_id,@p_student_id)",
            new
            {
                p_school_id = _tenantContext.SchoolId,
                p_student_id = studentId
            });

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StudentFeeSummaryDto>.Fail("Fee summary returned no data.", ErrorCodes.BusinessRule);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<StudentFeeSummaryDto>(json, options);
        if (dto == null)
            return ApiResponse<StudentFeeSummaryDto>.Fail("Fee summary failed.", ErrorCodes.BusinessRule);

        return ApiResponse<StudentFeeSummaryDto>.Ok(dto, "Fee summary fetched.");
    }

    public async Task<ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>> GetClassSectionFeePendingAsync(
        Guid classId,
        Guid sectionId,
        Guid academicSessionId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>.Fail(
                "Tenant context missing.",
                ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedClassSectionPendingList())
        {
            if (!await IsAssignedToClassSectionAsync(conn, classId, sectionId))
                return ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>.Fail(
                    "You can only view pending fees for your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        const string sql = @"
SELECT * FROM (
    SELECT
        s.id AS ""StudentId"",
        s.full_name AS ""StudentName"",
        s.admission_no AS ""AdmissionNo"",
        COALESCE(due.total_due, 0)::numeric(12,2) AS ""TotalDue"",
        COALESCE(paid.total_paid, 0)::numeric(12,2) AS ""TotalPaid"",
        (GREATEST(0, COALESCE(due.total_due, 0) - COALESCE(paid.total_paid, 0)))::numeric(12,2) AS ""Pending""
    FROM students s
    LEFT JOIN (
        SELECT sfa.student_id,
               SUM(sfa.amount - COALESCE(sfa.discount_amount, 0) + COALESCE(sfa.late_fine_amount, 0))::numeric(12,2) AS total_due
        FROM student_fee_assignments sfa
        WHERE sfa.school_id = @SchoolId
          AND sfa.is_deleted = FALSE
        GROUP BY sfa.student_id
    ) due ON due.student_id = s.id
    LEFT JOIN (
        SELECT fp.student_id,
               SUM(fp.amount_paid)::numeric(12,2) AS total_paid
        FROM fee_payments fp
        WHERE fp.school_id = @SchoolId
          AND fp.is_deleted = FALSE
        GROUP BY fp.student_id
    ) paid ON paid.student_id = s.id
    WHERE s.school_id = @SchoolId
      AND s.is_deleted = FALSE
      AND s.class_id = @ClassId
      AND s.section_id = @SectionId
      AND s.academic_session_id = @AcademicSessionId
) t
WHERE t.""Pending"" > 0
ORDER BY t.""Pending"" DESC, t.""StudentName"";";

        var rows = (await conn.QueryAsync<ClassSectionStudentFeePendingDto>(
                sql,
                new
                {
                    SchoolId = _tenantContext.SchoolId.Value,
                    ClassId = classId,
                    SectionId = sectionId,
                    AcademicSessionId = academicSessionId
                }))
            .ToList();

        return ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>.Ok(
            rows,
            "Pending fee list fetched.");
    }

    public async Task<ApiResponse<object>> AddOptionalFeeAsync(AddOptionalFeeRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            if (!await IsStudentInAssignedClassAsync(conn, request.StudentId))
                return ApiResponse<object>.Fail(
                    "You can only add fees for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
            if (!await StudentBelongsToClassAsync(conn, request.StudentId, request.ClassId))
                return ApiResponse<object>.Fail("Student does not belong to the selected class.", ErrorCodes.Validation);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", request.StudentId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_fee_head_id", request.FeeHeadId);
        p.Add("p_due_date", request.DueDate.Date, dbType: DbType.Date);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_fee_add_optional(@p_school_id,@p_student_id,@p_class_id,@p_fee_head_id,@p_due_date,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Optional fee added.");
    }

    public async Task<ApiResponse<object>> AddStudentFeeAsync(AddStudentFeeRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            if (!await IsStudentInAssignedClassAsync(conn, request.StudentId))
                return ApiResponse<object>.Fail(
                    "You can only add fees for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", request.StudentId);
        p.Add("p_fee_head_id", request.FeeHeadId);
        p.Add("p_amount", request.Amount);
        p.Add("p_due_date", request.DueDate.Date, dbType: DbType.Date);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        try
        {
            await conn.ExecuteAsync("CALL sp_student_fee_add_any(@p_school_id,@p_student_id,@p_fee_head_id,@p_amount,@p_due_date,@p_user_id)", p);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
        {
            return ApiResponse<object>.Fail(ex.MessageText, ErrorCodes.BusinessRule);
        }

        return ApiResponse<object>.Ok(null, "Fee added.");
    }

    public async Task<ApiResponse<object>> SetFeeAssignmentDiscountAsync(Guid assignmentId, SetFeeAssignmentDiscountRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            var sid = await GetStudentIdForFeeAssignmentAsync(conn, assignmentId);
            if (sid is null || !await IsStudentInAssignedClassAsync(conn, sid.Value))
                return ApiResponse<object>.Fail(
                    "You can only change discounts for fee assignments of students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_assignment_id", assignmentId);
        p.Add("p_discount_amount", request.DiscountAmount);
        // Dapper does not accept DBNull.Value here; Npgsql maps C# null to SQL NULL.
        var discountReason = string.IsNullOrWhiteSpace(request.DiscountReason)
            ? null
            : request.DiscountReason.Trim();
        p.Add("p_discount_reason", discountReason);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_fee_assignment_set_discount(@p_school_id,@p_assignment_id,@p_discount_amount,@p_discount_reason,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Discount updated.");
    }

    public async Task<ApiResponse<object>> RemoveFeeAssignmentAsync(Guid assignmentId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            var sid = await GetStudentIdForFeeAssignmentAsync(conn, assignmentId);
            if (sid is null || !await IsStudentInAssignedClassAsync(conn, sid.Value))
                return ApiResponse<object>.Fail(
                    "You can only remove fee assignments for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_assignment_id", assignmentId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_fee_assignment_soft_delete(@p_school_id,@p_assignment_id,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Fee assignment removed.");
    }

    public async Task<ApiResponse<object>> UpdateFeeAssignmentAsync(Guid assignmentId, UpdateFeeAssignmentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (ShouldEnforceScopedFeesManage())
        {
            var sid = await GetStudentIdForFeeAssignmentAsync(conn, assignmentId);
            if (sid is null || !await IsStudentInAssignedClassAsync(conn, sid.Value))
                return ApiResponse<object>.Fail(
                    "You can only update fee assignments for students in your assigned class and section.",
                    ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_assignment_id", assignmentId);
        p.Add("p_amount", request.Amount);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_fee_assignment_update_amount(@p_school_id,@p_assignment_id,@p_amount,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Fee assignment updated.");
    }
}

