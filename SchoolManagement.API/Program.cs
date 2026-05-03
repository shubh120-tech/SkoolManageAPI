using System.Data;
using System.Text;
using Dapper;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Auth.Services;
using SchoolManagement.Infrastructure.Auth;
using SchoolManagement.Infrastructure.Configuration;
using SchoolManagement.Infrastructure.Logging;
using SchoolManagement.Infrastructure.MultiTenancy;
using SchoolManagement.Infrastructure.Persistence;
using SchoolManagement.Infrastructure.Security;
using SchoolManagement.Infrastructure.Services;
using SchoolManagement.Application.Notifications.Services;
using SchoolManagement.API.Middleware;
using SchoolManagement.API.Authorization;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// CORS — origins from appsettings / env-specific files (Cors:AllowedOrigins)
var frontendOrigin = "AllowSpecific";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsOrigins is null || corsOrigins.Length == 0)
    corsOrigins = new[] { "http://localhost:3000" };

// When testing a local React app (localhost:3000) against this deployed API, set Cors:AllowLocalhost=true
// (Azure: application setting Cors__AllowLocalhost = true). Turn off in production if you do not need it.
if (builder.Configuration.GetValue<bool>("Cors:AllowLocalhost"))
{
    var list = corsOrigins.ToList();
    foreach (var o in new[] { "http://localhost:3000", "http://127.0.0.1:3000" })
    {
        if (!list.Exists(x => string.Equals(x, o, StringComparison.OrdinalIgnoreCase)))
            list.Add(o);
    }

    corsOrigins = list.ToArray();
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendOrigin, policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// existing stuff
// ... other services

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IWhatsAppSender, MetaWhatsAppSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();
builder.Services.AddScoped<SchoolManagement.Application.Access.Services.IAccessService, SchoolManagement.Infrastructure.Services.AccessService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<SchoolManagement.Application.Schools.Services.ISchoolFeatureService, SchoolManagement.Infrastructure.Services.SchoolFeatureService>();
builder.Services.AddScoped<SchoolManagement.Application.Schools.Services.ISchoolService, SchoolService>();
builder.Services.AddScoped<SchoolManagement.Application.Students.Services.IStudentService, StudentService>();
builder.Services.AddScoped<SchoolManagement.Application.Staff.Services.IStaffService, StaffService>();
builder.Services.AddScoped<SchoolManagement.Application.Attendance.Services.IAttendanceService, AttendanceService>();
builder.Services.AddScoped<SchoolManagement.Application.Leaves.Services.ILeaveService, LeaveService>();
builder.Services.AddScoped<SchoolManagement.Application.Fees.Services.IFeeService, FeeService>();
builder.Services.AddScoped<SchoolManagement.Application.Payroll.Services.IPayrollService, PayrollService>();
builder.Services.AddScoped<SchoolManagement.Application.Timetable.Services.ITimetableService, TimetableService>();
builder.Services.AddScoped<SchoolManagement.Application.Communication.Services.ICommunicationService, CommunicationService>();
builder.Services.AddScoped<SchoolManagement.Application.Reporting.Services.IReportingService, ReportingService>();
builder.Services.AddScoped<SchoolManagement.Application.Academic.Services.IAcademicService, AcademicService>();
builder.Services.AddScoped<SchoolManagement.Application.Academic.Services.IAcademicSessionService, AcademicSessionService>();
builder.Services.AddScoped<SubscriptionLifecycleJob>();

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    defaultConnection = builder.Configuration["ConnectionStrings:DefaultConnection"];
}

if (string.IsNullOrWhiteSpace(defaultConnection))
{
    // Azure App Service maps the "Connection strings" blade to typed env vars; names differ by DB type and OS.
    foreach (var envKey in new[]
             {
                 "CUSTOMCONNSTR_DefaultConnection",
                 "POSTGRESQLCONNSTR_DefaultConnection",
                 "SQLCONNSTR_DefaultConnection",
                 "SQLAZURECONNSTR_DefaultConnection",
                 "ConnectionStrings__DefaultConnection"
             })
    {
        var v = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(v))
        {
            defaultConnection = v;
            break;
        }
    }
}

