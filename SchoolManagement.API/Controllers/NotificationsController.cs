using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Notifications.Dtos;
using SchoolManagement.Application.Notifications.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<NotificationListResponseDto>>> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false)
    {
        var result = await _notificationService.GetMyNotificationsAsync(page, pageSize, unreadOnly);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<long>>> GetUnreadCount()
    {
        var result = await _notificationService.GetUnreadCountAsync();
        return Ok(result);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkRead(Guid id)
    {
        var result = await _notificationService.MarkReadAsync(id);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllRead()
    {
        var result = await _notificationService.MarkAllReadAsync();
        return Ok(result);
    }
}
