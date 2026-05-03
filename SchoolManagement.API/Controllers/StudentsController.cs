using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Students.Dtos;
using SchoolManagement.Application.Students.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("students")]
[Authorize]
public class StudentsController : ControllerBase
{
    private readonly IStudentService _studentService;

    public StudentsController(IStudentService studentService)
    {
        _studentService = studentService;
    }

    [HttpGet]
    [HasPermission("Student.View")]
    [HasPermission("Student.View.All")]
    [HasPermission("Reporting.Students.View")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<StudentResponseDto>>>> GetStudents(
        [FromQuery] Guid academicSessionId,
        [FromQuery] bool showInactive = false)
    {
        var result = await _studentService.GetStudentsAsync(academicSessionId, showInactive);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("by-class-section")]
    [HasPermission("Student.View")]
    [HasPermission("Student.View.All")]
    [HasPermission("Student.Promote")]
    [HasPermission("Student.Promote.All")]
    [HasPermission("Reporting.Students.View")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<StudentResponseDto>>>> GetStudentsByClassAndSection(
        [FromQuery] Guid classId,
        [FromQuery] Guid sectionId,
        [FromQuery] Guid? academicSessionId = null,
        [FromQuery] bool showInactive = false)
    {
        var result = await _studentService.GetStudentsByClassAndSectionAsync(classId, sectionId, academicSessionId, showInactive);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("{studentId:guid}")]
    [HasPermission("Student.View")]
    [HasPermission("Student.View.All")]
    [HasPermission("Reporting.Students.View")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<StudentResponseDto>>> GetStudent(
        Guid studentId,
        [FromQuery] bool showInactive = false)
    {
        var result = await _studentService.GetStudentByIdAsync(studentId, showInactive);
        var status = result.Success ? 200 : (string.Equals(result.ErrorCode, ErrorCodes.NotFound, StringComparison.Ordinal) ? 404 : 400);
        return StatusCode(status, result);
    }

    [HttpPost]
    [HasPermission("Student.Admit")]
    [HasPermission("Student.Admit.All")]
    public async Task<ActionResult<ApiResponse<StudentResponseDto>>> AdmitStudent([FromBody] CreateStudentRequestDto request)
    {
        var result = await _studentService.AdmitStudentAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPost("{studentId:guid}/promote")]
    [HasPermission("Student.Promote")]
    [HasPermission("Student.Promote.All")]
    [HasPermission("Student.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> PromoteStudent(Guid studentId, [FromBody] PromoteStudentRequestDto request)
    {
        var result = await _studentService.PromoteStudentAsync(studentId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPut("{studentId:guid}")]
    [HasPermission("Student.Manage")]
    [HasPermission("Student.Update")]
    [HasPermission("Student.Update.All")]
    public async Task<ActionResult<ApiResponse<StudentResponseDto>>> UpdateStudent(Guid studentId, [FromBody] UpdateStudentRequestDto request)
    {
        var result = await _studentService.UpdateStudentAsync(studentId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("{studentId:guid}")]
    [HasPermission("Student.Manage")]
    [HasPermission("Student.Delete")]
    public async Task<ActionResult<ApiResponse<object>>> SoftDeleteStudent(Guid studentId)
    {
        var result = await _studentService.SoftDeleteStudentAsync(studentId);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