if (string.IsNullOrWhiteSpace(defaultConnection))
{
    foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
    {
        var key = e.Key?.ToString() ?? "";
        if (!key.Contains("CONNSTR", StringComparison.OrdinalIgnoreCase))
            continue;
        if (!key.Contains("DefaultConnection", StringComparison.OrdinalIgnoreCase))
            continue;
        if (e.Value is string s && !string.IsNullOrWhiteSpace(s))
        {
            defaultConnection = s;
            break;
        }
    }
}

if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException(
        "Database connection string is missing at runtime. Fix in Azure Portal → your Web App (correct app + production slot) → Configuration → Save: " +
        "(1) Application settings: name ConnectionStrings__DefaultConnection (two underscores), value = full Npgsql connection string; OR " +
        "(2) Connection strings: name DefaultConnection, type PostgreSQL or Custom. " +
        "If using Key Vault reference, ensure access policy/role and that the secret resolves (empty = failed). " +
        "Redeploy does not create this; you must set it in the portal for this app.");
}
builder.Services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(defaultConnection));
});
builder.Services.AddHangfireServer();

builder.Services.AddAuthorization();

var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection.GetValue<string>("SigningKey") ?? throw new InvalidOperationException("Jwt:SigningKey missing");
var issuer = jwtSection.GetValue<string>("Issuer");
var audience = jwtSection.GetValue<string>("Audience");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(100)
        };
    });

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure user_permissions table exists (migration 011) and auth SQL functions are current
// (patches SuperAdmin login when DB had old fn_auth_get_user_for_login from manual 011).
await EnsureUserPermissionsTableAsync(app.Services);
// Ensure leave half-day columns exist (migration 012)
await EnsureLeaveHalfDayColumnsAsync(app.Services);
// Leave withdrawal + attendance_status (migration 013)
await EnsureLeaveWithdrawAttendanceMigrationAsync(app.Services);
await EnsureCanEditPastAttendancePermissionAsync(app.Services);
// Ensure Fees.Payments permission exists for assignment UI/workflow.
await EnsureFeesPaymentsPermissionAsync(app.Services);
// Ensure payroll granular permissions exist for assignment workflow.
await EnsurePayrollPermissionsAsync(app.Services);
// Ensure full permission catalog exists for assignment UI.
await EnsurePlatformPermissionsCatalogAsync(app.Services);
// Ensure SuperAdmin role has SuperAdmin.* permissions (fix 403 on subscriptions/analytics).
await EnsureSuperAdminRolePermissionsAsync(app.Services);
// Ensure platform Teacher role has baseline permissions (fees view, payroll history, profile).
await EnsureTeacherRoleBaselinePermissionsAsync(app.Services);
// Ensure table for school subscription payment history exists.
await EnsureSchoolSubscriptionPaymentsTableAsync(app.Services);
// Ensure table for SuperAdmin expense tracking exists.
await EnsureSuperAdminExpensesTableAsync(app.Services);
// Ensure table for subscription reminder logs exists.
await EnsureSubscriptionReminderLogsTableAsync(app.Services);
// In-app notifications: link_url column for deep links from bell UI.
await EnsureNotificationsLinkUrlColumnAsync(app.Services);
await EnsureSchoolFeatureEntitlementsTableAsync(app.Services);

app.UseCors(frontendOrigin);

app.UseSerilogRequestLogging();


    app.UseSwagger();
    app.UseSwaggerUI();


app.UseHttpsRedirection();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<SchoolStatusMiddleware>();
app.UseMiddleware<LicenseValidationMiddleware>();
app.UseMiddleware<RbacPermissionMiddleware>();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Ensure Hangfire assets/links resolve properly.
    AppPath = "/",
    Authorization =
    [
        new SuperAdminHangfireDashboardAuthorizationFilter(signingKey, issuer, audience)
    ]
});

RecurringJob.AddOrUpdate<SubscriptionLifecycleJob>(
    "subscription-lifecycle-daily",
    job => job.ProcessDailyAsync(),
    "0 2 * * *",
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Utc
    });

Serilog.Log.Information(
    "Hangfire startup: ensured recurring job 'subscription-lifecycle-daily' with cron '0 2 * * *' (UTC).");

app.MapControllers();

app.Run();

static async Task EnsureUserPermissionsTableAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
        // Idempotent schema — safe to run every startup.
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS user_permissions (
    id UUID PRIMARY KEY DEFAULT md5(random()::text || clock_timestamp()::text)::uuid,
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,
    user_id UUID NOT NULL REFERENCES users(id),
    permission_id UUID NOT NULL REFERENCES permissions(id),
    CONSTRAINT uq_user_permissions UNIQUE (user_id, permission_id)
);
CREATE INDEX IF NOT EXISTS ix_user_permissions_user_id ON user_permissions(user_id) WHERE is_deleted = FALSE;");

        // Always replace login function: DBs that already had user_permissions used to skip this block and
        // kept an old fn_auth_get_user_for_login that referenced v_school for SuperAdmin (school_id NULL) → 55000.
        await conn.ExecuteAsync(@"
CREATE OR REPLACE FUNCTION fn_auth_get_user_for_login(p_email TEXT)
RETURNS JSONB LANGUAGE plpgsql AS $fn$
DECLARE
    v_user RECORD;
    v_school RECORD;
    v_permissions TEXT[];
BEGIN
    SELECT u.*, r.name AS role_name INTO v_user
    FROM users u
    JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
    WHERE LOWER(u.email) = LOWER(p_email) AND u.is_deleted = FALSE LIMIT 1;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF v_user.school_id IS NOT NULL THEN
        SELECT s.* INTO v_school FROM schools s WHERE s.id = v_user.school_id AND s.is_deleted = FALSE LIMIT 1;
        IF NOT FOUND THEN RETURN NULL; END IF;
    END IF;
    SELECT ARRAY(SELECT p.name FROM user_permissions up JOIN permissions p ON p.id = up.permission_id AND p.is_deleted = FALSE
        WHERE up.user_id = v_user.id AND up.is_deleted = FALSE) INTO v_permissions;
    IF v_permissions IS NULL OR array_length(v_permissions, 1) IS NULL THEN
        SELECT ARRAY(SELECT p.name FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
            WHERE rp.role_id = v_user.role_id AND rp.is_deleted = FALSE) INTO v_permissions;
    END IF;
    RETURN jsonb_build_object(
        'UserId', v_user.id, 'SchoolId', v_user.school_id, 'RoleName', v_user.role_name,
        'SchoolCode', CASE WHEN v_user.school_id IS NULL THEN NULL ELSE (SELECT code FROM schools WHERE id = v_user.school_id AND is_deleted = FALSE LIMIT 1) END,
        'PasswordHash', v_user.password_hash, 'IsLocked', v_user.is_locked, 'MustChangePassword', v_user.must_change_password,
        'IsDeletedUser', v_user.is_soft_deleted_user,
        'SchoolIsActive', COALESCE((SELECT is_active FROM schools WHERE id = v_user.school_id AND is_deleted = FALSE LIMIT 1), TRUE),
        'LicenseExpired', CASE WHEN v_user.school_id IS NULL THEN FALSE WHEN (SELECT license_end_date FROM schools WHERE id = v_user.school_id LIMIT 1) IS NULL THEN FALSE
            ELSE (SELECT license_end_date FROM schools WHERE id = v_user.school_id LIMIT 1) < NOW() END,
        'Permissions', COALESCE(v_permissions, ARRAY[]::TEXT[]));
END;
$fn$;");

        await conn.ExecuteAsync(@"
CREATE OR REPLACE FUNCTION fn_auth_rotate_refresh_token(p_refresh_token_hash TEXT)
RETURNS JSONB LANGUAGE plpgsql AS $fn$
DECLARE
    v_rt RECORD;
    v_user RECORD;
    v_school RECORD;
    v_permissions TEXT[];
BEGIN
    SELECT * INTO v_rt FROM refresh_tokens
    WHERE token_hash = p_refresh_token_hash AND is_deleted = FALSE FOR UPDATE;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF v_rt.is_revoked OR v_rt.expires_at < NOW() THEN RETURN NULL; END IF;
    UPDATE refresh_tokens SET is_revoked = TRUE, updated_at = NOW() WHERE id = v_rt.id;
    SELECT u.*, r.name AS role_name INTO v_user
    FROM users u JOIN roles r ON r.id = u.role_id AND r.is_deleted = FALSE
    WHERE u.id = v_rt.user_id AND u.is_deleted = FALSE;
    IF NOT FOUND OR v_user.is_soft_deleted_user OR v_user.is_locked THEN RETURN NULL; END IF;
    IF v_user.school_id IS NOT NULL THEN
        SELECT * INTO v_school FROM schools s WHERE s.id = v_user.school_id AND s.is_deleted = FALSE;
        IF NOT FOUND OR NOT v_school.is_active
           OR (v_school.license_end_date IS NOT NULL AND v_school.license_end_date < NOW()) THEN
            RETURN NULL;
        END IF;
    END IF;
    SELECT ARRAY(
        SELECT p.name FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id AND p.is_deleted = FALSE
        WHERE rp.role_id = v_user.role_id AND rp.is_deleted = FALSE) INTO v_permissions;
    RETURN jsonb_build_object(
        'UserId', v_user.id, 'SchoolId', v_user.school_id, 'RoleName', v_user.role_name,
        'SchoolCode', CASE WHEN v_user.school_id IS NULL THEN NULL ELSE v_school.code END,
        'PasswordHash', v_user.password_hash, 'IsLocked', v_user.is_locked, 'MustChangePassword', v_user.must_change_password,
        'IsDeletedUser', v_user.is_soft_deleted_user,
        'SchoolIsActive', CASE WHEN v_user.school_id IS NULL THEN TRUE ELSE COALESCE(v_school.is_active, TRUE) END,
        'LicenseExpired', CASE
            WHEN v_user.school_id IS NULL THEN FALSE
            WHEN v_school.license_end_date IS NULL THEN FALSE
            ELSE v_school.license_end_date < NOW() END,
        'Permissions', v_permissions);
END;
$fn$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration user_permissions skipped or failed.");
    }
}

