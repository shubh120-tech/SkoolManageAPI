using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Academic.Services;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;

namespace SchoolManagement.Infrastructure.Services;

public class AcademicService : IAcademicService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public AcademicService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<ClassDto>> CreateClassAsync(CreateClassRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<ClassDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_academic_session_id", request.AcademicSessionId);
        p.Add("p_name", request.Name);
        p.Add("p_capacity", request.Capacity);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_create(@p_school_id,@p_academic_session_id,@p_name,@p_capacity,@p_created_by)", p);

        var dto = new ClassDto
        {
            Id = Guid.Empty,
            Name = request.Name,
            Capacity = request.Capacity,
            AcademicSessionId = request.AcademicSessionId
        };

        return ApiResponse<ClassDto>.Ok(dto, "Class created.");
    }

    public async Task<ApiResponse<ClassDto>> UpdateClassAsync(Guid classId, UpdateClassRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<ClassDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);
        p.Add("p_academic_session_id", request.AcademicSessionId);
        p.Add("p_name", request.Name);
        p.Add("p_capacity", request.Capacity);
        p.Add("p_updated_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_update(@p_school_id,@p_class_id,@p_academic_session_id,@p_name,@p_capacity,@p_updated_by)", p);

        var dto = new ClassDto
        {
            Id = classId,
            Name = request.Name,
            Capacity = request.Capacity,
            AcademicSessionId = request.AcademicSessionId
        };

        return ApiResponse<ClassDto>.Ok(dto, "Class updated.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<ClassDto>>> GetClassesAsync(Guid? academicSessionId = null)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<ClassDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);

        Guid sessionId;
        if (academicSessionId.HasValue)
        {
            sessionId = academicSessionId.Value;
        }
        else
        {
            var activeJson = await conn.ExecuteScalarAsync<string>("SELECT fn_academic_session_get_active(@p_school_id)", p);
            if (string.IsNullOrWhiteSpace(activeJson))
                return ApiResponse<IReadOnlyCollection<ClassDto>>.Ok(Array.Empty<ClassDto>(), "No active academic session.");
            using (var docActive = JsonDocument.Parse(activeJson))
                sessionId = docActive.RootElement.GetProperty("Id").GetGuid();
        }

        p.Add("p_academic_session_id", sessionId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_classes_get_all(@p_school_id,@p_academic_session_id)", p);
        var items = new List<ClassDto>();

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(new ClassDto
                {
                    Id = item.GetProperty("Id").GetGuid(),
                    Name = item.GetProperty("Name").GetString() ?? string.Empty,
                    Capacity = item.GetProperty("Capacity").GetInt32(),
                    AcademicSessionId = sessionId
                });
            }
        }

        return ApiResponse<IReadOnlyCollection<ClassDto>>.Ok(items);
    }

    public async Task<ApiResponse<SectionDto>> CreateSectionAsync(CreateSectionRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<SectionDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_name", request.Name);
        p.Add("p_capacity", request.Capacity);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_section_create(@p_school_id,@p_class_id,@p_name,@p_capacity,@p_created_by)", p);

        var dto = new SectionDto
        {
            Id = Guid.Empty,
            ClassId = request.ClassId,
            Name = request.Name,
            Capacity = request.Capacity
        };

        return ApiResponse<SectionDto>.Ok(dto, "Section created.");
    }

    public async Task<ApiResponse<SectionDto>> UpdateSectionAsync(Guid sectionId, UpdateSectionRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<SectionDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_section_id", sectionId);
        p.Add("p_name", request.Name);
        p.Add("p_capacity", request.Capacity);
        p.Add("p_updated_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_section_update(@p_school_id,@p_section_id,@p_name,@p_capacity,@p_updated_by)", p);

        var dto = new SectionDto
        {
            Id = sectionId,
            Name = request.Name,
            Capacity = request.Capacity
        };

        return ApiResponse<SectionDto>.Ok(dto, "Section updated.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<SectionDto>>> GetSectionsByClassAsync(Guid classId, Guid? academicSessionId = null)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<SectionDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);

        Guid sessionId;
        if (academicSessionId.HasValue)
        {
            sessionId = academicSessionId.Value;
        }
        else
        {
            var activeJson = await conn.ExecuteScalarAsync<string>("SELECT fn_academic_session_get_active(@p_school_id)", p);
            if (string.IsNullOrWhiteSpace(activeJson))
                return ApiResponse<IReadOnlyCollection<SectionDto>>.Ok(Array.Empty<SectionDto>(), "No active academic session.");
            using (var docActive = JsonDocument.Parse(activeJson))
                sessionId = docActive.RootElement.GetProperty("Id").GetGuid();
        }

        p.Add("p_academic_session_id", sessionId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_sections_get_by_class(@p_school_id,@p_class_id,@p_academic_session_id)", p);
        var items = new List<SectionDto>();

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(new SectionDto
                {
                    Id = item.GetProperty("Id").GetGuid(),
                    ClassId = item.GetProperty("ClassId").GetGuid(),
                    Name = item.GetProperty("Name").GetString() ?? string.Empty,
                    Capacity = item.GetProperty("Capacity").GetInt32()
                });
            }
        }

        return ApiResponse<IReadOnlyCollection<SectionDto>>.Ok(items);
    }

    public async Task<ApiResponse<object>> AssignClassTeacherAsync(Guid teacherId, Guid classId, Guid sectionId, Guid academicSessionId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_teacher_id", teacherId);
        p.Add("p_class_id", classId);
        p.Add("p_section_id", sectionId);
        p.Add("p_academic_session_id", academicSessionId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_teacher_assign(@p_school_id,@p_teacher_id,@p_class_id,@p_section_id,@p_academic_session_id,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Class teacher assigned.");
    }

    public async Task<ApiResponse<object>> UnassignClassTeacherAsync(Guid classId, Guid sectionId, Guid academicSessionId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);
        p.Add("p_section_id", sectionId);
        p.Add("p_academic_session_id", academicSessionId);
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_teacher_unassign(@p_school_id,@p_class_id,@p_section_id,@p_academic_session_id,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Class teacher unassigned.");
    }

    public async Task<ApiResponse<object>> GetClassTeacherAsync(Guid classId, Guid sectionId, Guid academicSessionId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);
        p.Add("p_section_id", sectionId);
        p.Add("p_academic_session_id", academicSessionId);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_class_teacher_get(@p_school_id,@p_class_id,@p_section_id,@p_academic_session_id)",
            p);

        // Directly return the JSON as dynamic data; middleware/wrapper already handles the outer shape.
        var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<object>(json);

        return ApiResponse<object>.Ok(data, "Class teacher fetched.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<SubjectDto>>> GetClassSubjectsAsync(Guid classId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<SubjectDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_class_subjects_get(@p_school_id,@p_class_id)", p);
        var items = new List<SubjectDto>();

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(new SubjectDto
                {
                    Id = item.GetProperty("Id").GetGuid(),
                    Name = item.GetProperty("Name").GetString() ?? string.Empty,
                    Code = item.TryGetProperty("Code", out var codeProp) && codeProp.ValueKind != JsonValueKind.Null
                        ? codeProp.GetString()
                        : null
                });
            }
        }

        return ApiResponse<IReadOnlyCollection<SubjectDto>>.Ok(items, "Class subjects fetched.");
    }

    public async Task<ApiResponse<object>> SetClassSubjectsAsync(Guid classId, Guid[] subjectIds)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);
        p.Add("p_subject_ids", subjectIds ?? Array.Empty<Guid>());
        p.Add("p_user_id", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_class_subjects_set(@p_school_id,@p_class_id,@p_subject_ids,@p_user_id)", p);

        return ApiResponse<object>.Ok(null, "Class subjects updated.");
    }
}

