using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolManagement.Application.Abstractions;

public record JwtTokenResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

public interface IJwtService
{
    Task<JwtTokenResult> GenerateTokensAsync(
        Guid userId,
        Guid? schoolId,
        string role,
        string? schoolCode,
        IReadOnlyCollection<string> permissions);

    Task<(bool IsValid, Guid UserId, Guid? SchoolId)> ValidateAccessTokenAsync(string token);
}