static async Task EnsureLeaveHalfDayColumnsAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
ALTER TABLE leave_requests
ADD COLUMN IF NOT EXISTS is_half_day BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE leave_requests
ADD COLUMN IF NOT EXISTS half_day_session VARCHAR(20) NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_leave_requests_half_day_session'
    ) THEN
        ALTER TABLE leave_requests
        ADD CONSTRAINT ck_leave_requests_half_day_session
        CHECK (
            half_day_session IS NULL
            OR half_day_session IN ('FirstHalf', 'SecondHalf')
        );
    END IF;
END $$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration leave half-day columns skipped or failed.");
    }
}

static async Task EnsureNotificationsLinkUrlColumnAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
ALTER TABLE notifications
ADD COLUMN IF NOT EXISTS link_url TEXT NULL;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration notifications.link_url skipped or failed.");
    }
}

static async Task EnsureFeesPaymentsPermissionAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
SELECT md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), '00000000-0000-0000-0000-000000000001'::uuid, 'Fees.Payments', 'Record & view fee payments'
WHERE NOT EXISTS (
    SELECT 1
    FROM permissions
    WHERE school_id IS NULL
      AND name = 'Fees.Payments'
      AND is_deleted = FALSE
);

INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
SELECT
    md5(random()::text || clock_timestamp()::text)::uuid,
    NULL,
    FALSE,
    NOW(),
    '00000000-0000-0000-0000-000000000001'::uuid,
    r.id,
    p.id
FROM roles r
JOIN permissions p
  ON p.school_id IS NULL
 AND p.name = 'Fees.Payments'
 AND p.is_deleted = FALSE
WHERE r.school_id IS NULL
  AND r.name = 'SchoolAdmin'
  AND r.is_deleted = FALSE
  AND NOT EXISTS (
      SELECT 1
      FROM role_permissions rp
      WHERE rp.role_id = r.id
        AND rp.permission_id = p.id
        AND rp.is_deleted = FALSE
  );");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration Fees.Payments permission skipped or failed.");
    }
}

static async Task EnsurePayrollPermissionsAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_school_admin_role_id UUID;
    v_perm_id UUID;
    v_perm_name TEXT;
    v_permissions TEXT[] := ARRAY[
        'Payroll.View',
        'Payroll.Generate',
        'Payroll.Pay',
        'Payroll.History.View',
        'Reporting.Staff.Payroll'
    ];
