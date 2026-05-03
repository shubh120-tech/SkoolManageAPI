using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Students.Dtos;
using SchoolManagement.Application.Students.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SchoolManagement.Infrastructure.Services;

public class StudentService : IStudentService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public StudentService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<StudentResponseDto>> AdmitStudentAsync(CreateStudentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (request.AcademicSessionId == Guid.Empty)
            return ApiResponse<StudentResponseDto>.Fail("Academic session is required.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        // Scoped admit: if user has only Student.Admit (without *.All / manage), restrict to assigned class+section.
        if (!HasAnyPermission("Student.Admit.All", "Student.Manage"))
        {
            var assigned = await IsAssignedToClassSectionAsync(conn, request.ClassId, request.SectionId);
            if (!assigned)
                return ApiResponse<StudentResponseDto>.Fail("You can admit students only for your assigned class and section.", ErrorCodes.Forbidden);
        }

        var classSectionValidation = await ValidateClassSectionSessionAsync(conn, request.ClassId, request.SectionId, request.AcademicSessionId);
        if (!string.IsNullOrWhiteSpace(classSectionValidation))
            return ApiResponse<StudentResponseDto>.Fail(classSectionValidation, ErrorCodes.BusinessRule);

        // Use session-aware path directly so admission does not depend on legacy DB active-session-only functions.
        var fallback = await TryAdmitUsingClassSessionAsync(conn, request);
        if (!string.IsNullOrWhiteSpace(fallback.Error))
            return ApiResponse<StudentResponseDto>.Fail(fallback.Error, ErrorCodes.BusinessRule);
        var created = fallback.Created;

        if (created == null || created.StudentId == Guid.Empty)
            return ApiResponse<StudentResponseDto>.Fail("Admit failed.", ErrorCodes.BusinessRule);

        var createdId = created.StudentId;
        var admissionNo = created.AdmissionNo ?? string.Empty;
        var fullName = created.FullName ??
                       $"{request.FirstName} {request.MiddleName} {request.LastName}".Replace("  ", " ").Trim();
        var academicSessionId = created.AcademicSessionId;

        var dto = new StudentResponseDto
        {
                Id = createdId,
                AdmissionNo = admissionNo,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                FullName = fullName,
                ClassId = request.ClassId,
                SectionId = request.SectionId,
                AcademicSessionId = academicSessionId
        };

        // Best-effort: assign fees for admission month.
        // - Mandatory fees for the month are assigned from class structure.
        // - One-time/admission fee heads (fee_heads.is_recurring = FALSE) are assigned once (not per month).
        await TryAssignAdmissionFeesAsync(conn, createdId, request.ClassId);

        return ApiResponse<StudentResponseDto>.Ok(dto, "Student admitted.");
    }

    private async Task TryAssignAdmissionFeesAsync(IDbConnection conn, Guid studentId, Guid classId)
    {
        try
        {
            if (_tenantContext.SchoolId is null) return;
            var schoolId = _tenantContext.SchoolId.Value;
            var userId = _currentUser.UserId ?? Guid.Empty;

            var utcNow = DateTime.UtcNow;
            var dueDate = new DateTime(utcNow.Year, utcNow.Month, 1);

            // Assign mandatory (non-optional) fee heads for this month.
            await conn.ExecuteAsync(
                "CALL sp_student_fee_assign_from_class(@p_school_id,@p_student_id,@p_class_id,@p_due_date,@p_user_id)",
                new
                {
                    p_school_id = schoolId,
                    p_student_id = studentId,
                    p_class_id = classId,
                    p_due_date = dueDate,
                    p_user_id = userId
                });

            // Assign one-time (non-recurring) fee heads once at admission time.
            // We treat any non-recurring fee head included in class_fee_structures as "assign on admission".
            await conn.ExecuteAsync(@"
INSERT INTO student_fee_assignments(
    id, school_id, is_deleted, created_at, created_by,
    student_id, fee_head_id, amount, due_date,
    discount_amount, late_fine_amount, is_paid)
SELECT
    md5(random()::text || clock_timestamp()::text)::uuid,
    cfs.school_id,
    FALSE,
    NOW(),
    @UserId,
    @StudentId,
    cfs.fee_head_id,
    cfs.amount,
    @DueDate,
    NULL,
    NULL,
    FALSE
FROM class_fee_structures cfs
JOIN fee_heads fh
  ON fh.id = cfs.fee_head_id
 AND fh.school_id = cfs.school_id
 AND fh.is_deleted = FALSE
WHERE cfs.school_id = @SchoolId
  AND cfs.class_id = @ClassId
  AND cfs.is_deleted = FALSE
  AND COALESCE(fh.is_recurring, TRUE) = FALSE
  AND COALESCE(cfs.is_optional, FALSE) = FALSE
  AND NOT EXISTS (
    SELECT 1
    FROM student_fee_assignments sfa
    WHERE sfa.school_id = cfs.school_id
      AND sfa.student_id = @StudentId
      AND sfa.fee_head_id = cfs.fee_head_id
      AND sfa.is_deleted = FALSE
  );",
                new
                {
                    SchoolId = schoolId,
                    StudentId = studentId,
                    ClassId = classId,
                    DueDate = dueDate,
                    UserId = userId
                });
        }
        catch
        {
            // Best-effort: admission should succeed even if fee assignment fails.
        }
    }

    private async Task<string?> ValidateClassSectionSessionAsync(
        IDbConnection conn,
        Guid classId,
        Guid sectionId,
        Guid academicSessionId)
    {
        if (_tenantContext.SchoolId is null) return "Tenant context missing.";

        const string classSql = @"
SELECT id, academic_session_id as AcademicSessionId
FROM classes
WHERE id = @ClassId AND school_id = @SchoolId AND is_deleted = FALSE
LIMIT 1;";
        var cls = await conn.QuerySingleOrDefaultAsync<ClassSessionRow>(
            classSql,
            new { ClassId = classId, SchoolId = _tenantContext.SchoolId.Value });
        if (cls is null) return "Class not found";
        if (cls.AcademicSessionId != academicSessionId)
            return "Selected class does not belong to the selected working session";

        const string sectionSql = @"
SELECT id, class_id as ClassId, academic_session_id as AcademicSessionId
FROM sections
WHERE id = @SectionId AND school_id = @SchoolId AND is_deleted = FALSE
LIMIT 1;";
        var sec = await conn.QuerySingleOrDefaultAsync<SectionSessionRow>(
            sectionSql,
            new { SectionId = sectionId, SchoolId = _tenantContext.SchoolId.Value });
        if (sec is null) return "Section not found";
        if (sec.ClassId != classId) return "Section does not belong to the selected class";
        if (sec.AcademicSessionId != academicSessionId) return "Section does not belong to the selected working session";
        return null;
    }

    private static string NormalizeAadharForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.Trim(), @"[\s\-]", "");
    }

    private async Task<(StudentAdmitFnResult? Created, string? Error)> TryAdmitUsingClassSessionAsync(IDbConnection conn, CreateStudentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return (null, "Tenant context missing.");

        var classSessionValidation = await ValidateClassSectionSessionAsync(conn, request.ClassId, request.SectionId, request.AcademicSessionId);
        if (!string.IsNullOrWhiteSpace(classSessionValidation))
            return (null, classSessionValidation);

        var normalizedAadhar = NormalizeAadharForCompare(request.AadharNo);
        if (!string.IsNullOrEmpty(normalizedAadhar))
        {
            const string dupAadharSql = @"
SELECT COUNT(1)::int
FROM students
WHERE school_id = @SchoolId
  AND is_deleted = FALSE
  AND regexp_replace(regexp_replace(trim(COALESCE(aadhar_no, '')), '\s', '', 'g'), '-', '', 'g') = @Norm;";
            var dup = await conn.ExecuteScalarAsync<int>(
                dupAadharSql,
                new { SchoolId = _tenantContext.SchoolId.Value, Norm = normalizedAadhar });
            if (dup > 0)
                return (null, "A student with this Aadhaar number already exists in this school.");
        }

        const string insertSql = @"
WITH lock_row AS (
    SELECT pg_advisory_xact_lock(hashtext('svc_student_admit_' || @SchoolId::text))
),
school_ok AS (
    SELECT s.max_students, s.license_end_date
    FROM schools s
    WHERE s.id = @SchoolId
      AND s.is_deleted = FALSE
      AND s.is_active = TRUE
      AND s.is_soft_deleted = FALSE
),
student_count AS (
    SELECT COUNT(*)::INT AS cnt
    FROM students st
    WHERE st.school_id = @SchoolId
      AND st.is_deleted = FALSE
),
next_adm AS (
    SELECT format(
        'ADM-%s',
        LPAD(
            (COALESCE(MAX(CAST(NULLIF(TRIM(substring(admission_no from '[0-9]+$')), '') AS INT)), 0) + 1)::text,
            6,
            '0'
        )
    ) AS admission_no
    FROM students
    WHERE school_id = @SchoolId
),
next_roll AS (
    SELECT COALESCE(MAX(roll_no), 0) + 1 AS roll_no
    FROM students
    WHERE school_id = @SchoolId
      AND class_id = @ClassId
      AND section_id = @SectionId
      AND academic_session_id = @AcademicSessionId
      AND is_deleted = FALSE
),
inserted AS (
    INSERT INTO students(
        id, school_id, is_deleted, created_at, created_by,
        admission_no, first_name, middle_name, last_name, full_name,
        class_id, section_id, academic_session_id, date_of_birth, email,
        parent_mobile_no, father_name, mother_name, address_line, blood_group, aadhar_no, roll_no,
        udise_no, father_aadhar_no, mother_aadhar_no, father_occupation, mother_occupation, pen_no,
        bank_name, bank_account_no, bank_ifsc, bank_branch
    )
    SELECT
        md5(random()::text || clock_timestamp()::text)::uuid,
        @SchoolId,
        FALSE,
        NOW(),
        @CreatedBy,
        n.admission_no,
        @FirstName,
        @MiddleName,
        @LastName,
        @FullName,
        @ClassId,
        @SectionId,
        @AcademicSessionId,
        @DateOfBirth::date,
        @Email,
        @ParentMobileNo,
        @FatherName,
        @MotherName,
        @AddressLine,
        @BloodGroup,
        @AadharNo,
        r.roll_no,
        @UdiseNo,
        @FatherAadharNo,
        @MotherAadharNo,
        @FatherOccupation,
        @MotherOccupation,
        @PenNo,
        @BankName,
        @BankAccountNo,
        @BankIfsc,
        @BankBranch
    FROM next_adm n
    CROSS JOIN next_roll r
    CROSS JOIN school_ok so
    CROSS JOIN student_count sc
    WHERE (so.license_end_date IS NULL OR so.license_end_date >= NOW())
      AND (so.max_students <= 0 OR sc.cnt < so.max_students)
    RETURNING id, admission_no, full_name
)
SELECT i.id AS StudentId,
       i.admission_no AS AdmissionNo,
       i.full_name AS FullName,
       @AcademicSessionId AS AcademicSessionId
FROM inserted i;";

        var dto = await conn.QuerySingleOrDefaultAsync<StudentAdmitFnResult>(
            insertSql,
            new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                ClassId = request.ClassId,
                SectionId = request.SectionId,
                AcademicSessionId = request.AcademicSessionId,
                CreatedBy = _currentUser.UserId ?? Guid.Empty,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                FullName = $"{request.FirstName} {request.MiddleName} {request.LastName}".Replace("  ", " ").Trim(),
                DateOfBirth = DateTime.SpecifyKind(request.DateOfBirth.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                Email = request.Email,
                ParentMobileNo = request.ParentMobileNo,
                FatherName = request.FatherName,
                MotherName = request.MotherName,
                AddressLine = BuildAddressLine(request.AddressLine, request.City, request.State),
                BloodGroup = request.BloodGroup,
                AadharNo = request.AadharNo,
                UdiseNo = request.UdiseNo,
                FatherAadharNo = request.FatherAadharNo,
                MotherAadharNo = request.MotherAadharNo,
                FatherOccupation = request.FatherOccupation,
                MotherOccupation = request.MotherOccupation,
                PenNo = request.PenNo,
                BankName = request.BankName,
                BankAccountNo = request.BankAccountNo,
                BankIfsc = request.BankIfsc,
                BankBranch = request.BankBranch
            });

        if (dto is null)
            return (null, "Student limit reached for subscription or school/license is inactive.");
        return (dto, null);
    }

    private sealed class ClassSessionRow
    {
        public Guid Id { get; set; }
        public Guid AcademicSessionId { get; set; }
    }

    private sealed class SectionSessionRow
    {
        public Guid Id { get; set; }
        public Guid ClassId { get; set; }
        public Guid AcademicSessionId { get; set; }
    }

    public async Task<ApiResponse<object>> PromoteStudentAsync(Guid studentId, PromoteStudentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        // School-wide: Student.Promote.All or Student.Manage. Scoped: Student.Promote only (class-teacher assignments).
        if (!HasAnyPermission("Student.Promote.All", "Student.Manage"))
        {
            if (!HasAnyPermission("Student.Promote"))
                return ApiResponse<object>.Fail("You do not have permission to promote students.", ErrorCodes.Forbidden);

            const string locSql = @"
SELECT class_id AS ClassId, section_id AS SectionId, academic_session_id AS AcademicSessionId
FROM students
WHERE id = @StudentId AND school_id = @SchoolId AND is_deleted = FALSE;";
            var cur = await conn.QuerySingleOrDefaultAsync<StudentLocationRow>(
                locSql,
                new { StudentId = studentId, SchoolId = _tenantContext.SchoolId.Value });
            if (cur is null)
                return ApiResponse<object>.Fail("Student not found.", ErrorCodes.NotFound);

            var assignments = await GetCurrentUserClassAssignmentsAsync(conn);
            bool TripleMatch(Guid classId, Guid sectionId, Guid sessionId) =>
                assignments.Any(a =>
                    a.ClassId == classId &&
                    a.SectionId == sectionId &&
                    a.AcademicSessionId == sessionId);

            if (!TripleMatch(cur.ClassId, cur.SectionId, cur.AcademicSessionId))
                return ApiResponse<object>.Fail(
                    "You can only promote students in your assigned class, section, and session.",
                    ErrorCodes.Forbidden);
            if (!TripleMatch(request.ToClassId, request.ToSectionId, request.ToAcademicSessionId))
                return ApiResponse<object>.Fail(
                    "You can only promote into a class, section, and session you are assigned to.",
                    ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);
        p.Add("p_to_class_id", request.ToClassId);
        p.Add("p_to_section_id", request.ToSectionId);
        p.Add("p_to_academic_session_id", request.ToAcademicSessionId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        try
        {
            await conn.ExecuteAsync(
                "CALL sp_student_promote(@p_school_id,@p_student_id,@p_to_class_id,@p_to_section_id,@p_to_academic_session_id,@p_user_id)",
                p);
        }
        catch (Exception ex)
        {
            return ApiResponse<object>.Fail(ex.Message, ErrorCodes.BusinessRule);
        }

        return ApiResponse<object>.Ok(null, "Student promoted.");
    }

    private sealed class StudentLocationRow
    {
        public Guid ClassId { get; set; }
        public Guid SectionId { get; set; }
        public Guid AcademicSessionId { get; set; }
    }

    public async Task<ApiResponse<IReadOnlyCollection<StudentResponseDto>>> GetStudentsAsync(Guid academicSessionId, bool showInactive)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_students_get_all(@p_school_id,@p_academic_session_id,@p_include_deleted)",
            new
            {
                p_school_id = _tenantContext.SchoolId,
                p_academic_session_id = academicSessionId,
                p_include_deleted = showInactive
            });

        var items = string.IsNullOrWhiteSpace(json)
            ? Array.Empty<StudentResponseDto>()
            : JsonSerializer.Deserialize<IReadOnlyCollection<StudentResponseDto>>(json) ?? Array.Empty<StudentResponseDto>();

        return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Ok(items, "Students fetched.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<StudentResponseDto>>> GetStudentsByClassAndSectionAsync(Guid classId, Guid sectionId, Guid? academicSessionId = null, bool showInactive = false)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        // Enforce teacher/staff visibility to their assigned class/section only.
        var effectiveClassId = classId;
        var effectiveSectionId = sectionId;
        Guid? effectiveAcademicSessionId = academicSessionId;

        var role = _currentUser.Role ?? string.Empty;
        var isStaffOrTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase);

        // School-wide student lists for fee workflows (any *.All) bypass class-teacher assignment scope.
        if (isStaffOrTeacher && !BypassAssignedClassSectionScopeForStudentQueries())
        {
            var assignments = await GetCurrentUserClassAssignmentsAsync(conn);
            if (assignments.Count == 0)
                return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Ok(Array.Empty<StudentResponseDto>(), "No assigned class/section for this user.");

            var matched = assignments.FirstOrDefault(a =>
                a.ClassId.HasValue && a.SectionId.HasValue &&
                a.ClassId.Value == classId && a.SectionId.Value == sectionId);

            // If request does not match any assigned class/section, deny data.
            if (matched == null)
                return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Ok(Array.Empty<StudentResponseDto>(), "No assigned class/section for this user.");

            effectiveClassId = matched.ClassId!.Value;
            effectiveSectionId = matched.SectionId!.Value;
            effectiveAcademicSessionId = matched.AcademicSessionId;
        }

            var json = await conn.ExecuteScalarAsync<string>(
                "SELECT fn_students_get_by_class_section(@p_school_id,@p_class_id,@p_section_id,@p_include_deleted,@p_academic_session_id)",
                new
                {
                    p_school_id = _tenantContext.SchoolId,
                    p_class_id = effectiveClassId,
                    p_section_id = effectiveSectionId,
                    p_include_deleted = showInactive,
                    p_academic_session_id = effectiveAcademicSessionId
                });

            var items = string.IsNullOrWhiteSpace(json)
                ? Array.Empty<StudentResponseDto>()
                : JsonSerializer.Deserialize<IReadOnlyCollection<StudentResponseDto>>(json) ?? Array.Empty<StudentResponseDto>();

        return ApiResponse<IReadOnlyCollection<StudentResponseDto>>.Ok(items, "Students fetched.");
    }

    public async Task<ApiResponse<StudentResponseDto>> GetStudentByIdAsync(Guid studentId, bool showInactive = false)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
