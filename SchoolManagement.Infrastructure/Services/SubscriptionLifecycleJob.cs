using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Notifications.Services;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Services;

public sealed class SubscriptionLifecycleJob
{
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly INotificationService _notifications;
    private readonly ILogger<SubscriptionLifecycleJob> _logger;

    public SubscriptionLifecycleJob(
        IDbConnectionFactory connectionFactory,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        INotificationService notifications,
        ILogger<SubscriptionLifecycleJob> logger)
    {
        _connectionFactory = connectionFactory;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task ProcessDailyAsync()
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var utcNow = DateTime.UtcNow;
        var targetReminderDate = utcNow.Date.AddDays(7);
        _logger.LogInformation(
            "SubscriptionLifecycleJob: running daily lifecycle. UtcNow={UtcNow}, targetReminderDate(T-7)={TargetDate}",
            utcNow,
            targetReminderDate);

        var expiredMarked = await conn.ExecuteAsync(
            @"
            UPDATE school_subscriptions
            SET is_active = FALSE,
                updated_at = NOW(),
                updated_by = @p_updated_by
            WHERE is_deleted = FALSE
              AND is_active = TRUE
              AND end_date < @p_now;",
            new
            {
                p_updated_by = SystemUserId,
                p_now = DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified)
            });

        _logger.LogInformation("SubscriptionLifecycleJob: marked {Count} subscriptions as expired.", expiredMarked);

        // Step 1: insert reminder rows first so SuperAdmin can see counts even if SMTP fails.
        var pendingInserted = await conn.ExecuteAsync(
            @"
            INSERT INTO subscription_reminder_logs (
                id, school_id, is_deleted, created_at, created_by, updated_at, updated_by,
                school_subscription_id, reminder_type, status, error_message, sent_at
            )
            SELECT
                md5(random()::text || clock_timestamp()::text)::uuid,
                ss.school_id_fk,
                FALSE,
                NOW(),
                @p_created_by,
                NULL,
                NULL,
                ss.id,
                'T-7',
                'Pending',
                NULL,
                NULL
            FROM school_subscriptions ss
            LEFT JOIN subscription_reminder_logs srl
              ON srl.school_subscription_id = ss.id
             AND srl.reminder_type = 'T-7'
             AND srl.is_deleted = FALSE
            WHERE ss.is_deleted = FALSE
              AND ss.is_active = TRUE
              AND ss.end_date::date = @p_target_date::date
              AND srl.id IS NULL
            ON CONFLICT DO NOTHING;",
            new
            {
                p_created_by = SystemUserId,
                p_target_date = DateTime.SpecifyKind(targetReminderDate, DateTimeKind.Unspecified)
            });

        _logger.LogInformation(
            "SubscriptionLifecycleJob: inserted {Count} pending reminder rows for T-7.",
            pendingInserted);

        if (!_emailOptions.Value.Enabled)
        {
            _logger.LogInformation(
                "SubscriptionLifecycleJob: Email:Enabled is false; will still deliver in-app notifications to School Admins where applicable.");
        }

        // Step 2: in-app + email for pending rows (no sent_at yet).
        var reminders = (await conn.QueryAsync<ReminderCandidate>(
            @"
            SELECT
                srl.id AS LogId,
                srl.school_subscription_id AS SubscriptionId,
                srl.school_id AS SchoolId,
                s.name AS SchoolName,
                s.code AS SchoolCode,
                ss.end_date AS EndDate,
                sp.name AS PlanName,
                NULLIF(TRIM(COALESCE((
                    SELECT u.email
                    FROM users u
                    JOIN roles r ON r.id = u.role_id
                    WHERE u.school_id = s.id
                      AND u.is_deleted = FALSE
                      AND r.is_deleted = FALSE
                      AND r.name = 'SchoolAdmin'
                    ORDER BY u.created_at ASC
                    LIMIT 1
                ), s.email, '')), '') AS Email
            FROM subscription_reminder_logs srl
            JOIN school_subscriptions ss ON ss.id = srl.school_subscription_id AND ss.is_deleted = FALSE
            JOIN schools s ON s.id = srl.school_id AND s.is_deleted = FALSE
            LEFT JOIN subscription_plans sp ON sp.id = ss.subscription_plan_id AND sp.is_deleted = FALSE
            WHERE srl.is_deleted = FALSE
              AND srl.reminder_type = 'T-7'
              AND srl.status = 'Pending'
              AND srl.sent_at IS NULL
              AND ss.is_active = TRUE;",
            new { p_target_date = DateTime.SpecifyKind(targetReminderDate, DateTimeKind.Unspecified) }))
            .AsList();