BEGIN
    SELECT id INTO v_school_admin_role_id
    FROM roles
    WHERE school_id IS NULL AND name = 'SchoolAdmin' AND is_deleted = FALSE
    LIMIT 1;

    FOREACH v_perm_name IN ARRAY v_permissions
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM permissions
            WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE
        ) THEN
            INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
            VALUES (md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_perm_name, 'Platform permission: ' || v_perm_name);
        END IF;

        IF v_school_admin_role_id IS NOT NULL THEN
            SELECT id INTO v_perm_id
            FROM permissions
            WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE
            LIMIT 1;

            IF v_perm_id IS NOT NULL AND NOT EXISTS (
                SELECT 1
                FROM role_permissions
                WHERE role_id = v_school_admin_role_id
                  AND permission_id = v_perm_id
                  AND is_deleted = FALSE
            ) THEN
                INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
                VALUES (md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_school_admin_role_id, v_perm_id);
            END IF;
        END IF;
    END LOOP;
END $$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration payroll permissions skipped or failed.");
    }
}

static async Task EnsurePlatformPermissionsCatalogAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_perm_name TEXT;
    v_permissions TEXT[] := ARRAY[
        -- Students
        'Student.View', 'Student.View.All',
        'Student.Admit', 'Student.Admit.All',
        'Student.Update', 'Student.Update.All',
        'Student.Promote', 'Student.Promote.All',
        'Student.Delete', 'Student.Manage',
        -- Fees
        'Fees.View', 'Fees.View.All',
        'Fees.Manage', 'Fees.Manage.All',
        'Fees.Payments', 'Fees.Payments.All',
        -- Attendance
        'Attendance.View', 'Attendance.Mark', 'Attendance.Manage', 'Attendance.ManageAll',
        'Attendance.Student.Manage', 'Attendance.Staff.Manage', 'CanEditPastAttendance',
        -- Academics
        'Academic.Sessions.View', 'Academic.Sessions.Manage',
        'Academic.Classes.View', 'Academic.Classes.Manage', 'Academic.ClassesSections.ManageAll',
        'Academic.Sections.View', 'Academic.Sections.Manage',
        'Academic.Subjects.Manage',
        -- Payroll / Reporting
        'Payroll.View', 'Payroll.View.All',
        'Payroll.Generate', 'Payroll.Generate.All',
        'Payroll.Pay', 'Payroll.Pay.All',
        'Payroll.History.View', 'Payroll.History.View.All',
        'Reporting.Students.View', 'Reporting.Students.Overview',
        'Reporting.Staff.Payroll', 'Reporting.Staff.View', 'Reporting.Timetable.View',
        -- Communication / Timetable / Leaves / Profile
        'Communication.Announcements.View', 'Communication.Announcements.Manage',
        'Timetable.View', 'Timetable.Manage',
        'Leave.Apply', 'Leave.ViewOwn', 'Leave.View.All', 'Leave.Approve', 'Leave.Approve.All',
        'Profile.Manage',
        -- Staff / Admin
        'Staff.View', 'Staff.Manage', 'Admin.Permissions',
        -- SuperAdmin
        'SuperAdmin.RegisterSchool', 'SuperAdmin.ActivateSchool',
        'SuperAdmin.DeactivateSchool', 'SuperAdmin.SoftDeleteSchool',
        'SuperAdmin.ManageSubscriptions', 'SuperAdmin.ViewAnalytics'
    ];
BEGIN
    FOREACH v_perm_name IN ARRAY v_permissions
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM permissions
            WHERE school_id IS NULL
              AND name = v_perm_name
              AND is_deleted = FALSE
        ) THEN
            INSERT INTO permissions (
                id, school_id, is_deleted, created_at, created_by, name, description
            )
            VALUES (
                md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_perm_name, 'Platform permission: ' || v_perm_name
            );
        END IF;
    END LOOP;
END $$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration permission catalog skipped or failed.");
    }
}

