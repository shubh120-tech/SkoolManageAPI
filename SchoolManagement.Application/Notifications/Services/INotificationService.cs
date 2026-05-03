using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Notifications.Dtos;

namespace SchoolManagement.Application.Notifications.Services;

public interface INotificationService
{
    Task<ApiResponse<NotificationListResponseDto>> GetMyNotificationsAsync(int page, int pageSize, bool unreadOnly);

    Task<ApiResponse<long>> GetUnreadCountAsync();

    Task<ApiResponse<object>> MarkReadAsync(Guid notificationId);

    Task<ApiResponse<object>> MarkAllReadAsync();

    /// <summary>Creates an in-app notification row (best-effort).</summary>
    Task CreateAsync(Guid userId, Guid? schoolId, string title, string message, Guid createdBy, string? linkUrl = null);
}