        _logger.LogInformation(
            "SubscriptionLifecycleJob: found {Count} candidate subscriptions for T-7 reminders.",
            reminders.Count);

        var sentCount = 0;
        foreach (var item in reminders)
        {
            var adminIds = (await conn.QueryAsync<Guid>(
                @"
                SELECT u.id
                FROM users u
                INNER JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
                WHERE u.school_id = @SchoolId
                  AND u.is_deleted = FALSE
                  AND r.name = 'SchoolAdmin';",
                new { SchoolId = item.SchoolId })).AsList();

            var inAppTitle = "Subscription expires in 7 days";
            var inAppMessage =
                $"Your school subscription ({item.PlanName ?? "plan"}) for {item.SchoolName} ({item.SchoolCode}) ends on {item.EndDate:yyyy-MM-dd}. Please renew to avoid service interruption.";

            var inAppDelivered = false;
            foreach (var adminId in adminIds)
            {
                await _notifications.CreateAsync(
                    adminId,
                    item.SchoolId,
                    inAppTitle,
                    inAppMessage,
                    SystemUserId,
                    "/dashboard");
                inAppDelivered = true;
            }

            if (adminIds.Count == 0)
            {
                _logger.LogWarning(
                    "SubscriptionLifecycleJob: no SchoolAdmin users for school {SchoolId}; T-7 in-app skipped.",
                    item.SchoolId);
            }

            var subject = "Subscription expiry reminder (7 days left)";
            var body = $@"
<p>Hello,</p>
<p>This is a reminder that your school subscription will expire in <strong>7 days</strong>.</p>
<ul>
  <li><strong>School:</strong> {item.SchoolName} ({item.SchoolCode})</li>
  <li><strong>Plan:</strong> {item.PlanName ?? "-"}</li>
  <li><strong>Expiry Date:</strong> {item.EndDate:yyyy-MM-dd}</li>
</ul>
<p>Please renew to avoid service interruption.</p>";

            var emailOk = false;
            if (_emailOptions.Value.Enabled && !string.IsNullOrWhiteSpace(item.Email))
            {
                try
                {
                    await _emailSender.SendAsync(item.Email!, subject, body);
                    emailOk = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "SubscriptionLifecycleJob: failed to send T-7 email for school {SchoolId}, subscription {SubscriptionId}.",
                        item.SchoolId,
                        item.SubscriptionId);
                }
            }
            else if (string.IsNullOrWhiteSpace(item.Email))
            {
                _logger.LogWarning(
                    "SubscriptionLifecycleJob: no SchoolAdmin / school email for T-7 reminder, school {SchoolId}.",
                    item.SchoolId);
            }

            if (inAppDelivered || emailOk)
            {
                await conn.ExecuteAsync(
                    @"
                    UPDATE subscription_reminder_logs
                    SET status = 'Sent',
                        error_message = NULL,
                        sent_at = @p_sent_at,
                        updated_at = NOW(),
                        updated_by = @p_updated_by
                    WHERE id = @p_id
                      AND is_deleted = FALSE;",
                    new
                    {
                        p_id = item.LogId,
                        p_updated_by = SystemUserId,
                        p_sent_at = DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified)
                    });
                sentCount++;
            }
            else
            {
                await conn.ExecuteAsync(
                    @"
                    UPDATE subscription_reminder_logs
                    SET status = 'Failed',
                        error_message = 'No SchoolAdmin users and no email configured',
                        updated_at = NOW(),
                        updated_by = @p_updated_by
                    WHERE id = @p_id
                      AND is_deleted = FALSE;",
                    new { p_id = item.LogId, p_updated_by = SystemUserId });
            }
        }

        _logger.LogInformation(
            "SubscriptionLifecycleJob: processed {Total} T-7 reminders, sent {Sent}.",
            reminders.Count,
            sentCount);
    }

    private sealed class ReminderCandidate
    {
        public Guid LogId { get; set; }
        public Guid SubscriptionId { get; set; }
        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string SchoolCode { get; set; } = string.Empty;
        public DateTime EndDate { get; set; }
        public string? PlanName { get; set; }
        public string? Email { get; set; }
    }
}