static async Task EnsureSuperAdminRolePermissionsAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_super_admin_role_id UUID;
    v_perm_id UUID;
    v_perm_name TEXT;
    v_permissions TEXT[] := ARRAY[
        'SuperAdmin.RegisterSchool',
        'SuperAdmin.ActivateSchool',
        'SuperAdmin.DeactivateSchool',
        'SuperAdmin.SoftDeleteSchool',
        'SuperAdmin.ManageSubscriptions',
        'SuperAdmin.ViewAnalytics'
    ];
BEGIN
    SELECT id INTO v_super_admin_role_id
    FROM roles
    WHERE school_id IS NULL
      AND name = 'SuperAdmin'
      AND is_deleted = FALSE
    LIMIT 1;

    IF v_super_admin_role_id IS NULL THEN
        RETURN;
    END IF;

    FOREACH v_perm_name IN ARRAY v_permissions
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM permissions
            WHERE school_id IS NULL
              AND name = v_perm_name
              AND is_deleted = FALSE
        ) THEN
            INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
            VALUES (md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_perm_name, 'Platform permission: ' || v_perm_name);
        END IF;

        SELECT id INTO v_perm_id
        FROM permissions
        WHERE school_id IS NULL
          AND name = v_perm_name
          AND is_deleted = FALSE
        LIMIT 1;

        IF v_perm_id IS NOT NULL AND NOT EXISTS (
            SELECT 1
            FROM role_permissions rp
            WHERE rp.role_id = v_super_admin_role_id
              AND rp.permission_id = v_perm_id
              AND rp.is_deleted = FALSE
        ) THEN
            INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
            VALUES (md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_super_admin_role_id, v_perm_id);
        END IF;
    END LOOP;
END $$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration SuperAdmin role permissions skipped or failed.");
    }
}

static async Task EnsureSchoolSubscriptionPaymentsTableAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS school_subscription_payments (
    id UUID PRIMARY KEY DEFAULT md5(random()::text || clock_timestamp()::text)::uuid,
    school_id UUID NOT NULL REFERENCES schools(id),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,
    school_subscription_id UUID NOT NULL REFERENCES school_subscriptions(id),
    amount_paid NUMERIC(12,2) NOT NULL,
    payment_date TIMESTAMP NOT NULL,
    payment_mode VARCHAR(50) NULL,
    reference_no VARCHAR(100) NULL,
    remarks TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_school_subscription_payments_school
ON school_subscription_payments(school_id) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS ix_school_subscription_payments_subscription
ON school_subscription_payments(school_subscription_id) WHERE is_deleted = FALSE;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration school_subscription_payments skipped or failed.");
    }
}

static async Task EnsureTeacherRoleBaselinePermissionsAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
DO $$
DECLARE
    v_system_user UUID := '00000000-0000-0000-0000-000000000001';
    v_teacher_role_id UUID;
    v_perm_id UUID;
    v_perm_name TEXT;
    v_permissions TEXT[] := ARRAY[
        'Fees.View',
        'Payroll.History.View',
        'Profile.Manage',
        'Payroll.View'
    ];
BEGIN
    SELECT id INTO v_teacher_role_id
    FROM roles
    WHERE school_id IS NULL AND name = 'Teacher' AND is_deleted = FALSE
    LIMIT 1;
    IF v_teacher_role_id IS NULL THEN RETURN; END IF;

    FOREACH v_perm_name IN ARRAY v_permissions
    LOOP
        SELECT id INTO v_perm_id
        FROM permissions
        WHERE school_id IS NULL AND name = v_perm_name AND is_deleted = FALSE
        LIMIT 1;
        IF v_perm_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM role_permissions
            WHERE role_id = v_teacher_role_id AND permission_id = v_perm_id AND is_deleted = FALSE
        ) THEN
            INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
            VALUES (md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), v_system_user, v_teacher_role_id, v_perm_id);
        END IF;
    END LOOP;
END $$;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration Teacher baseline permissions skipped or failed.");
    }
}

static async Task EnsureSuperAdminExpensesTableAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS superadmin_expenses (
    id UUID PRIMARY KEY DEFAULT md5(random()::text || clock_timestamp()::text)::uuid,
    school_id UUID NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,
    expense_type VARCHAR(100) NOT NULL,
    frequency VARCHAR(20) NOT NULL,
    amount NUMERIC(12,2) NOT NULL,
    expense_date TIMESTAMP NOT NULL,
    vendor VARCHAR(150) NULL,
    remarks TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_superadmin_expenses_date
ON superadmin_expenses(expense_date) WHERE is_deleted = FALSE;");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration superadmin_expenses skipped or failed.");
    }
}

