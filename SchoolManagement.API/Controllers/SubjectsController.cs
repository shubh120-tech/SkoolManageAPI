using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;
using SchoolManagement.Application.Staff.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("subjects")]
[Authorize]
public class SubjectsController : ControllerBase
{
    private readonly IStaffService _staffService;

    public SubjectsController(IStaffService staffService)
    {
        _staffService = staffService;
    }

    [HttpGet]
    [HasPermission("Staff.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SubjectDto>>>> GetSubjects()
    {
        var result = await _staffService.GetSubjectsAsync();
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<SubjectDto>>> CreateSubject([FromBody] CreateSubjectRequestDto request)
    {
        var result = await _staffService.CreateSubjectAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("{subjectId:guid}")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<SubjectDto>>> UpdateSubject(Guid subjectId, [FromBody] UpdateSubjectRequestDto request)
    {
        var result = await _staffService.UpdateSubjectAsync(subjectId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("{subjectId:guid}")]
    [HasPermission("Staff.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteSubject(Guid subjectId)
    {
        var result = await _staffService.SoftDeleteSubjectAsync(subjectId);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}
