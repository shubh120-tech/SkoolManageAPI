using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;

namespace SchoolManagement.API.Authorization;

public sealed class SuperAdminHangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string _signingKey;
    private readonly string? _issuer;
    private readonly string? _audience;
    private const string CookieName = "hangfire_access_token";

    public SuperAdminHangfireDashboardAuthorizationFilter(string signingKey, string? issuer, string? audience)
    {
        _signingKey = signingKey;
        _issuer = issuer;
        _audience = audience;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (IsSuperAdmin(httpContext.User))
            return true;

        var tokenFromQuery = httpContext.Request.Query["access_token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tokenFromQuery) && TryValidateJwt(tokenFromQuery!, out var queryPrincipal))
        {
            httpContext.User = queryPrincipal;
            // Persist authorization so Hangfire's subsequent requests (assets, job tables)
            // don't need `?access_token=` in the URL.
            httpContext.Response.Cookies.Append(
                CookieName,
                tokenFromQuery!,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/hangfire",
                    Secure = httpContext.Request.IsHttps
                });
            return IsSuperAdmin(queryPrincipal);
        }

        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(token) && TryValidateJwt(token, out var headerPrincipal))
            {
                httpContext.User = headerPrincipal;
                httpContext.Response.Cookies.Append(
                    CookieName,
                    token,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/hangfire",
                        Secure = httpContext.Request.IsHttps
                    });
                return IsSuperAdmin(headerPrincipal);
            }
        }

        // Cookie fallback for subsequent requests after the initial page load.
        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken)
            && TryValidateJwt(cookieToken, out var cookiePrincipal))
        {
            httpContext.User = cookiePrincipal;
            return IsSuperAdmin(cookiePrincipal);
        }

        return false;
    }

    private bool TryValidateJwt(string token, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(_issuer),
                ValidIssuer = _issuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(_audience),
                ValidAudience = _audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            principal = tokenHandler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSuperAdmin(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role");
        if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return true;

        return principal.Claims.Any(c =>
            c.Type == "perm" &&
            (string.Equals(c.Value, "SuperAdmin.ViewAnalytics", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.Value, "SuperAdmin.ManageSubscriptions", StringComparison.OrdinalIgnoreCase)));
    }
}
