using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Fees.Dtos;
using SchoolManagement.Application.Fees.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("fees")]
[Authorize]
public class FeesController : ControllerBase
{
    private readonly IFeeService _feeService;
    private readonly ITenantContext _tenantContext;

    public FeesController(IFeeService feeService, ITenantContext tenantContext)
    {
        _feeService = feeService;
        _tenantContext = tenantContext;
    }

    [HttpPost("payments")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<object>>> RecordPayment([FromBody] RecordFeePaymentRequestDto request)
    {
        var result = await _feeService.RecordFeePaymentAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("payments")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<PaymentsListResponseDto>> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? studentId = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null)
    {
        if (_tenantContext.SchoolId is null)
            return BadRequest(ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule));
        var result = await _feeService.GetPaymentsAsync(_tenantContext.SchoolId.Value, page, pageSize, search, studentId, year, month);
        return Ok(result);
    }

    // Fee heads

    [HttpPost("heads")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<FeeHeadDto>>> CreateFeeHead([FromBody] CreateFeeHeadRequestDto request)
    {
        var result = await _feeService.CreateFeeHeadAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("heads/{feeHeadId:guid}")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<FeeHeadDto>>> UpdateFeeHead(Guid feeHeadId, [FromBody] UpdateFeeHeadRequestDto request)
    {
        var result = await _feeService.UpdateFeeHeadAsync(feeHeadId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("heads")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<FeeHeadDto>>>> GetFeeHeads([FromQuery] bool showInactive = false)
    {
        var result = await _feeService.GetFeeHeadsAsync(showInactive);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("heads/{feeHeadId:guid}")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> SoftDeleteFeeHead(Guid feeHeadId)
    {
        var result = await _feeService.SoftDeleteFeeHeadAsync(feeHeadId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    // Class fee structure

    [HttpPost("class-structures")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> SetClassFeeStructure([FromBody] SetClassFeeStructureRequestDto request)
    {
        var result = await _feeService.SetClassFeeStructureAsync(request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("class-structures/{classId:guid}")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> GetClassFeeStructure(Guid classId)
    {
        var result = await _feeService.GetClassFeeStructureAsync(classId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    // Student fee assignments

    [HttpPost("students/assign-from-class")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> AssignStudentFeesFromClass([FromBody] AssignStudentFeesFromClassRequestDto request)
    {
        var result = await _feeService.AssignStudentFeesFromClassAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("students/{studentId:guid}/assignments")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    public async Task<ActionResult<ApiResponse<object>>> GetStudentFeeAssignments(Guid studentId)
    {
        var result = await _feeService.GetStudentFeeAssignmentsAsync(studentId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("students/{studentId:guid}/summary")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    public async Task<ActionResult<ApiResponse<StudentFeeSummaryDto>>> GetStudentFeeSummary(Guid studentId)
    {
        var result = await _feeService.GetStudentFeeSummaryAsync(studentId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    /// <summary>Students in a class/section (session) with fee pending &gt; 0. Teachers see only assigned class/section unless they have a school-wide fee permission.</summary>
    [HttpGet("class-section/pending")]
    [HasPermission("Fees.View")]
    [HasPermission("Fees.View.All")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    [HasPermission("Fees.Payments")]
    [HasPermission("Fees.Payments.All")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClassSectionStudentFeePendingDto>>>> GetClassSectionFeePending(
        [FromQuery] Guid classId,
        [FromQuery] Guid sectionId,
        [FromQuery] Guid academicSessionId)
    {
        var result = await _feeService.GetClassSectionFeePendingAsync(classId, sectionId, academicSessionId);
        if (!result.Success && string.Equals(result.ErrorCode, ErrorCodes.Forbidden, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("students/add-optional-fee")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> AddOptionalFee([FromBody] AddOptionalFeeRequestDto request)
    {
        var result = await _feeService.AddOptionalFeeAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPost("students/add-fee")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> AddStudentFee([FromBody] AddStudentFeeRequestDto request)
    {
        var result = await _feeService.AddStudentFeeAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("students/assignments/{assignmentId:guid}/discount")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> SetFeeAssignmentDiscount(Guid assignmentId, [FromBody] SetFeeAssignmentDiscountRequestDto request)
    {
        var result = await _feeService.SetFeeAssignmentDiscountAsync(assignmentId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpDelete("students/assignments/{assignmentId:guid}")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> RemoveFeeAssignment(Guid assignmentId)
    {
        var result = await _feeService.RemoveFeeAssignmentAsync(assignmentId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPut("students/assignments/{assignmentId:guid}")]
    [HasPermission("Fees.Manage")]
    [HasPermission("Fees.Manage.All")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateFeeAssignment(Guid assignmentId, [FromBody] UpdateFeeAssignmentRequestDto request)
    {
        var result = await _feeService.UpdateFeeAssignmentAsync(assignmentId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

