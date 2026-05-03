using System;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Leaves.Dtos;

namespace SchoolManagement.Application.Leaves.Services;

public interface ILeaveService
{
    Task<ApiResponse<object>> CreateLeaveAsync(LeaveRequestCreateDto request);
    Task<ApiResponse<LeaveRequestListResponseDto>> GetMyLeavesAsync(int page, int pageSize, string? status, int? year, int? month);
    Task<ApiResponse<LeaveRequestListResponseDto>> GetAllLeavesAsync(int page, int pageSize, string? status, int? year, int? month);
    Task<ApiResponse<object>> ApproveLeaveAsync(Guid id, string? remarks);
    Task<ApiResponse<object>> RejectLeaveAsync(Guid id, string? remarks);
    Task<ApiResponse<object>> RequestWithdrawLeaveAsync(Guid id);
    Task<ApiResponse<object>> RejectWithdrawLeaveAsync(Guid id, string? remarks);
    Task<ApiResponse<object>> ApproveWithdrawLeaveAsync(Guid id, ApproveWithdrawLeaveRequestDto? request);
}

