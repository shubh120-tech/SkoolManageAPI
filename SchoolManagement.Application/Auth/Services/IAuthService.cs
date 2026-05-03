using System.Threading.Tasks;
using SchoolManagement.Application.Auth.Dtos;
using SchoolManagement.Application.Common;

namespace SchoolManagement.Application.Auth.Services;

public interface IAuthService
{
    Task<ApiResponse<LoginResponseDto>> LoginAsync(LoginRequestDto request, string ipAddress);
    Task<ApiResponse<LoginResponseDto>> RefreshAsync(RefreshTokenRequestDto request, string ipAddress);
    Task<ApiResponse<object>> LogoutAsync(string refreshToken, string ipAddress);
    Task<ApiResponse<object>> ChangePasswordAsync(ChangePasswordRequestDto request);
    Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequestDto request);
    Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequestDto request);
    Task<ApiResponse<object>> UpdateProfileAsync(UpdateProfileRequestDto request);
    Task<ApiResponse<CurrentProfileDto>> GetCurrentProfileAsync();
}

