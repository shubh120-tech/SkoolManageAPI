using System;
using System.Threading.Tasks;

namespace SchoolManagement.Application.Abstractions;

public interface IAuditLogger
{
    Task LogAsync(Guid? userId, Guid? schoolId, string action, string entityName, Guid? entityId, string? detailsJson);
}

