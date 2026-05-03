using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Timetable.Dtos;
using SchoolManagement.Application.Timetable.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("timetable")]
[Authorize]
public class TimetableController : ControllerBase
{
    private readonly ITimetableService _timetableService;

    public TimetableController(ITimetableService timetableService)
    {
        _timetableService = timetableService;
    }

    [HttpPost]
    [HasPermission("Timetable.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> UpsertTimetable([FromBody] TimetableEntryDto request)
    {
        var result = await _timetableService.UpsertTimetableEntryAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpDelete("{timetableId:guid}")]
    [HasPermission("Timetable.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTimetable(Guid timetableId)
    {
        var result = await _timetableService.DeleteTimetableEntryAsync(timetableId);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

