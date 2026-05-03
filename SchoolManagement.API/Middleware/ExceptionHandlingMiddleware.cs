using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolManagement.Application.Common;

namespace SchoolManagement.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var pgEx = FindPostgresException(ex);
            if (pgEx != null && pgEx.SqlState == "P0001")
            {
                var message = NormalizeRaiseExceptionMessage(pgEx);
                _logger.LogWarning(pgEx, "Business rule: {Message}", message);
                await WriteErrorAsync(context, HttpStatusCode.BadRequest, message, ErrorCodes.BusinessRule);
                return;
            }

            if (pgEx != null && pgEx.SqlState == "23505")
            {
                var message = MapUniqueViolationToUserMessage(pgEx);
                _logger.LogWarning(pgEx, "Unique violation: {Message}", message);
                await WriteErrorAsync(context, HttpStatusCode.Conflict, message, ErrorCodes.Conflict);
                return;
            }

            _logger.LogError(ex, "Unhandled exception");
            var fallbackMessage = _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.";
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, fallbackMessage, ErrorCodes.InternalError);
        }
    }

    private static string NormalizeRaiseExceptionMessage(PostgresException pgEx)
    {
        var raw = string.IsNullOrWhiteSpace(pgEx.MessageText) ? pgEx.Message : pgEx.MessageText;
        return raw.StartsWith("P0001:", StringComparison.OrdinalIgnoreCase)
            ? raw.Substring(6).Trim()
            : raw.Trim();
    }

    /// <summary>
    /// Maps PostgreSQL unique_violation (23505) to messages the UI can show as-is.
    /// Constraint names match <c>src/db/schema.sql</c> and PostgreSQL auto-names (e.g. schools_code_key).
    /// </summary>
    private static string MapUniqueViolationToUserMessage(PostgresException ex)
    {
        var c = (ex.ConstraintName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(c))
        {
            var hint = ex.Detail ?? ex.MessageText ?? ex.Message;
            if (!string.IsNullOrWhiteSpace(hint) &&
                hint.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                return hint.Trim();
            return "This value already exists. It conflicts with another record.";
        }

        return c switch
        {
            "schools_code_key" => "School code already exists. Choose a different code.",
            "subscription_plans_name_key" => "A subscription plan with this name already exists.",
            "uq_roles_name" => "A role with this name already exists.",
            "uq_permissions_name" => "A permission with this name already exists.",
            "uq_users_email" => "This email is already registered for a user in this school.",
            "uq_classes_name_per_session" => "A class with this name already exists for this academic session.",
            "uq_sections_class_name_per_session" => "A section with this name already exists for this class and session.",
            "uq_subjects_name" => "A subject with this name already exists.",
            "uq_staff_email" => "A staff member with this email already exists in this school.",
            "uq_students_email" => "A student with this email already exists in this school.",
            "uq_students_admission_no" => "This admission number is already in use.",
            "uq_fee_heads_name" => "A fee head with this name already exists.",
            "uq_fee_payments_receipt_number" => "This receipt number is already used. Enter a different number or leave blank to auto-generate.",
            "uq_class_teachers" => "This class and section already has a class teacher for this session.",
            "uq_staff_subject" => "This subject is already assigned to this staff member.",
            "uq_payroll_records" => "Payroll for this staff member, month, and year already exists.",
            "uq_timetables_class_slot" => "A timetable entry already exists for this class, day, and start time.",
            "uq_attendance_day" => "Attendance for this class, section, and date is already recorded.",
            "uq_staff_bank_details" => "Bank details for this staff member already exist.",
            _ => FallbackFromConstraintName(c, ex)
        };
    }

    private static string FallbackFromConstraintName(string constraintLower, PostgresException ex)
    {
        if (constraintLower.Contains("schools") && constraintLower.Contains("code"))
            return "School code already exists. Choose a different code.";
        if (constraintLower.Contains("students") && constraintLower.Contains("email"))
            return "A student with this email already exists in this school.";
        if (constraintLower.Contains("staff") && constraintLower.Contains("email"))
            return "A staff member with this email already exists in this school.";
        if (constraintLower.Contains("section"))
            return "A section with this name already exists for this class and session.";
        if (constraintLower.Contains("class") && constraintLower.Contains("session"))
            return "A class with this name already exists for this academic session.";

        var detail = ex.Detail;
        if (!string.IsNullOrWhiteSpace(detail))
            return detail.Trim();

        return "This value already exists or conflicts with an existing record.";
    }

    private static PostgresException? FindPostgresException(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg)
                return pg;
        }
        return null;
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message, string errorCode)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = new ApiResponse<object>
        {
            Success = false,
            Message = message,
            Data = null,
            ErrorCode = errorCode
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}

