using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Academic.Services;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Infrastructure.Services;

public class AcademicSessionService : IAcademicSessionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public AcademicSessionService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<AcademicSessionDto>> CreateAsync(CreateAcademicSessionRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<AcademicSessionDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var p = new DynamicParameters();
            p.Add("p_school_id", _tenantContext.SchoolId);
            p.Add("p_name", request.Name);
            p.Add("p_start_date", request.StartDate);
            p.Add("p_end_date", request.EndDate);
            p.Add("p_is_active", request.IsActive);
            p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

            await conn.ExecuteAsync(
                "CALL sp_academic_session_create(@p_school_id,@p_name,@p_start_date,@p_end_date,@p_is_active,@p_created_by)",
                p,
                tx);

            var newId = await conn.QuerySingleAsync<Guid>(
                @"SELECT id FROM academic_sessions
                  WHERE school_id = @SchoolId
                    AND LOWER(TRIM(name)) = LOWER(TRIM(@Name))
                    AND is_deleted = FALSE
                  ORDER BY created_at DESC
                  LIMIT 1",
                new { SchoolId = _tenantContext.SchoolId.Value, Name = request.Name },
                tx);

            var copyFrom = request.CopyClassStructureFromSessionId;
            if (copyFrom is { } src && src != Guid.Empty)
            {
                if (src == newId)
                {
                    tx.Rollback();
                    return ApiResponse<AcademicSessionDto>.Fail(
                        "Cannot copy structure from the same session.",
                        ErrorCodes.BusinessRule);
                }

                var copyP = new DynamicParameters();
                copyP.Add("p_school_id", _tenantContext.SchoolId);
                copyP.Add("p_from_session_id", src);
                copyP.Add("p_to_session_id", newId);
                copyP.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

                await conn.ExecuteAsync(
                    "CALL sp_copy_classes_sections_from_session(@p_school_id,@p_from_session_id,@p_to_session_id,@p_created_by)",
                    copyP,
                    tx);
            }

            tx.Commit();

            var dto = new AcademicSessionDto
            {
                Id = newId,
                Name = request.Name,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsActive = request.IsActive
            };

            var msg = copyFrom is { } c && c != Guid.Empty
                ? "Academic session created; classes, sections, and class fee structures were copied from the selected session."
                : "Academic session created.";

            return ApiResponse<AcademicSessionDto>.Ok(dto, msg);
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
                /* ignore rollback errors */
            }

            throw;
        }
    }

    public async Task<ApiResponse<AcademicSessionDto>> UpdateAsync(Guid sessionId, UpdateAcademicSessionRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<AcademicSessionDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_session_id", sessionId);
        p.Add("p_name", request.Name);
        p.Add("p_start_date", request.StartDate);
        p.Add("p_end_date", request.EndDate);
        p.Add("p_is_active", request.IsActive);
        p.Add("p_updated_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync(
            "CALL sp_academic_session_update(@p_school_id,@p_session_id,@p_name,@p_start_date,@p_end_date,@p_is_active,@p_updated_by)", p);

        var dto = new AcademicSessionDto
        {
            Id = sessionId,
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive
        };

        return ApiResponse<AcademicSessionDto>.Ok(dto, "Academic session updated.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<AcademicSessionDto>>> GetAllAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<AcademicSessionDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_academic_sessions_get_all(@p_school_id)", p);
        var list = new List<AcademicSessionDto>();

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                list.Add(new AcademicSessionDto
                {
                    Id = e.GetProperty("Id").GetGuid(),
                    Name = e.GetProperty("Name").GetString() ?? string.Empty,
                    StartDate = e.GetProperty("StartDate").GetDateTime(),
                    EndDate = e.GetProperty("EndDate").GetDateTime(),
                    IsActive = e.GetProperty("IsActive").GetBoolean()
                });
            }
        }

        return ApiResponse<IReadOnlyCollection<AcademicSessionDto>>.Ok(list);
    }

    public async Task<ApiResponse<AcademicSessionDto?>> GetActiveAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<AcademicSessionDto?>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_academic_session_get_active(@p_school_id)", p);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<AcademicSessionDto?>.Ok(null);

        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;

        var dto = new AcademicSessionDto
        {
            Id = e.GetProperty("Id").GetGuid(),
            Name = e.GetProperty("Name").GetString() ?? string.Empty,
            StartDate = e.GetProperty("StartDate").GetDateTime(),
            EndDate = e.GetProperty("EndDate").GetDateTime(),
            IsActive = e.GetProperty("IsActive").GetBoolean()
        };

        return ApiResponse<AcademicSessionDto?>.Ok(dto);
    }
}

