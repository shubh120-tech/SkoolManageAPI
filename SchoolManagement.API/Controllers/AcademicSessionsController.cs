using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Academic.Services;
using SchoolManagement.Application.Common;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("academic-sessions")]
[Authorize]
public class AcademicSessionsController : ControllerBase
{
    private readonly IAcademicSessionService _sessionService;

    public AcademicSessionsController(IAcademicSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [HttpPost]
    [HasPermission("Academic.Sessions.Manage")]
    public async Task<ActionResult<ApiResponse<AcademicSessionDto>>> Create([FromBody] CreateAcademicSessionRequestDto request)
    {
        var result = await _sessionService.CreateAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("{sessionId:guid}")]
    [HasPermission("Academic.Sessions.Manage")]
    public async Task<ActionResult<ApiResponse<AcademicSessionDto>>> Update(Guid sessionId, [FromBody] UpdateAcademicSessionRequestDto request)
    {
        var result = await _sessionService.UpdateAsync(sessionId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet]
    [HasPermission("Academic.Sessions.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AcademicSessionDto>>>> GetAll()
    {
        var result = await _sessionService.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("active")]
    [HasPermission("Academic.Sessions.View")]
    public async Task<ActionResult<ApiResponse<AcademicSessionDto?>>> GetActive()
    {
        var result = await _sessionService.GetActiveAsync();
        return Ok(result);
    }
}

