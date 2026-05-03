using System;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;

namespace SchoolManagement.Infrastructure.Logging;

public class AuditLogger : IAuditLogger
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AuditLogger(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task LogAsync(Guid? userId, Guid? schoolId, string action, string entityName, Guid? entityId, string? detailsJson)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_user_id", userId);
        p.Add("p_action", action);
        p.Add("p_entity_name", entityName);
        p.Add("p_entity_id", entityId);
        p.Add("p_details_json", detailsJson);
        await conn.ExecuteAsync("CALL sp_audit_log_create(@p_school_id,@p_user_id,@p_action,@p_entity_name,@p_entity_id,@p_details_json)", p);
    }

    public static string SerializeDetails(object obj) => JsonSerializer.Serialize(obj);
}

