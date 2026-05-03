using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Schools.Dtos;
using SchoolManagement.Application.Schools.Services;
using SchoolManagement.Infrastructure.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("superadmin")]
[Authorize]
public class SuperAdminController : ControllerBase
{
    private readonly ISchoolService _schoolService;
    private readonly ISchoolFeatureService _schoolFeatureService;
    private readonly ICurrentUserService _currentUser;
    private readonly SubscriptionLifecycleJob _subscriptionLifecycleJob;

    public SuperAdminController(
        ISchoolService schoolService,
        ISchoolFeatureService schoolFeatureService,
        ICurrentUserService currentUser,
        SubscriptionLifecycleJob subscriptionLifecycleJob)
    {
        _schoolService = schoolService;
        _schoolFeatureService = schoolFeatureService;
        _currentUser = currentUser;
        _subscriptionLifecycleJob = subscriptionLifecycleJob;
    }

    [HttpGet("schools")]
    [HasPermission("SuperAdmin.RegisterSchool")]
    public async Task<ActionResult<ApiResponse<PagedResult<SchoolResponseDto>>>> GetSchools([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _schoolService.GetSchoolsAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("analytics")]
    [HasPermission("SuperAdmin.ViewAnalytics")]
    public async Task<ActionResult<ApiResponse<SuperAdminDashboardSummaryDto>>> GetAnalytics()
    {
        var result = await _schoolService.GetDashboardSummaryAsync();
        return Ok(result);
    }

    [HttpPost("register-school")]
    [HasPermission("SuperAdmin.RegisterSchool")]
    public async Task<ActionResult<ApiResponse<SchoolResponseDto>>> RegisterSchool([FromBody] RegisterSchoolRequestDto request)
    {
        var result = await _schoolService.RegisterSchoolAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("schools/{schoolId:guid}/features")]
    [HasPermission("SuperAdmin.RegisterSchool")]
    public async Task<ActionResult<ApiResponse<SchoolFeaturesResponseDto>>> GetSchoolFeatures(Guid schoolId)
    {
        var result = await _schoolFeatureService.GetSchoolFeaturesForSuperAdminAsync(schoolId);
        if (!result.Success && result.ErrorCode == ErrorCodes.NotFound) return NotFound(result);
        return Ok(result);
    }

    [HttpPut("schools/{schoolId:guid}/features")]
    [HasPermission("SuperAdmin.RegisterSchool")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateSchoolFeatures(Guid schoolId, [FromBody] UpdateSchoolFeaturesRequestDto request)
    {
        if (_currentUser.UserId is null)
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", ErrorCodes.Unauthorized));
        var result = await _schoolFeatureService.UpdateSchoolFeaturesAsync(schoolId, request, _currentUser.UserId.Value);
        if (!result.Success && result.ErrorCode == ErrorCodes.NotFound) return NotFound(result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPut("schools/{schoolId:guid}")]
    [HasPermission("SuperAdmin.RegisterSchool")]
    public async Task<ActionResult<ApiResponse<SchoolResponseDto>>> UpdateSchool(Guid schoolId, [FromBody] UpdateSchoolRequestDto request)
    {
        var result = await _schoolService.UpdateSchoolAsync(schoolId, request);
        if (!result.Success && result.ErrorCode == ErrorCodes.NotFound) return NotFound(result);
        if (!result.Success && result.ErrorCode == ErrorCodes.Conflict) return Conflict(result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("subscription-plans")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<PagedResult<SubscriptionPlanResponseDto>>>> GetSubscriptionPlans([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _schoolService.GetSubscriptionPlansAsync(page, pageSize);
        return Ok(result);
    }

    [HttpPost("subscription-plans")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<SubscriptionPlanResponseDto>>> CreateSubscriptionPlan([FromBody] CreateSubscriptionPlanRequestDto request)
    {
        var result = await _schoolService.CreateSubscriptionPlanAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPut("subscription-plans/{planId:guid}")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<SubscriptionPlanResponseDto>>> UpdateSubscriptionPlan(Guid planId, [FromBody] UpdateSubscriptionPlanRequestDto request)
    {
        var result = await _schoolService.UpdateSubscriptionPlanAsync(planId, request);
        if (!result.Success && result.ErrorCode == ErrorCodes.NotFound) return NotFound(result);
        if (!result.Success && result.ErrorCode == ErrorCodes.Conflict) return Conflict(result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("schools/{schoolId:guid}/subscriptions")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<object>>> AssignSubscription(Guid schoolId, [FromBody] AssignSubscriptionRequestDto request)
    {
        var result = await _schoolService.AssignSubscriptionAsync(schoolId, request);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpGet("subscription-payments")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<PagedResult<SchoolSubscriptionPaymentResponseDto>>>> GetSubscriptionPayments([FromQuery] Guid? schoolId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _schoolService.GetSubscriptionPaymentsAsync(schoolId, page, pageSize);
        return Ok(result);
    }

    [HttpGet("expenses")]
    [HasPermission("SuperAdmin.ViewAnalytics")]
    public async Task<ActionResult<ApiResponse<PagedResult<SuperAdminExpenseResponseDto>>>> GetExpenses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] int? month = null,
        [FromQuery] int? year = null)
    {
        var result = await _schoolService.GetExpensesAsync(page, pageSize, search, month, year);
        return Ok(result);
    }

    [HttpPost("expenses")]
    [HasPermission("SuperAdmin.ViewAnalytics")]
    public async Task<ActionResult<ApiResponse<SuperAdminExpenseResponseDto>>> CreateExpense([FromBody] CreateSuperAdminExpenseRequestDto request)
    {
        var result = await _schoolService.CreateExpenseAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpGet("expenses-summary")]
    [HasPermission("SuperAdmin.ViewAnalytics")]
    public async Task<ActionResult<ApiResponse<SuperAdminExpenseSummaryDto>>> GetExpensesSummary([FromQuery] int? month = null, [FromQuery] int? year = null)
    {
        var result = await _schoolService.GetExpenseSummaryAsync(month, year);
        return Ok(result);
    }

    [HttpPost("subscription-payments")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<object>>> RecordSubscriptionPayment([FromBody] RecordSchoolSubscriptionPaymentRequestDto request)
    {
        var result = await _schoolService.RecordSubscriptionPaymentAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPost("subscription-reminders")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<object>>> SendSubscriptionReminder([FromBody] SubscriptionReminderRequestDto request)
    {
        var result = await _schoolService.SendSubscriptionReminderAsync(request);
        if (!result.Success && result.ErrorCode == ErrorCodes.NotFound) return NotFound(result);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("subscription-lifecycle/run-now")]
    [HasPermission("SuperAdmin.ManageSubscriptions")]
    public async Task<ActionResult<ApiResponse<object>>> RunSubscriptionLifecycleNow()
    {
        await _subscriptionLifecycleJob.ProcessDailyAsync();
        return Ok(ApiResponse<object>.Ok(null, "Subscription lifecycle job executed."));
    }

    [HttpPost("activate-school/{schoolId:guid}")]
    [HasPermission("SuperAdmin.ActivateSchool")]
    public async Task<ActionResult<ApiResponse<object>>> ActivateSchool(Guid schoolId)
    {
        var result = await _schoolService.ActivateSchoolAsync(schoolId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("deactivate-school/{schoolId:guid}")]
    [HasPermission("SuperAdmin.DeactivateSchool")]
    public async Task<ActionResult<ApiResponse<object>>> DeactivateSchool(Guid schoolId)
    {
        var result = await _schoolService.DeactivateSchoolAsync(schoolId);
        return StatusCode(result.Success ? 200 : 400, result);
    }

    [HttpPost("soft-delete-school/{schoolId:guid}")]
    [HasPermission("SuperAdmin.SoftDeleteSchool")]
    public async Task<ActionResult<ApiResponse<object>>> SoftDeleteSchool(Guid schoolId)
    {
        var result = await _schoolService.SoftDeleteSchoolAsync(schoolId);
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

