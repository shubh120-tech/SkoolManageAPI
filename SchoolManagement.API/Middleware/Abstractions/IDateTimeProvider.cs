using System;

namespace SchoolManagement.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