static async Task EnsureSubscriptionReminderLogsTableAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS subscription_reminder_logs (
    id UUID PRIMARY KEY DEFAULT md5(random()::text || clock_timestamp()::text)::uuid,
    school_id UUID NOT NULL REFERENCES schools(id),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by UUID NOT NULL,
    updated_at TIMESTAMP NULL,
    updated_by UUID NULL,
    school_subscription_id UUID NOT NULL REFERENCES school_subscriptions(id),
    reminder_type VARCHAR(30) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    error_message TEXT NULL,
    sent_at TIMESTAMP NULL
);

ALTER TABLE subscription_reminder_logs
ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'Pending';
ALTER TABLE subscription_reminder_logs
ADD COLUMN IF NOT EXISTS error_message TEXT NULL;
ALTER TABLE subscription_reminder_logs
ADD COLUMN IF NOT EXISTS sent_at TIMESTAMP NULL;

-- Older DBs created sent_at as NOT NULL; job now inserts Pending rows with sent_at = NULL.
ALTER TABLE subscription_reminder_logs
ALTER COLUMN sent_at DROP NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_subscription_reminder_logs_subscription_type
ON subscription_reminder_logs(school_subscription_id, reminder_type)
WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS ix_subscription_reminder_logs_sent_at
ON subscription_reminder_logs(sent_at);");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration subscription_reminder_logs skipped or failed.");
    }
}

static async Task EnsureLeaveWithdrawAttendanceMigrationAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "db", "migrations", "013_leave_withdraw_attendance_status.sql");
            if (!File.Exists(path))
            {
                Serilog.Log.Warning("Migration 013 SQL not found at {Path}", path);
                return;
            }

            var sql = await File.ReadAllTextAsync(path);
            await conn.ExecuteAsync(sql);
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration 013 leave/withdraw/attendance skipped or failed.");
    }
}

static async Task EnsureCanEditPastAttendancePermissionAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
INSERT INTO permissions (id, school_id, is_deleted, created_at, created_by, name, description)
SELECT md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), '00000000-0000-0000-0000-000000000001'::uuid, 'CanEditPastAttendance', 'Edit student/staff attendance for past calendar dates (IST)'
WHERE NOT EXISTS (
    SELECT 1
    FROM permissions
    WHERE school_id IS NULL
      AND name = 'CanEditPastAttendance'
      AND is_deleted = FALSE
);

INSERT INTO role_permissions (id, school_id, is_deleted, created_at, created_by, role_id, permission_id)
SELECT
    md5(random()::text || clock_timestamp()::text)::uuid,
    NULL,
    FALSE,
    NOW(),
    '00000000-0000-0000-0000-000000000001'::uuid,
    r.id,
    p.id
FROM roles r
JOIN permissions p
  ON p.school_id IS NULL
 AND p.name = 'CanEditPastAttendance'
 AND p.is_deleted = FALSE
WHERE r.school_id IS NULL
  AND r.name = 'SchoolAdmin'
  AND r.is_deleted = FALSE
  AND NOT EXISTS (
      SELECT 1
      FROM role_permissions rp
      WHERE rp.role_id = r.id
        AND rp.permission_id = p.id
        AND rp.is_deleted = FALSE
  );");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration CanEditPastAttendance permission skipped or failed.");
    }
}

static async Task EnsureSchoolFeatureEntitlementsTableAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SchoolManagement.Application.Abstractions.IDbConnectionFactory>();
        var conn = await factory.CreateConnectionAsync();
        if (conn == null) return;
        using (conn)
        {
            await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS school_feature_entitlements (
    school_id UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    feature_key VARCHAR(80) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_by UUID NOT NULL,
    PRIMARY KEY (school_id, feature_key)
);
CREATE INDEX IF NOT EXISTS ix_school_feature_entitlements_school
ON school_feature_entitlements(school_id);");
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Startup migration school_feature_entitlements skipped or failed.");
    }
}

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public partial class Program { }
