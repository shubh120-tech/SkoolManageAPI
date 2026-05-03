using System;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Timetable.Dtos;
using SchoolManagement.Application.Timetable.Services;

namespace SchoolManagement.Infrastructure.Services;

public class TimetableService : ITimetableService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public TimetableService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<object>> UpsertTimetableEntryAsync(TimetableEntryDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", request.ClassId);
        p.Add("p_section_id", request.SectionId);
        p.Add("p_subject_id", request.SubjectId);
        p.Add("p_teacher_id", request.TeacherId);
        p.Add("p_day_of_week", request.DayOfWeek);
        p.Add("p_start_time", request.StartTime);
        p.Add("p_end_time", request.EndTime);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_timetable_upsert(@p_school_id,@p_class_id,@p_section_id,@p_subject_id,@p_teacher_id,@p_day_of_week,@p_start_time,@p_end_time,@p_created_by)", p);

        return ApiResponse<object>.Ok(null, "Timetable entry saved.");
    }

    public async Task<ApiResponse<object>> DeleteTimetableEntryAsync(Guid timetableId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_timetable_id", timetableId);
        p.Add("p_deleted_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_timetable_delete(@p_school_id,@p_timetable_id,@p_deleted_by)", p);

        return ApiResponse<object>.Ok(null, "Timetable entry deleted.");
    }
}

