using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using SchoolManagement.API.Authorization;

namespace SchoolManagement.API.Middleware;

public class RbacPermissionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RbacPermissionMiddleware> _logger;

    public RbacPermissionMiddleware(RequestDelegate next, ILogger<RbacPermissionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        var permAttrs = endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>();
        if (permAttrs != null && permAttrs.Count > 0)
        {
            // Treat multiple [HasPermission] attributes as OR (any permission grants access).
            var requiredPermissions = permAttrs
                .Select(a => a.Permission)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToArray();

            var hasPermission = context.User.Claims
                .Where(c => c.Type == "perm")
                .Any(c => requiredPermissions.Contains(c.Value));

            if (!hasPermission)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""
                    {
                      "success": false,
                      "message": "Forbidden: missing permission",
                      "data": null,
                      "errorCode": "FORBIDDEN"
                    }
                    """);
                return;
            }
        }

        await _next(context);
    }
}

