using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Academic.Dtos;
using SchoolManagement.Application.Academic.Services;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Staff.Dtos;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("academics")]
[Authorize]
public class AcademicController : ControllerBase
{
    private readonly IAcademicService _academicService;

    public AcademicController(IAcademicService academicService)
    {
        _academicService = academicService;
    }

    // Classes

    [HttpPost("classes")]
    [HasPermission("Academic.Classes.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<ClassDto>>> CreateClass([FromBody] CreateClassRequestDto request)
    {
        var result = await _academicService.CreateClassAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("classes/{classId:guid}")]
    [HasPermission("Academic.Classes.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<ClassDto>>> UpdateClass(Guid classId, [FromBody] UpdateClassRequestDto request)
    {
        var result = await _academicService.UpdateClassAsync(classId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("classes")]
    [HasPermission("Academic.Classes.View")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ClassDto>>>> GetClasses([FromQuery] Guid? academicSessionId = null)
    {
        var result = await _academicService.GetClassesAsync(academicSessionId);
        return Ok(result);
    }

    // Sections

    [HttpPost("classes/{classId:guid}/sections")]
    [HasPermission("Academic.Sections.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<SectionDto>>> CreateSection(Guid classId, [FromBody] CreateSectionRequestDto request)
    {
        request.ClassId = classId;
        var result = await _academicService.CreateSectionAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("sections/{sectionId:guid}")]
    [HasPermission("Academic.Sections.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<SectionDto>>> UpdateSection(Guid sectionId, [FromBody] UpdateSectionRequestDto request)
    {
        var result = await _academicService.UpdateSectionAsync(sectionId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("classes/{classId:guid}/sections")]
    [HasPermission("Academic.Sections.View")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SectionDto>>>> GetSectionsByClass(Guid classId, [FromQuery] Guid? academicSessionId = null)
    {
        var result = await _academicService.GetSectionsByClassAsync(classId, academicSessionId);
        return Ok(result);
    }

    // Class subjects

    [HttpGet("classes/{classId:guid}/subjects")]
    [HasPermission("Academic.Classes.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SubjectDto>>>> GetClassSubjects(Guid classId)
    {
        var result = await _academicService.GetClassSubjectsAsync(classId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("classes/{classId:guid}/subjects")]
    [HasPermission("Academic.Classes.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<object>>> SetClassSubjects(Guid classId, [FromBody] Guid[] subjectIds)
    {
        var result = await _academicService.SetClassSubjectsAsync(classId, subjectIds);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    // Class teacher assignment

    [HttpPost("classes/{classId:guid}/sections/{sectionId:guid}/class-teacher")]
    [HasPermission("Academic.Classes.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<object>>> AssignClassTeacher(
        Guid classId,
        Guid sectionId,
        [FromQuery] Guid teacherId,
        [FromQuery] Guid academicSessionId)
    {
        var result = await _academicService.AssignClassTeacherAsync(teacherId, classId, sectionId, academicSessionId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("classes/{classId:guid}/sections/{sectionId:guid}/class-teacher")]
    [HasPermission("Academic.Classes.Manage")]
    [HasPermission("Academic.ClassesSections.ManageAll")]
    public async Task<ActionResult<ApiResponse<object>>> UnassignClassTeacher(
        Guid classId,
        Guid sectionId,
        [FromQuery] Guid academicSessionId)
    {
        var result = await _academicService.UnassignClassTeacherAsync(classId, sectionId, academicSessionId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("classes/{classId:guid}/sections/{sectionId:guid}/class-teacher")]
    [HasPermission("Academic.Classes.View")]
    public async Task<ActionResult<ApiResponse<object>>> GetClassTeacher(
        Guid classId,
        Guid sectionId,
        [FromQuery] Guid academicSessionId)
    {
        var result = await _academicService.GetClassTeacherAsync(classId, sectionId, academicSessionId);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

