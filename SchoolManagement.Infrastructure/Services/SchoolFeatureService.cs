using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools;
using SchoolManagement.Application.Schools.Dtos;
using SchoolManagement.Application.Schools.Services;

namespace SchoolManagement.Infrastructure.Services;

public class SchoolFeatureService : ISchoolFeatureService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SchoolFeatureService> _logger;

    private static readonly IReadOnlyDictionary<string, bool> DefaultEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        [SchoolFeatureKeys.WhatsAppBasic] = true,
        [SchoolFeatureKeys.WhatsAppPremium] = false,
    };

    private static readonly IReadOnlyList<(string Key, string DisplayName, string Description)> Catalog = new List<(string, string, string)>
    {
        (SchoolFeatureKeys.WhatsAppBasic, "WhatsApp (chat link)",
            "School users can open WhatsApp via wa.me links for reminders—no Meta API."),
        (SchoolFeatureKeys.WhatsAppPremium, "WhatsApp Premium (API)",
            "Server-side WhatsApp Cloud API: automated/bulk messages, templates, invoice links when configured."),
    };

    public SchoolFeatureService(IDbConnectionFactory connectionFactory, ILogger<SchoolFeatureService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid schoolId)
    {
        var dict = new Dictionary<string, bool>(DefaultEnabled, StringComparer.OrdinalIgnoreCase);
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var rows = await conn.QueryAsync<(string Key, bool Enabled)>(
            @"SELECT feature_key AS Key, is_enabled AS Enabled
              FROM school_feature_entitlements
              WHERE school_id = @SchoolId;",
            new { SchoolId = schoolId });

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Key))
                dict[row.Key] = row.Enabled;
        }

        return dict;
    }

    public async Task<bool> IsFeatureEnabledAsync(Guid schoolId, string featureKey)
    {
        var eff = await GetEffectiveFeaturesAsync(schoolId);
        return eff.TryGetValue(featureKey, out var v) && v;
    }

    public async Task<ApiResponse<SchoolFeaturesResponseDto>> GetSchoolFeaturesForSuperAdminAsync(Guid schoolId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM schools WHERE id = @Id AND is_deleted = FALSE);",
            new { Id = schoolId });
        if (!exists)
            return ApiResponse<SchoolFeaturesResponseDto>.Fail("School not found.", ErrorCodes.NotFound);

        var effective = await GetEffectiveFeaturesAsync(schoolId);
        var items = Catalog.Select(c => new SchoolFeatureItemDto
        {
            Key = c.Key,
            DisplayName = c.DisplayName,
            Description = c.Description,
            IsEnabled = effective.TryGetValue(c.Key, out var en) && en,
        }).ToList();

        return ApiResponse<SchoolFeaturesResponseDto>.Ok(new SchoolFeaturesResponseDto { Items = items });
    }

    public async Task<ApiResponse<object>> UpdateSchoolFeaturesAsync(
        Guid schoolId,
        UpdateSchoolFeaturesRequestDto request,
        Guid updatedByUserId)
    {
        if (request.Features is null || request.Features.Count == 0)
            return ApiResponse<object>.Fail("No features supplied.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM schools WHERE id = @Id AND is_deleted = FALSE);",
            new { Id = schoolId });
        if (!exists)
            return ApiResponse<object>.Fail("School not found.", ErrorCodes.NotFound);

        var allowedKeys = new HashSet<string>(Catalog.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var kv in request.Features)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || !allowedKeys.Contains(kv.Key))
                continue;

            await conn.ExecuteAsync(
                @"INSERT INTO school_feature_entitlements (school_id, feature_key, is_enabled, updated_at, updated_by)
                  VALUES (@SchoolId, @FeatureKey, @Enabled, NOW(), @UpdatedBy)
                  ON CONFLICT (school_id, feature_key)
                  DO UPDATE SET is_enabled = EXCLUDED.is_enabled, updated_at = NOW(), updated_by = EXCLUDED.updated_by;",
                new
                {
                    SchoolId = schoolId,
                    FeatureKey = NormalizeKey(kv.Key),
                    Enabled = kv.Value,
                    UpdatedBy = updatedByUserId,
                });
        }

        _logger.LogInformation("School {SchoolId} feature entitlements updated by {UserId}.", schoolId, updatedByUserId);
        return ApiResponse<object>.Ok(null, "Features updated.");
    }

    public async Task SeedDefaultsForNewSchoolAsync(Guid schoolId, Guid createdByUserId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        foreach (var kv in DefaultEnabled)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO school_feature_entitlements (school_id, feature_key, is_enabled, updated_at, updated_by)
                  VALUES (@SchoolId, @FeatureKey, @Enabled, NOW(), @UpdatedBy)
                  ON CONFLICT (school_id, feature_key) DO NOTHING;",
                new
                {
                    SchoolId = schoolId,
                    FeatureKey = kv.Key,
                    Enabled = kv.Value,
                    UpdatedBy = createdByUserId,
                });
        }
    }

    private static string NormalizeKey(string key)
    {
        foreach (var c in Catalog)
        {
            if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                return c.Key;
        }

        return key;
    }
}
