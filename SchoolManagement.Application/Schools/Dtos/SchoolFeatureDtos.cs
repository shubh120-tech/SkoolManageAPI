using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Schools.Dtos;

public class SchoolFeatureItemDto
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class SchoolFeaturesResponseDto
{
    public List<SchoolFeatureItemDto> Items { get; set; } = new();
}

public class UpdateSchoolFeaturesRequestDto
{
    /// <summary>Feature key → enabled. Only known keys are applied.</summary>
    public Dictionary<string, bool>? Features { get; set; }
}