SELECT
    s.id AS Id,
    s.admission_no AS AdmissionNo,
    s.full_name AS FullName,
    s.first_name AS FirstName,
    s.middle_name AS MiddleName,
    s.last_name AS LastName,
    s.class_id AS ClassId,
    s.section_id AS SectionId,
    s.academic_session_id AS AcademicSessionId,
    s.date_of_birth AS DateOfBirth,
    s.email AS Email,
    s.parent_mobile_no AS ParentMobileNo,
    s.father_name AS FatherName,
    s.mother_name AS MotherName,
    s.address_line AS AddressLine,
    s.blood_group AS BloodGroup,
    s.aadhar_no AS AadharNo,
    s.roll_no AS RollNo,
    s.udise_no AS UdiseNo,
    s.father_aadhar_no AS FatherAadharNo,
    s.mother_aadhar_no AS MotherAadharNo,
    s.father_occupation AS FatherOccupation,
    s.mother_occupation AS MotherOccupation,
    s.pen_no AS PenNo,
    s.bank_name AS BankName,
    s.bank_account_no AS BankAccountNo,
    s.bank_ifsc AS BankIfsc,
    s.bank_branch AS BankBranch
FROM students s
WHERE s.school_id = @SchoolId
  AND s.id = @StudentId
  AND (@IncludeDeleted OR s.is_deleted = FALSE)
