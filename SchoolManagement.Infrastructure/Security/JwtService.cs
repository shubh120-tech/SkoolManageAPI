using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Security;

public class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly IDbConnectionFactory _connectionFactory;

    public JwtService(IOptions<JwtOptions> options, IDbConnectionFactory connectionFactory)
    {
        _options = options.Value;
        _connectionFactory = connectionFactory;
    }

    public async Task<JwtTokenResult> GenerateTokensAsync(
        Guid userId,
        Guid? schoolId,
        string role,
        string? schoolCode,
        IReadOnlyCollection<string> permissions)
    {
        var now = DateTime.UtcNow;
        // Access token valid for 24 hours
        var accessExpires = now.AddHours(24);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (schoolId.HasValue)
        {
            claims.Add(new Claim("school_id", schoolId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(schoolCode))
        {
            claims.Add(new Claim("school_code", schoolCode!));
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim("role", role!));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("perm", permission));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: accessExpires,
            signingCredentials: creds);

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(token);

        var refreshToken = GenerateSecureRefreshToken();
        var refreshHash = HashToken(refreshToken);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_user_id", userId);
        p.Add("p_token_hash", refreshHash);
        p.Add("p_expires_at", refreshExpires);
        await conn.ExecuteAsync("CALL sp_auth_create_refresh_token(@p_user_id,@p_token_hash,@p_expires_at)", p);

        return new JwtTokenResult(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    public Task<(bool IsValid, Guid UserId, Guid? SchoolId)> ValidateAccessTokenAsync(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var jwt = handler.ReadJwtToken(token);
            var sub = jwt.Subject;
            if (!Guid.TryParse(sub, out var userId))
                return Task.FromResult((false, Guid.Empty, (Guid?)null));

            Guid? schoolId = null;
            var sid = jwt.Claims.FirstOrDefault(c => c.Type == "school_id")?.Value;
            if (Guid.TryParse(sid, out var sidGuid))
            {
                schoolId = sidGuid;
            }

            return Task.FromResult((true, userId, schoolId));
        }
        catch
        {
            return Task.FromResult((false, Guid.Empty, (Guid?)null));
        }
    }

    private static string GenerateSecureRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}

