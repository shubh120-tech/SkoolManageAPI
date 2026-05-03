using System;
using System.Collections.Generic;

namespace SchoolManagement.Application.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? SchoolId { get; }
    string? Role { get; }
    string? SchoolCode { get; }
    IReadOnlyCollection<string> Permissions { get; }
}

