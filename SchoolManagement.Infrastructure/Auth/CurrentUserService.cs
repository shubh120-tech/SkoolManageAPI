using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolManagement.Application.Abstractions;

namespace SchoolManagement.Infrastructure.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            var claim = principal?.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && Guid.TryParse(claim.Value, out var guid) ? guid : null;
        }
    }

    public Guid? SchoolId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            var claim = principal?.FindFirst("school_id");
            return claim != null && Guid.TryParse(claim.Value, out var guid) ? guid : null;
        }
    }

    public string? Role
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            return principal?.FindFirst(ClaimTypes.Role)?.Value;
        }
    }

    public string? SchoolCode
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            return principal?.FindFirst("school_code")?.Value;
        }
    }

    public IReadOnlyCollection<string> Permissions =>
        _httpContextAccessor.HttpContext?.User?
            .FindAll("perm")
            .Select(c => c.Value)
            .ToArray()
        ?? Array.Empty<string>();
}

