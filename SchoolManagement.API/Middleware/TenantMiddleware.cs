using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchoolManagement.Infrastructure.MultiTenancy;

namespace SchoolManagement.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;
    private readonly TenantContext _tenantContext;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger, TenantContext tenantContext)
    {
        _next = next;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var schoolIdStr = context.User.FindFirst("school_id")?.Value;
        var schoolCode = context.User.FindFirst("school_code")?.Value;

        if (Guid.TryParse(schoolIdStr, out var sid))
        {
            _tenantContext.SchoolId = sid;
        }

        if (!string.IsNullOrWhiteSpace(schoolCode))
        {
            _tenantContext.SchoolCode = schoolCode;
        }

        await _next(context);
    }
}

