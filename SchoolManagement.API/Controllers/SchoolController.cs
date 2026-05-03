using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools.Dtos;
using SchoolManagement.Application.Schools.Services;

namespace SchoolManagement.API.Controllers;

[ApiController]
[Route("school")]
[Authorize]
public class SchoolController : ControllerBase
{
    private readonly ISchoolService _schoolService;

    public SchoolController(ISchoolService schoolService)
    {
        _schoolService = schoolService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<SchoolResponseDto>>> GetCurrentSchool()
    {
        var result = await _schoolService.GetCurrentSchoolAsync();
        return StatusCode(result.Success ? 200 : 400, result);
    }
}

