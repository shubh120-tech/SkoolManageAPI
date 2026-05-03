using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Communication.Dtos;
using SchoolManagement.Application.Communication.Services;
using SchoolManagement.Application.Notifications.Services;

namespace SchoolManagement.Infrastructure.Services;

public class CommunicationService : ICommunicationService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;

    public CommunicationService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        INotificationService notifications)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<ApiResponse<object>> CreateAnnouncementAsync(AnnouncementRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_title", request.Title);
        p.Add("p_message", request.Message);
        p.Add("p_valid_from", request.ValidFrom);
        p.Add("p_valid_to", request.ValidTo);
        p.Add("p_for_students", request.ForStudents);
        p.Add("p_for_staff", request.ForStaff);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync("CALL sp_announcement_create(@p_school_id,@p_title,@p_message,@p_valid_from,@p_valid_to,@p_for_students,@p_for_staff,@p_created_by)", p);

        if (request.ForStudents || request.ForStaff)
        {
            var notifTitle = $"Announcement: {request.Title}";
            var preview = request.Message.Length > 400
                ? request.Message.Substring(0, 400) + "…"
                : request.Message;
            var createdBy = _currentUser.UserId ?? Guid.Empty;

            var rows = (await conn.QueryAsync<(Guid UserId, string RoleName)>(@"
SELECT u.id AS UserId, r.name AS RoleName
FROM users u
JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
WHERE u.school_id = @SchoolId AND u.is_deleted = FALSE
  AND (
    (@ForStaff AND r.name IN ('SchoolAdmin', 'Principal', 'Teacher', 'Staff'))
    OR (@ForStudents AND r.name = 'Student')
  )",
                new
                {
                    SchoolId = _tenantContext.SchoolId.Value,
                    ForStaff = request.ForStaff,
                    ForStudents = request.ForStudents
                })).ToList();

            foreach (var row in rows.DistinctBy(x => x.UserId))
            {
                var link = string.Equals(row.RoleName, "Student", StringComparison.OrdinalIgnoreCase)
                    ? "/student/announcements"
                    : "/communication";
                await _notifications.CreateAsync(row.UserId, _tenantContext.SchoolId, notifTitle, preview, createdBy, link);
            }
        }

        return ApiResponse<object>.Ok(null, "Announcement created.");
    }

    public async Task<ApiResponse<object>> UpdateAnnouncementAsync(Guid id, AnnouncementRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE announcements
SET title = @Title,
    message = @Message,
    valid_from = @ValidFrom,
    valid_to = @ValidTo,
    for_students = @ForStudents,
    for_staff = @ForStaff,
    updated_at = NOW(),
    updated_by = @UserId
WHERE id = @Id
  AND school_id = @SchoolId
  AND is_deleted = FALSE;";

        var rows = await conn.ExecuteAsync(sql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            Title = request.Title,
            Message = request.Message,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            ForStudents = request.ForStudents,
            ForStaff = request.ForStaff,
            UserId = _currentUser.UserId ?? Guid.Empty
        });

        if (rows == 0)
            return ApiResponse<object>.Fail("Announcement not found.", ErrorCodes.NotFound);

        return ApiResponse<object>.Ok(null, "Announcement updated.");
    }

    public async Task<ApiResponse<object>> DeleteAnnouncementAsync(Guid id)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE announcements
SET is_deleted = TRUE,
    updated_at = NOW(),
    updated_by = @UserId
WHERE id = @Id
  AND school_id = @SchoolId
  AND is_deleted = FALSE;";

        var rows = await conn.ExecuteAsync(sql, new
        {
            Id = id,
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId ?? Guid.Empty
        });

        if (rows == 0)
            return ApiResponse<object>.Fail("Announcement not found.", ErrorCodes.NotFound);

        return ApiResponse<object>.Ok(null, "Announcement deleted.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<AnnouncementItemDto>>> GetAnnouncementsAsync(
        bool? forStudents,
        bool? forStaff,
        bool includeExpired,
        int limit)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<AnnouncementItemDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();

        // Soft-delete expired announcements so they no longer appear anywhere.
        const string cleanupSql = @"
UPDATE announcements
SET is_deleted = TRUE,
    updated_at = NOW(),
    updated_by = COALESCE(@UserId, updated_by)
WHERE school_id = @SchoolId
  AND is_deleted = FALSE
  AND valid_to < @Now;";

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(cleanupSql, new
        {
            SchoolId = _tenantContext.SchoolId,
            UserId = _currentUser.UserId,
            Now = now
        });

        const string sql = @"
SELECT
    id AS ""Id"",
    title AS ""Title"",
    message AS ""Message"",
    valid_from AS ""ValidFrom"",
    valid_to AS ""ValidTo"",
    for_students AS ""ForStudents"",
    for_staff AS ""ForStaff""
FROM announcements
WHERE school_id = @SchoolId
  AND is_deleted = FALSE
  AND (@ForStudents IS NULL OR for_students = @ForStudents)
  AND (@ForStaff IS NULL OR for_staff = @ForStaff)
  AND (@IncludeExpired = TRUE OR (valid_from <= @Now AND valid_to >= @Now))
ORDER BY valid_from DESC
LIMIT @Limit;";

        var items = (await conn.QueryAsync<AnnouncementItemDto>(sql, new
        {
            SchoolId = _tenantContext.SchoolId,
            ForStudents = forStudents,
            ForStaff = forStaff,
            IncludeExpired = includeExpired,
            Now = now,
            Limit = limit <= 0 ? 10 : limit
        })).AsList();

        return ApiResponse<IReadOnlyCollection<AnnouncementItemDto>>.Ok(items, "Announcements fetched.");
    }
}

