using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Notifications.Dtos;
using SchoolManagement.Application.Notifications.Services;

namespace SchoolManagement.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        ILogger<NotificationService> logger)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task CreateAsync(Guid userId, Guid? schoolId, string title, string message, Guid createdBy, string? linkUrl = null)
    {
        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"
INSERT INTO notifications(
    id, school_id, is_deleted, created_at, created_by,
    user_id, title, message, is_read, link_url)
VALUES (
    md5(random()::text || clock_timestamp()::text)::uuid, @SchoolId, FALSE, NOW(), @CreatedBy,
    @UserId, @Title, @Message, FALSE, @LinkUrl)";
            await conn.ExecuteAsync(sql, new
            {
                SchoolId = schoolId,
                UserId = userId,
                Title = title,
                Message = message,
                CreatedBy = createdBy,
                LinkUrl = linkUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to insert in-app notification for user {UserId}", userId);
        }
    }

    public async Task<ApiResponse<NotificationListResponseDto>> GetMyNotificationsAsync(int page, int pageSize, bool unreadOnly)
    {
        if (_currentUser.UserId is null)
            return ApiResponse<NotificationListResponseDto>.Fail("User context missing.", ErrorCodes.BusinessRule);

        var p = Math.Max(1, page);
        var ps = Math.Max(1, Math.Min(pageSize, 100));
        var offset = (p - 1) * ps;

        using var conn = await _connectionFactory.CreateConnectionAsync();

        const string whereSql = @"
WHERE n.is_deleted = FALSE
  AND n.user_id = @UserId
  AND (
    (@SchoolId IS NULL AND n.school_id IS NULL)
    OR (n.school_id = @SchoolId)
  )
  AND (@UnreadOnly = FALSE OR n.is_read = FALSE)";

        var countSql = $"SELECT COUNT(*)::bigint FROM notifications n {whereSql}";
        var total = await conn.ExecuteScalarAsync<long>(countSql, new
        {
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId,
            UnreadOnly = unreadOnly
        });

        var listSql = $@"
SELECT n.id AS Id,
       n.title AS Title,
       n.message AS Message,
       n.is_read AS IsRead,
       n.created_at AS CreatedAt,
       n.link_url AS LinkUrl
FROM notifications n
{whereSql}
ORDER BY n.created_at DESC
LIMIT @Limit OFFSET @Offset";

        var items = (await conn.QueryAsync<NotificationItemDto>(listSql, new
        {
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId,
            UnreadOnly = unreadOnly,
            Limit = ps,
            Offset = offset
        })).AsList();

        return ApiResponse<NotificationListResponseDto>.Ok(new NotificationListResponseDto
        {
            Items = items,
            TotalCount = total
        });
    }

    public async Task<ApiResponse<long>> GetUnreadCountAsync()
    {
        if (_currentUser.UserId is null)
            return ApiResponse<long>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
SELECT COUNT(*)::bigint
FROM notifications n
WHERE n.is_deleted = FALSE
  AND n.user_id = @UserId
  AND n.is_read = FALSE
  AND (
    (@SchoolId IS NULL AND n.school_id IS NULL)
    OR (n.school_id = @SchoolId)
  )";
        var c = await conn.ExecuteScalarAsync<long>(sql, new
        {
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId
        });
        return ApiResponse<long>.Ok(c);
    }

    public async Task<ApiResponse<object>> MarkReadAsync(Guid notificationId)
    {
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE notifications
SET is_read = TRUE,
    updated_at = NOW(),
    updated_by = @UserId
WHERE id = @Id
  AND user_id = @UserId
  AND is_deleted = FALSE
  AND (
    (@SchoolId IS NULL AND school_id IS NULL)
    OR (school_id = @SchoolId)
  )";
        var rows = await conn.ExecuteAsync(sql, new
        {
            Id = notificationId,
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId
        });
        if (rows == 0)
            return ApiResponse<object>.Fail("Notification not found.", ErrorCodes.NotFound);
        return ApiResponse<object>.Ok(null, "Marked as read.");
    }

    public async Task<ApiResponse<object>> MarkAllReadAsync()
    {
        if (_currentUser.UserId is null)
            return ApiResponse<object>.Fail("User context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        const string sql = @"
UPDATE notifications
SET is_read = TRUE,
    updated_at = NOW(),
    updated_by = @UserId
WHERE user_id = @UserId
  AND is_deleted = FALSE
  AND is_read = FALSE
  AND (
    (@SchoolId IS NULL AND school_id IS NULL)
    OR (school_id = @SchoolId)
  )";
        await conn.ExecuteAsync(sql, new
        {
            UserId = _currentUser.UserId.Value,
            SchoolId = _tenantContext.SchoolId
        });
        return ApiResponse<object>.Ok(null, "All marked as read.");
    }
}
