using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Notifications.Dtos;

public class NotificationItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? LinkUrl { get; set; }
}

public class NotificationListResponseDto
{
    public IReadOnlyCollection<NotificationItemDto> Items { get; set; } = Array.Empty<NotificationItemDto>();
    public long TotalCount { get; set; }
}
