using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Communication.Dtos;

namespace SchoolManagement.Application.Communication.Services;

public interface ICommunicationService
{
    Task<ApiResponse<object>> CreateAnnouncementAsync(AnnouncementRequestDto request);
    Task<ApiResponse<IReadOnlyCollection<AnnouncementItemDto>>> GetAnnouncementsAsync(
        bool? forStudents,
        bool? forStaff,
        bool includeExpired,
        int limit);
    Task<ApiResponse<object>> UpdateAnnouncementAsync(Guid id, AnnouncementRequestDto request);
    Task<ApiResponse<object>> DeleteAnnouncementAsync(Guid id);
}

