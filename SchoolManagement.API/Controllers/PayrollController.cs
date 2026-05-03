using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.API.Authorization;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Payroll.Dtos;
using SchoolManagement.Application.Payroll.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("payroll")]
[Authorize]
public class PayrollController : ControllerBase
{
    private readonly IPayrollService _payrollService;

    public PayrollController(IPayrollService payrollService)
    {
        _payrollService = payrollService;
    }

    [HttpPost("generate")]
    [HasPermission("Payroll.Generate")]
    [HasPermission("Payroll.Generate.All")]
    public async Task<ActionResult<ApiResponse<object>>> GeneratePayroll([FromBody] GeneratePayrollRequestDto request)
    {
        var result = await _payrollService.GeneratePayrollAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    [HttpPost("staff-payments")]
    [HasPermission("Payroll.Pay")]
    [HasPermission("Payroll.Pay.All")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Reporting.Staff.Payroll")]
    public async Task<ActionResult<ApiResponse<object>>> RecordStaffPayment([FromBody] StaffPaymentRequestDto request)
    {
        var result = await _payrollService.RecordStaffPaymentAsync(request);
        return StatusCode(result.Success ? 201 : 400, result);
    }

    /// <summary>
    /// Paged list of recorded salary payments (staff_payments). School-wide filters for admins;
    /// staff with Payroll.View only see their own payments.
    /// </summary>
    [HttpGet("salary-payment-history")]
    [HasPermission("Reporting.Staff.Payroll")]
    [HasPermission("Payroll.History.View")]
    [HasPermission("Payroll.History.View.All")]
    [HasPermission("Payroll.Pay")]
    [HasPermission("Payroll.Pay.All")]
    [HasPermission("Staff.Manage")]
    [HasPermission("Payroll.View")]
    [HasPermission("Payroll.View.All")]
    public async Task<ActionResult<ApiResponse<StaffSalaryPaymentHistoryResultDto>>> GetSalaryPaymentHistory(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] Guid? staffId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var result = await _payrollService.GetStaffSalaryPaymentHistoryAsync(year, month, staffId, page, pageSize, search);
        if (!result.Success)
        {
            var code = result.ErrorCode == ErrorCodes.Forbidden ? 403
                : result.ErrorCode == ErrorCodes.Validation ? 400
                : 400;
            return StatusCode(code, result);
        }

        return Ok(result);
    }
}

