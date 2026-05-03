using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools.Dtos;

namespace SchoolManagement.Application.Schools.Services;

public interface ISchoolFeatureService
{
    /// <summary>Effective flags for a school (defaults merged with DB).</summary>
    Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid schoolId);

    Task<bool> IsFeatureEnabledAsync(Guid schoolId, string featureKey);

    Task<ApiResponse<SchoolFeaturesResponseDto>> GetSchoolFeaturesForSuperAdminAsync(Guid schoolId);

    Task<ApiResponse<object>> UpdateSchoolFeaturesAsync(Guid schoolId, UpdateSchoolFeaturesRequestDto request, Guid updatedByUserId);

    /// <summary>Insert default rows for a newly registered school.</summary>
    Task SeedDefaultsForNewSchoolAsync(Guid schoolId, Guid createdByUserId);
}
