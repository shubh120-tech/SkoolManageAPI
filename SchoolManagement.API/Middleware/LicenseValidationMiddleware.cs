using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Infrastructure.MultiTenancy;

namespace SchoolManagement.API.Middleware;

public class LicenseValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LicenseValidationMiddleware> _logger;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TenantContext _tenantContext;

    public LicenseValidationMiddleware(
        RequestDelegate next,
        ILogger<LicenseValidationMiddleware> logger,
        IDbConnectionFactory connectionFactory,
        TenantContext tenantContext)
    {
        _next = next;
        _logger = logger;
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/superadmin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (_tenantContext.SchoolId is null)
        {
            await _next(context);
            return;
        }

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        var active = await conn.ExecuteScalarAsync<bool>("SELECT fn_school_check_license(@p_school_id)", p);

        if (!active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""
                {
                  "success": false,
                  "message": "School license expired.",
                  "data": null,
                  "errorCode": "FORBIDDEN"
                }
                """);
            return;
        }

        await _next(context);
    }
}