LIMIT 1;";

        var row = await conn.QuerySingleOrDefaultAsync<StudentGetByIdRow>(sql, new
        {
            SchoolId = _tenantContext.SchoolId,
            StudentId = studentId,
            IncludeDeleted = showInactive
        });

        if (row == null)
            return ApiResponse<StudentResponseDto>.Fail("Student not found.", ErrorCodes.NotFound);

        var role = _currentUser.Role ?? string.Empty;
        var isStaffOrTeacher = string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase);
        if (isStaffOrTeacher && !BypassAssignedClassSectionScopeForStudentQueries())
        {
            if (!await IsAssignedToClassSectionAsync(conn, row.ClassId, row.SectionId))
                return ApiResponse<StudentResponseDto>.Fail("Student not found.", ErrorCodes.NotFound);
        }

        var dto = new StudentResponseDto
        {
            Id = row.Id,
            AdmissionNo = row.AdmissionNo ?? string.Empty,
            FullName = row.FullName ?? string.Empty,
            FirstName = row.FirstName ?? string.Empty,
            MiddleName = row.MiddleName,
            LastName = row.LastName ?? string.Empty,
            ClassId = row.ClassId,
            SectionId = row.SectionId,
            AcademicSessionId = row.AcademicSessionId,
            DateOfBirth = row.DateOfBirth.HasValue
                ? DateOnly.FromDateTime(row.DateOfBirth.Value.Date)
                : null,
            Email = row.Email,
            ParentMobileNo = row.ParentMobileNo,
            FatherName = row.FatherName,
            MotherName = row.MotherName,
            AddressLine = row.AddressLine,
            BloodGroup = row.BloodGroup,
            AadharNo = row.AadharNo,
            RollNo = row.RollNo,
            UdiseNo = row.UdiseNo,
            FatherAadharNo = row.FatherAadharNo,
            MotherAadharNo = row.MotherAadharNo,
            FatherOccupation = row.FatherOccupation,
            MotherOccupation = row.MotherOccupation,
            PenNo = row.PenNo,
            BankName = row.BankName,
            BankAccountNo = row.BankAccountNo,
            BankIfsc = row.BankIfsc,
            BankBranch = row.BankBranch
        };

        return ApiResponse<StudentResponseDto>.Ok(dto, "Student fetched.");
    }

    private sealed class ClassTeacherAssignmentResult
    {
        public Guid? ClassId { get; set; }
        public Guid? SectionId { get; set; }
        public Guid? AcademicSessionId { get; set; }
    }

    public async Task<ApiResponse<StudentResponseDto>> UpdateStudentAsync(Guid studentId, UpdateStudentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        // Scoped update: if user has only Student.Update (without *.All / manage), restrict to assigned class+section.
        if (!HasAnyPermission("Student.Update.All", "Student.Manage"))
        {
            var assigned = await IsAssignedToClassSectionAsync(conn, request.ClassId, request.SectionId);
            if (!assigned)
                return ApiResponse<StudentResponseDto>.Fail("You can update students only for your assigned class and section.", ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);
        p.Add("p_first_name", request.FirstName);
        p.Add("p_middle_name", request.MiddleName);
        p.Add("p_last_name", request.LastName);
        p.Add(
            "p_date_of_birth",
            DateTime.SpecifyKind(request.DateOfBirth.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            DbType.DateTime);
        p.Add("p_email", request.Email);
        p.Add("p_parent_mobile_no", request.ParentMobileNo);
        p.Add("p_father_name", request.FatherName);
        p.Add("p_mother_name", request.MotherName);
        p.Add("p_address_line", request.AddressLine);
        p.Add("p_blood_group", request.BloodGroup);
        p.Add("p_aadhar_no", request.AadharNo);
        p.Add("p_udise_no", request.UdiseNo);
        p.Add("p_father_aadhar_no", request.FatherAadharNo);
        p.Add("p_mother_aadhar_no", request.MotherAadharNo);
        p.Add("p_father_occupation", request.FatherOccupation);
        p.Add("p_mother_occupation", request.MotherOccupation);
        p.Add("p_pen_no", request.PenNo);
        p.Add("p_bank_name", request.BankName);
        p.Add("p_bank_account_no", request.BankAccountNo);
        p.Add("p_bank_ifsc", request.BankIfsc);
        p.Add("p_bank_branch", request.BankBranch);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_section_id", request.SectionId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync(
            "sp_student_update",
            p,
            commandType: CommandType.StoredProcedure);

        var dto = new StudentResponseDto
        {
            Id = studentId,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            FullName = $"{request.FirstName} {request.MiddleName} {request.LastName}".Replace("  ", " ").Trim(),
            AdmissionNo = string.Empty
        };

        return ApiResponse<StudentResponseDto>.Ok(dto, "Student updated.");
    }

    public async Task<ApiResponse<object>> SoftDeleteStudentAsync(Guid studentId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_student_soft_delete(@p_school_id,@p_student_id,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Student deleted.");
    }

    private bool HasAnyPermission(params string[] permissions)
    {
        var current = _currentUser.Permissions ?? Array.Empty<string>();
        return permissions.Any(p => current.Contains(p));
    }

    /// <summary>
    /// Teacher/Staff may list students for any class when they have global student or global fees access
    /// (fee pages call /students/by-class-section without Student.View.All).
    /// </summary>
    private bool BypassAssignedClassSectionScopeForStudentQueries() =>
        HasAnyPermission(
            "Student.View.All",
            "Student.Promote.All",
            "Student.Manage",
            "Fees.View.All",
            "Fees.Manage.All",
            "Fees.Payments.All");

    private async Task<bool> IsAssignedToClassSectionAsync(IDbConnection conn, Guid classId, Guid sectionId)
    {
        var assignments = await GetCurrentUserClassAssignmentsAsync(conn);
        return assignments.Any(a =>
            a.ClassId.HasValue && a.SectionId.HasValue &&
            a.ClassId.Value == classId && a.SectionId.Value == sectionId);
    }

    private async Task<List<ClassTeacherAssignmentResult>> GetCurrentUserClassAssignmentsAsync(IDbConnection conn)
    {
        if (_tenantContext.SchoolId is null || _currentUser.UserId is null)
            return new List<ClassTeacherAssignmentResult>();

        const string userSql = @"
SELECT u.email
FROM users u
WHERE u.id = @UserId
  AND u.school_id = @SchoolId
  AND u.is_deleted = FALSE
LIMIT 1;";

        var email = await conn.ExecuteScalarAsync<string?>(
            userSql,
            new { UserId = _currentUser.UserId.Value, SchoolId = _tenantContext.SchoolId.Value });

        if (string.IsNullOrWhiteSpace(email))
            return new List<ClassTeacherAssignmentResult>();

        const string staffSql = @"
SELECT id
FROM staff
WHERE school_id = @SchoolId
  AND LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";

        var staffId = await conn.ExecuteScalarAsync<Guid?>(
            staffSql,
            new { SchoolId = _tenantContext.SchoolId.Value, Email = email });

        if (staffId == null)
            return new List<ClassTeacherAssignmentResult>();

        const string assignmentsSql = @"
SELECT
    ct.class_id AS ""ClassId"",
    ct.section_id AS ""SectionId"",
    ct.academic_session_id AS ""AcademicSessionId""
FROM class_teachers ct
WHERE ct.school_id = @SchoolId
  AND ct.teacher_id = @StaffId
  AND ct.is_deleted = FALSE;";

        return (await conn.QueryAsync<ClassTeacherAssignmentResult>(assignmentsSql, new
        {
            SchoolId = _tenantContext.SchoolId.Value,
            StaffId = staffId.Value
        }))?.ToList() ?? new List<ClassTeacherAssignmentResult>();
    }

    private static string? BuildAddressLine(string? addressLine, string? city, string? state)
    {
        var parts = new[] { addressLine?.Trim(), city?.Trim(), state?.Trim() };
        var combined = string.Join(", ", parts.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    private sealed class StudentAdmitFnResult
    {
        public Guid StudentId { get; set; }
        public string? AdmissionNo { get; set; }
        public string? FullName { get; set; }
        public Guid AcademicSessionId { get; set; }
    }

    private sealed class StudentGetByIdRow
    {
        public Guid Id { get; set; }
        public string? AdmissionNo { get; set; }
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public Guid ClassId { get; set; }
        public Guid SectionId { get; set; }
        public Guid AcademicSessionId { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string? ParentMobileNo { get; set; }
        public string? FatherName { get; set; }
        public string? MotherName { get; set; }
        public string? AddressLine { get; set; }
        public string? BloodGroup { get; set; }
        public string? AadharNo { get; set; }
        public int? RollNo { get; set; }
        public string? UdiseNo { get; set; }
        public string? FatherAadharNo { get; set; }
        public string? MotherAadharNo { get; set; }
        public string? FatherOccupation { get; set; }
        public string? MotherOccupation { get; set; }
        public string? PenNo { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankIfsc { get; set; }
        public string? BankBranch { get; set; }
    }
}

