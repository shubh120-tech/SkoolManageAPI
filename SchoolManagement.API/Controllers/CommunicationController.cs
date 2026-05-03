using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Communication.Dtos;
using SchoolManagement.Application.Communication.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("communication")]
[Authorize]
public class CommunicationController : ControllerBase
{
    private readonly ICommunicationService _communicationService;

    public CommunicationController(ICommunicationService communicationService)
    {
        _communicationService = communicationService;
    }

    [HttpPost("announcements")]
    [HasPermission("Communication.Announcements.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> CreateAnnouncement([FromBody] AnnouncementRequestDto request)
    {
        var result = await _communicationService.CreateAnnouncementAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("announcements")]
    [HasPermission("Communication.Announcements.View")]
    [HasPermission("Communication.Announcements.Manage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AnnouncementItemDto>>>> GetAnnouncements(
        [FromQuery] bool? forStudents = null,
        [FromQuery] bool? forStaff = null,
        [FromQuery] bool includeExpired = false,
        [FromQuery] int limit = 10)
    {
        var result = await _communicationService.GetAnnouncementsAsync(forStudents, forStaff, includeExpired, limit);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPut("announcements/{id:guid}")]
    [HasPermission("Communication.Announcements.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateAnnouncement(Guid id, [FromBody] AnnouncementRequestDto request)
    {
        var result = await _communicationService.UpdateAnnouncementAsync(id, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("announcements/{id:guid}")]
    [HasPermission("Communication.Announcements.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAnnouncement(Guid id)
    {
        var result = await _communicationService.DeleteAnnouncementAsync(id);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

