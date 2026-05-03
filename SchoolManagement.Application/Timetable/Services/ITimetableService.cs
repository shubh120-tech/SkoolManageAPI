using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Timetable.Dtos;

namespace SchoolManagement.Application.Timetable.Services;

public interface ITimetableService
{
    Task<ApiResponse<object>> UpsertTimetableEntryAsync(TimetableEntryDto request);
    Task<ApiResponse<object>> DeleteTimetableEntryAsync(Guid timetableId);
}

