using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Schools.Dtos;
using SchoolManagement.Application.Schools.Services;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Services;

public class SchoolService : ISchoolService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITenantContext _tenantContext;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ISchoolFeatureService _schoolFeatureService;
    private readonly ILogger<SchoolService> _logger;

    public SchoolService(
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        ITenantContext tenantContext,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ISchoolFeatureService schoolFeatureService,
        ILogger<SchoolService> logger)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _tenantContext = tenantContext;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _schoolFeatureService = schoolFeatureService;
        _logger = logger;
    }

    public async Task<ApiResponse<SchoolResponseDto>> RegisterSchoolAsync(RegisterSchoolRequestDto request)
    {
        if (request.SubscriptionPlanId == Guid.Empty)
            return ApiResponse<SchoolResponseDto>.Fail("Subscription plan is required during registration.", ErrorCodes.ValidationError);
        if (request.SubscriptionEndDate <= request.SubscriptionStartDate)
            return ApiResponse<SchoolResponseDto>.Fail("Subscription end date must be after start date.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_code", request.Code);
        p.Add("p_name", request.Name);
        p.Add("p_admin_name", request.AdminName);
        p.Add("p_admin_email", request.AdminEmail);
        p.Add("p_admin_password_hash", _passwordHasher.Hash(request.AdminPassword));
        p.Add("p_contact_phone", request.AdminMobile);
        p.Add("p_address_line", request.AddressLine);
        p.Add("p_created_by", Guid.Empty);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("CALL sp_school_register(@p_code,@p_name,@p_admin_name,@p_admin_email,@p_admin_password_hash,@p_contact_phone,@p_address_line,@p_created_by)", p, tx);

        var school = await conn.QueryFirstOrDefaultAsync<SchoolResponseDto>(
            @"SELECT id, code, name, admin_name AS AdminName, is_active AS IsActive,
                     is_soft_deleted AS IsSoftDeleted,
                     mobile_no AS ContactPhone,
                     address_line AS AddressLine,
                     @p_admin_email AS Email,
                     created_at AS CreatedAt,
                     0::numeric AS DueAmount,
                     NULL::text AS SubscriptionName,
                     NULL::timestamp AS SubscriptionEnd
              FROM schools
              WHERE LOWER(code) = LOWER(@p_code)
                AND is_deleted = FALSE",
            p, tx);
        if (school is null)
        {
            tx.Rollback();
            return ApiResponse<SchoolResponseDto>.Fail("School registration failed.", ErrorCodes.BusinessRule);
        }

        var startDate = DateTime.SpecifyKind(request.SubscriptionStartDate, DateTimeKind.Unspecified);
        var endDate = DateTime.SpecifyKind(request.SubscriptionEndDate, DateTimeKind.Unspecified);
        var pSub = new DynamicParameters();
        pSub.Add("p_school_id", school.Id);
        pSub.Add("p_subscription_plan_id", request.SubscriptionPlanId);
        pSub.Add("p_start_date", startDate);
        pSub.Add("p_end_date", endDate);
        pSub.Add("p_auto_renew", request.AutoRenew);
        pSub.Add("p_user_id", Guid.Empty);
        await conn.ExecuteAsync(
            "CALL sp_subscription_assign(@p_school_id,@p_subscription_plan_id,@p_start_date,@p_end_date,@p_auto_renew,@p_user_id)",
            pSub, tx);

        var schoolWithSubscription = await conn.QueryFirstOrDefaultAsync<SchoolResponseDto>(
            @"
            SELECT
                s.id,
                s.code,
                s.name,
                s.admin_name AS AdminName,
                s.is_active AS IsActive,
                s.is_soft_deleted AS IsSoftDeleted,
                s.mobile_no AS ContactPhone,
                s.address_line AS AddressLine,
                @p_admin_email AS Email,
                s.created_at AS CreatedAt,
                0::numeric AS DueAmount,
                sp.name AS SubscriptionName,
                ss.end_date AS SubscriptionEnd
            FROM schools s
            LEFT JOIN school_subscriptions ss
              ON ss.school_id_fk = s.id
             AND ss.is_deleted = FALSE
             AND ss.is_active = TRUE
            LEFT JOIN subscription_plans sp
              ON sp.id = ss.subscription_plan_id
             AND sp.is_deleted = FALSE
            WHERE s.id = @p_school_id
            LIMIT 1",
            new { p_school_id = school.Id, p_admin_email = request.AdminEmail }, tx);

        tx.Commit();
        var registered = schoolWithSubscription ?? school;
        try
        {
            await _schoolFeatureService.SeedDefaultsForNewSchoolAsync(registered.Id, Guid.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeedDefaultsForNewSchoolAsync failed for school {SchoolId}.", registered.Id);
        }

        await TrySendSchoolRegistrationEmailAsync(request, registered);
        return ApiResponse<SchoolResponseDto>.Ok(registered, "School registered.");
    }

    private async Task TrySendSchoolRegistrationEmailAsync(RegisterSchoolRequestDto request, SchoolResponseDto school)
    {
        try
        {
            var subject = "School Registration Successful";
            var body = $@"
<p>Hello {request.AdminName},</p>
<p>Your school has been successfully registered on the platform.</p>
<ul>
  <li><strong>School:</strong> {school.Name}</li>
  <li><strong>Code:</strong> {school.Code}</li>
  <li><strong>Subscription:</strong> {school.SubscriptionName ?? "Assigned"}</li>
  <li><strong>Subscription End:</strong> {(school.SubscriptionEnd?.ToString("yyyy-MM-dd") ?? "-")}</li>
</ul>
<p>Please login and complete onboarding.</p>";
            await _emailSender.SendAsync(request.AdminEmail, subject, body);
        }
        catch
        {
            // Best effort email; registration should not fail if SMTP is unavailable.
        }
    }

    public async Task<ApiResponse<SchoolResponseDto>> UpdateSchoolAsync(Guid schoolId, UpdateSchoolRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<SchoolResponseDto>.Fail("Code and name are required.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_code", request.Code.Trim());
        p.Add("p_name", request.Name.Trim());
        p.Add("p_admin_name", string.IsNullOrWhiteSpace(request.AdminName) ? null : request.AdminName.Trim());
        p.Add("p_admin_email", string.IsNullOrWhiteSpace(request.AdminEmail) ? null : request.AdminEmail.Trim());
        p.Add("p_contact_phone", string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim());
        p.Add("p_address_line", string.IsNullOrWhiteSpace(request.AddressLine) ? null : request.AddressLine.Trim());
        p.Add("p_user_id", Guid.Empty);

        try
        {
            var updated = await conn.QueryFirstOrDefaultAsync<SchoolResponseDto>(
                @"
                UPDATE schools
                SET code = @p_code,
                    name = @p_name,
                    admin_name = @p_admin_name,
                    mobile_no = @p_contact_phone,
                    address_line = @p_address_line,
                    updated_at = NOW(),
                    updated_by = @p_user_id
                WHERE id = @p_school_id
                  AND is_deleted = FALSE
                RETURNING
                    id,
                    code,
                    name,
                    admin_name AS AdminName,
                    is_active AS IsActive,
                    is_soft_deleted AS IsSoftDeleted,
                    mobile_no AS ContactPhone,
                    address_line AS AddressLine,
                    @p_admin_email AS Email,
                    created_at AS CreatedAt,
                    0::numeric AS DueAmount,
                    NULL::text AS SubscriptionName,
                    NULL::timestamp AS SubscriptionEnd;",
                p);

            if (updated is not null && !string.IsNullOrWhiteSpace(request.AdminEmail))
            {
                await conn.ExecuteAsync(
                    @"
                    UPDATE users u
                    SET email = @p_admin_email,
                        updated_at = NOW(),
                        updated_by = @p_user_id
                    WHERE u.id = (
                        SELECT u2.id
                        FROM users u2
                        JOIN roles r ON r.id = u2.role_id
                        WHERE u2.school_id = @p_school_id
                          AND u2.is_deleted = FALSE
                          AND r.is_deleted = FALSE
                          AND r.name = 'SchoolAdmin'
                        ORDER BY u2.created_at ASC
                        LIMIT 1
                    );",
                    p);
            }

            if (updated is null)
                return ApiResponse<SchoolResponseDto>.Fail("School not found.", ErrorCodes.NotFound);

            return ApiResponse<SchoolResponseDto>.Ok(updated, "School updated.");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SchoolResponseDto>.Fail("School code already exists.", ErrorCodes.Conflict);
        }
    }

    public async Task<ApiResponse<PagedResult<SubscriptionPlanResponseDto>>> GetSubscriptionPlansAsync(int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var offset = (page - 1) * pageSize;
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_limit", pageSize);
        p.Add("p_offset", offset);
        var items = await conn.QueryAsync<SubscriptionPlanResponseDto>(
            @"
            SELECT
                id,
                name,
                price_per_month AS PricePerMonth,
                max_students AS MaxStudents,
                max_staff AS MaxStaff,
                is_active AS IsActive
            FROM subscription_plans
            WHERE is_deleted = FALSE
            ORDER BY price_per_month ASC, created_at DESC
            LIMIT @p_limit OFFSET @p_offset;", p);
        var totalCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM subscription_plans WHERE is_deleted = FALSE");
        return ApiResponse<PagedResult<SubscriptionPlanResponseDto>>.Ok(new PagedResult<SubscriptionPlanResponseDto>
        {
            Items = items.AsList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SubscriptionPlanResponseDto>> CreateSubscriptionPlanAsync(CreateSubscriptionPlanRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Plan name is required.", ErrorCodes.ValidationError);
        if (request.PricePerMonth < 0)
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Price must be non-negative.", ErrorCodes.ValidationError);
        if (request.MaxStudents < 0 || request.MaxStaff < 0)
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Max students/staff must be non-negative.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_name", request.Name.Trim());
        p.Add("p_price_per_month", request.PricePerMonth);
        p.Add("p_max_students", request.MaxStudents);
        p.Add("p_max_staff", request.MaxStaff);
        p.Add("p_created_by", Guid.Empty);

        try
        {
            var item = await conn.QueryFirstOrDefaultAsync<SubscriptionPlanResponseDto>(
                @"
                INSERT INTO subscription_plans (
                    id, school_id, is_deleted, created_at, created_by, updated_at, updated_by,
                    name, price_per_month, max_students, max_staff, is_active
                )
                VALUES (
                    md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), @p_created_by, NULL, NULL,
                    @p_name, @p_price_per_month, @p_max_students, @p_max_staff, TRUE
                )
                RETURNING
                    id,
                    name,
                    price_per_month AS PricePerMonth,
                    max_students AS MaxStudents,
                    max_staff AS MaxStaff,
                    is_active AS IsActive;",
                p);

            return ApiResponse<SubscriptionPlanResponseDto>.Ok(item, "Subscription plan created.");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("A subscription plan with this name already exists.", ErrorCodes.Conflict);
        }
    }

    public async Task<ApiResponse<SubscriptionPlanResponseDto>> UpdateSubscriptionPlanAsync(Guid planId, UpdateSubscriptionPlanRequestDto request)
    {
        if (planId == Guid.Empty)
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Plan id is required.", ErrorCodes.ValidationError);
        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Plan name is required.", ErrorCodes.ValidationError);
        if (request.PricePerMonth < 0)
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Price must be non-negative.", ErrorCodes.ValidationError);
        if (request.MaxStudents < 0 || request.MaxStaff < 0)
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("Max students/staff must be non-negative.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_id", planId);
        p.Add("p_name", request.Name.Trim());
        p.Add("p_price_per_month", request.PricePerMonth);
        p.Add("p_max_students", request.MaxStudents);
        p.Add("p_max_staff", request.MaxStaff);
        p.Add("p_is_active", request.IsActive);
        p.Add("p_user_id", Guid.Empty);

        try
        {
            var item = await conn.QueryFirstOrDefaultAsync<SubscriptionPlanResponseDto>(
                @"
                UPDATE subscription_plans
                SET name = @p_name,
                    price_per_month = @p_price_per_month,
                    max_students = @p_max_students,
                    max_staff = @p_max_staff,
                    is_active = @p_is_active,
                    updated_at = NOW(),
                    updated_by = @p_user_id
                WHERE id = @p_id
                  AND is_deleted = FALSE
                RETURNING
                    id,
                    name,
                    price_per_month AS PricePerMonth,
                    max_students AS MaxStudents,
                    max_staff AS MaxStaff,
                    is_active AS IsActive;",
                p);

            if (item is null)
                return ApiResponse<SubscriptionPlanResponseDto>.Fail("Subscription plan not found.", ErrorCodes.NotFound);
            return ApiResponse<SubscriptionPlanResponseDto>.Ok(item, "Subscription plan updated.");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SubscriptionPlanResponseDto>.Fail("A subscription plan with this name already exists.", ErrorCodes.Conflict);
        }
    }

    public async Task<ApiResponse<object>> AssignSubscriptionAsync(Guid schoolId, AssignSubscriptionRequestDto request)
    {
        if (request.SubscriptionPlanId == Guid.Empty)
            return ApiResponse<object>.Fail("Subscription plan is required.", ErrorCodes.ValidationError);
        if (request.EndDate <= request.StartDate)
            return ApiResponse<object>.Fail("End date must be after start date.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_subscription_plan_id", request.SubscriptionPlanId);
        // sp_subscription_assign expects TIMESTAMP (without time zone).
        // Frontend sends ISO strings that deserialize as UTC DateTime; force Unspecified
        // so Npgsql binds as timestamp without time zone and matches procedure signature.
        var startDate = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Unspecified);
        var endDate = DateTime.SpecifyKind(request.EndDate, DateTimeKind.Unspecified);
        p.Add("p_start_date", startDate);
        p.Add("p_end_date", endDate);
        p.Add("p_auto_renew", request.AutoRenew);
        p.Add("p_user_id", Guid.Empty);

        await conn.ExecuteAsync(
            "CALL sp_subscription_assign(@p_school_id,@p_subscription_plan_id,@p_start_date,@p_end_date,@p_auto_renew,@p_user_id)",
            p);

        return ApiResponse<object>.Ok(null, "Subscription assigned.");
    }

    public async Task<ApiResponse<object>> RecordSubscriptionPaymentAsync(RecordSchoolSubscriptionPaymentRequestDto request)
    {
        if (request.SchoolId == Guid.Empty)
            return ApiResponse<object>.Fail("School is required.", ErrorCodes.ValidationError);
        if (request.AmountPaid <= 0)
            return ApiResponse<object>.Fail("Payment amount must be greater than zero.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", request.SchoolId);
        p.Add("p_amount_paid", request.AmountPaid);
        p.Add("p_payment_date", DateTime.SpecifyKind(request.PaymentDate == default ? DateTime.UtcNow : request.PaymentDate, DateTimeKind.Unspecified));
        p.Add("p_payment_mode", string.IsNullOrWhiteSpace(request.PaymentMode) ? null : request.PaymentMode.Trim());
        p.Add("p_reference_no", string.IsNullOrWhiteSpace(request.ReferenceNo) ? null : request.ReferenceNo.Trim());
        p.Add("p_remarks", string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim());
        p.Add("p_created_by", Guid.Empty);

        var activeSubscriptionId = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT id
              FROM school_subscriptions
              WHERE school_id_fk = @p_school_id
                AND is_deleted = FALSE
                AND is_active = TRUE
              ORDER BY created_at DESC
              LIMIT 1;", p);
        if (activeSubscriptionId is null)
            return ApiResponse<object>.Fail("No active subscription found for this school.", ErrorCodes.BusinessRule);

        p.Add("p_school_subscription_id", activeSubscriptionId.Value);
        var paymentId = await conn.ExecuteScalarAsync<Guid>(
            @"
            INSERT INTO school_subscription_payments (
                id, school_id, is_deleted, created_at, created_by,
                school_subscription_id, amount_paid, payment_date, payment_mode, reference_no, remarks
            )
            VALUES (
                md5(random()::text || clock_timestamp()::text)::uuid, @p_school_id, FALSE, NOW(), @p_created_by,
                @p_school_subscription_id, @p_amount_paid, @p_payment_date, @p_payment_mode, @p_reference_no, @p_remarks
            )
            RETURNING id;",
            p);

        var invoiceResult = await TrySendSubscriptionPaymentInvoiceEmailAsync(
            request.SchoolId,
            paymentId,
            request.AmountPaid,
            request.PaymentDate == default ? DateTime.UtcNow : request.PaymentDate,
            request.PaymentMode,
            request.ReferenceNo);

        var paymentMessage = invoiceResult.Sent
            ? $"Subscription payment recorded. Invoice emailed to {invoiceResult.SentToEmail}."
            : $"Subscription payment recorded. Invoice was not emailed: {invoiceResult.FailureReason}";

        return ApiResponse<object>.Ok(null, paymentMessage);
    }

    private sealed class SubscriptionInvoiceRecipientRow
    {
        public string SchoolName { get; set; } = string.Empty;
        public string SchoolCode { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    private async Task<(bool Sent, string? SentToEmail, string FailureReason)> TrySendSubscriptionPaymentInvoiceEmailAsync(
        Guid schoolId,
        Guid paymentId,
        decimal amountPaid,
        DateTime paymentDate,
        string? paymentMode,
        string? referenceNo)
    {
        if (!_emailOptions.Value.Enabled)
        {
            _logger.LogWarning("Invoice email skipped: Email:Enabled is false.");
            return (false, null, "Email is turned off (set Email:Enabled to true and configure SMTP).");
        }

        try
        {
            using var conn = await _connectionFactory.CreateConnectionAsync();
            var school = await conn.QueryFirstOrDefaultAsync<SubscriptionInvoiceRecipientRow>(
                @"
                SELECT
                    s.name AS SchoolName,
                    s.code AS SchoolCode,
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
                FROM schools s
                WHERE s.id = @p_school_id
                LIMIT 1;",
                new { p_school_id = schoolId });
            if (school is null)
            {
                _logger.LogWarning("Invoice email skipped: school {SchoolId} not found.", schoolId);
                return (false, null, "School not found.");
            }

            var email = school.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Invoice email skipped: no SchoolAdmin or school email for school {SchoolId}.", schoolId);
                return (false, null, "No school admin or school email on file. Update the SchoolAdmin user email or school email.");
            }

            var invoiceNo = $"SUB-INV-{DateTime.UtcNow:yyyyMMdd}-{paymentId.ToString()[..8].ToUpperInvariant()}";
            var subject = $"Subscription payment receipt - {invoiceNo}";
            var body = $@"
<p>Hello,</p>
<p>We have received your subscription payment.</p>
<ul>
  <li><strong>Invoice No:</strong> {invoiceNo}</li>
  <li><strong>School:</strong> {school.SchoolName} ({school.SchoolCode})</li>
  <li><strong>Amount Paid:</strong> {amountPaid:0.00}</li>
  <li><strong>Payment Date:</strong> {paymentDate:yyyy-MM-dd}</li>
  <li><strong>Payment Mode:</strong> {paymentMode ?? "-"}</li>
  <li><strong>Reference:</strong> {referenceNo ?? "-"}</li>
</ul>
<p>Please find attached invoice copy.</p>";

            var invoicePdf = GenerateSubscriptionInvoicePdf(
                invoiceNo,
                school.SchoolName,
                school.SchoolCode,
                amountPaid,
                paymentDate,
                paymentMode,
                referenceNo);

            await _emailSender.SendAsync(
                email,
                subject,
                body,
                [
                    new EmailAttachment
                    {
                        FileName = $"{invoiceNo}.pdf",
                        ContentType = "application/pdf",
                        Content = invoicePdf
                    }
                ]);

            return (true, email, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice email failed for school {SchoolId}.", schoolId);
            return (false, null, ex.Message);
        }
    }

    private static byte[] GenerateSubscriptionInvoicePdf(
    string invoiceNo,
    string schoolName,
    string schoolCode,
    decimal amountPaid,
    DateTime paymentDate,
    string? paymentMode,
    string? referenceNo)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        decimal gstRate = 0.18m;
        decimal gstAmount = amountPaid * gstRate;
        decimal cgst = gstAmount / 2;
        decimal sgst = gstAmount / 2;
        decimal total = amountPaid + gstAmount;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(10));

                // 🔹 HEADER
                page.Header().Row(row =>
                {
                    // Logo + Company Details
                    row.RelativeItem().Row(r =>
                    {
                        //r.ConstantItem(60).Image("wwwroot/logo.png", ImageScaling.FitArea);

                        r.RelativeItem().Column(col =>
                        {
                            col.Item().Text("SkoolManage")
                                .Bold().FontSize(14);

                            col.Item().Text("School Management ERP Solution");
                            col.Item().Text("Address: Mathura, Uttar Pradesh");
                            col.Item().Text("Mobile: +91-8439120410");
                            col.Item().Text("Email: skoolmanage.platform@gmail.com");
                        });
                    });

                    // Invoice Info
                    row.ConstantItem(200).Column(col =>
                    {
                        col.Item().AlignRight().Text("INVOICE")
                            .Bold().FontSize(18);

                        col.Item().AlignRight().Text($"Invoice No: {invoiceNo}");
                        col.Item().AlignRight().Text($"Date: {paymentDate:dd-MM-yyyy}");
                    });
                });

                // 🔹 CONTENT
                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().LineHorizontal(1);

                    // Bill To
                    col.Item().Text("Bill To").Bold().FontSize(12);
                    col.Item().Text($"{schoolName} ({schoolCode})");

                    col.Item().LineHorizontal(1);

                    // Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("Description").Bold();
                            header.Cell().Element(CellStyle).AlignCenter().Text("Qty").Bold();
                            header.Cell().Element(CellStyle).AlignRight().Text("Rate").Bold();
                            header.Cell().Element(CellStyle).AlignRight().Text("Amount").Bold();
                        });

                        table.Cell().Element(CellStyle).Text("ERP Subscription Fee");
                        table.Cell().Element(CellStyle).AlignCenter().Text("1");
                        table.Cell().Element(CellStyle).AlignRight().Text($"{amountPaid:0.00}");
                        table.Cell().Element(CellStyle).AlignRight().Text($"{amountPaid:0.00}");
                    });

                    // 🔹 GST Breakdown
                    col.Item().AlignRight().Column(c =>
                    {
                        c.Item().Text($"Subtotal: ₹ {amountPaid:0.00}");
                        c.Item().Text($"CGST (9%): ₹ {cgst:0.00}");
                        c.Item().Text($"SGST (9%): ₹ {sgst:0.00}");
                        c.Item().Text($"Discount: ₹ {cgst + sgst:00}");
                        c.Item().Text($"Total: ₹ {total-(cgst + sgst):0.00}")
                            .Bold().FontSize(12);
                    });

                    col.Item().LineHorizontal(1);

                    // 🔹 Payment Details
                    col.Item().Text("Payment Details").Bold();
                    col.Item().Text($"Mode: {paymentMode ?? "-"}");
                    col.Item().Text($"Reference No: {referenceNo ?? "-"}");


                    col.Item().LineHorizontal(1);

                    // 🔹 Terms & Conditions
                    col.Item().Text("Terms & Conditions").Bold();
                    col.Item().Text("1. Subscription is valid for one month.");
                    col.Item().Text("2. Fees once paid are non-refundable.");
                    col.Item().Text("3. Late renewal may incur extra charges.");
                    col.Item().Text("4. Subject to jurisdiction of Mathura, UP.");

                    col.Item().LineHorizontal(1);

                    col.Item().Text("This is a computer-generated invoice.")
                        .FontSize(9);
                });

                // 🔹 FOOTER
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf();

        static IContainer CellStyle(IContainer container)
        {
            return container.Border(1).Padding(5);
        }
    }
    private sealed class SubscriptionReminderRow
    {
        public string? Email { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public decimal DueAmount { get; set; }
    }

    public async Task<ApiResponse<object>> SendSubscriptionReminderAsync(SubscriptionReminderRequestDto request)
    {
        if (request.SchoolId == Guid.Empty)
            return ApiResponse<object>.Fail("School is required.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var school = await conn.QueryFirstOrDefaultAsync<SubscriptionReminderRow>(
            @"
            SELECT
                s.id,
                s.name AS Name,
                s.code AS Code,
                COALESCE((
                    SELECT u.email
                    FROM users u
                    JOIN roles r ON r.id = u.role_id
                    WHERE u.school_id = s.id
                      AND u.is_deleted = FALSE
                      AND r.is_deleted = FALSE
                      AND r.name = 'SchoolAdmin'
                    ORDER BY u.created_at ASC
                    LIMIT 1
                ), s.email, '') AS Email,
                COALESCE((
                    SELECT GREATEST(
                        COALESCE(sp.price_per_month, 0::numeric) - COALESCE(SUM(ssp.amount_paid), 0::numeric),
                        0::numeric
                    )
                    FROM school_subscriptions ss
                    LEFT JOIN subscription_plans sp ON sp.id = ss.subscription_plan_id AND sp.is_deleted = FALSE
                    LEFT JOIN school_subscription_payments ssp ON ssp.school_subscription_id = ss.id AND ssp.is_deleted = FALSE
                    WHERE ss.school_id_fk = s.id AND ss.is_deleted = FALSE AND ss.is_active = TRUE
                    GROUP BY sp.price_per_month
                    LIMIT 1
                ), 0::numeric) AS DueAmount
            FROM schools s
            WHERE s.id = @p_school_id
              AND s.is_deleted = FALSE",
            new { p_school_id = request.SchoolId });
        if (school is null)
            return ApiResponse<object>.Fail("School not found.", ErrorCodes.NotFound);

        var email = school.Email;
        if (string.IsNullOrWhiteSpace(email))
            return ApiResponse<object>.Fail("No school admin email found.", ErrorCodes.BusinessRule);

        var dueAmount = school.DueAmount;
        var subject = "Subscription Payment Reminder";
        var message = string.IsNullOrWhiteSpace(request.Message)
            ? $"Your school subscription payment is pending. Due amount: {dueAmount:0.00}."
            : request.Message!.Trim();
        var body = $@"
<p>Hello,</p>
<p>{message}</p>
<ul>
  <li><strong>School:</strong> {school.Name}</li>
  <li><strong>Code:</strong> {school.Code}</li>
  <li><strong>Due Amount:</strong> {dueAmount:0.00}</li>
</ul>
<p>Please clear the due at the earliest.</p>";

        try
        {
            await _emailSender.SendAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            return ApiResponse<object>.Fail(
                $"Reminder could not be sent: {ex.Message}",
                ErrorCodes.BusinessRule);
        }

        return ApiResponse<object>.Ok(null, "Reminder email sent.");
    }

    public async Task<ApiResponse<PagedResult<SchoolSubscriptionPaymentResponseDto>>> GetSubscriptionPaymentsAsync(Guid? schoolId = null, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var offset = (page - 1) * pageSize;
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_limit", pageSize);
        p.Add("p_offset", offset);
        var items = await conn.QueryAsync<SchoolSubscriptionPaymentResponseDto>(
            @"
            SELECT
                p.id,
                p.school_id AS SchoolId,
                s.name AS SchoolName,
                p.school_subscription_id AS SchoolSubscriptionId,
                p.amount_paid AS AmountPaid,
                p.payment_date AS PaymentDate,
                p.payment_mode AS PaymentMode,
                p.reference_no AS ReferenceNo,
                p.remarks AS Remarks,
                p.created_at AS CreatedAt
            FROM school_subscription_payments p
            JOIN schools s ON s.id = p.school_id AND s.is_deleted = FALSE
            WHERE p.is_deleted = FALSE
              AND ((@p_school_id)::uuid IS NULL OR p.school_id = (@p_school_id)::uuid)
            ORDER BY p.payment_date DESC, p.created_at DESC
            LIMIT @p_limit OFFSET @p_offset;",
            p);
        var totalCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM school_subscription_payments p
              WHERE p.is_deleted = FALSE
                AND ((@p_school_id)::uuid IS NULL OR p.school_id = (@p_school_id)::uuid);",
            p);
        return ApiResponse<PagedResult<SchoolSubscriptionPaymentResponseDto>>.Ok(new PagedResult<SchoolSubscriptionPaymentResponseDto>
        {
            Items = items.AsList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<SuperAdminExpenseResponseDto>>> GetExpensesAsync(int page = 1, int pageSize = 20, string? search = null, int? month = null, int? year = null)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var offset = (page - 1) * pageSize;
        var cleanedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedMonth = month is >= 1 and <= 12 ? month : null;
        var normalizedYear = year is >= 2000 and <= 9999 ? year : null;

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_limit", pageSize);
        p.Add("p_offset", offset);
        var where = "WHERE e.is_deleted = FALSE";
        if (!string.IsNullOrWhiteSpace(cleanedSearch))
        {
            where += @"
              AND (
                    e.expense_type ILIKE ('%' || @p_search || '%')
                 OR COALESCE(e.vendor, '') ILIKE ('%' || @p_search || '%')
                 OR COALESCE(e.remarks, '') ILIKE ('%' || @p_search || '%')
              )";
            p.Add("p_search", cleanedSearch);
        }
        if (normalizedMonth.HasValue)
        {
            where += " AND EXTRACT(MONTH FROM e.expense_date)::int = @p_month";
            p.Add("p_month", normalizedMonth.Value);
        }
        if (normalizedYear.HasValue)
        {
            where += " AND EXTRACT(YEAR FROM e.expense_date)::int = @p_year";
            p.Add("p_year", normalizedYear.Value);
        }

        var itemsSql = $@"
            SELECT
                e.id,
                e.expense_type AS ExpenseType,
                e.frequency AS Frequency,
                e.amount AS Amount,
                e.expense_date AS ExpenseDate,
                e.vendor AS Vendor,
                e.remarks AS Remarks,
                e.created_at AS CreatedAt
            FROM superadmin_expenses e
            {where}
            ORDER BY e.expense_date DESC, e.created_at DESC
            LIMIT @p_limit OFFSET @p_offset;";

        var countSql = $@"
            SELECT COUNT(*)
            FROM superadmin_expenses e
            {where};";

        var items = await conn.QueryAsync<SuperAdminExpenseResponseDto>(itemsSql, p);
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);

        return ApiResponse<PagedResult<SuperAdminExpenseResponseDto>>.Ok(new PagedResult<SuperAdminExpenseResponseDto>
        {
            Items = items.AsList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SuperAdminExpenseResponseDto>> CreateExpenseAsync(CreateSuperAdminExpenseRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ExpenseType))
            return ApiResponse<SuperAdminExpenseResponseDto>.Fail("Expense type is required.", ErrorCodes.ValidationError);
        if (request.Amount <= 0)
            return ApiResponse<SuperAdminExpenseResponseDto>.Fail("Amount must be greater than zero.", ErrorCodes.ValidationError);

        var allowedFrequency = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Monthly", "Annually", "OneTime" };
        if (string.IsNullOrWhiteSpace(request.Frequency) || !allowedFrequency.Contains(request.Frequency))
            return ApiResponse<SuperAdminExpenseResponseDto>.Fail("Frequency must be Monthly, Annually, or OneTime.", ErrorCodes.ValidationError);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_expense_type", request.ExpenseType.Trim());
        p.Add("p_frequency", request.Frequency.Trim());
        p.Add("p_amount", request.Amount);
        p.Add("p_expense_date", DateTime.SpecifyKind(request.ExpenseDate == default ? DateTime.UtcNow : request.ExpenseDate, DateTimeKind.Unspecified));
        p.Add("p_vendor", string.IsNullOrWhiteSpace(request.Vendor) ? null : request.Vendor.Trim());
        p.Add("p_remarks", string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim());
        p.Add("p_created_by", Guid.Empty);

        var created = await conn.QueryFirstOrDefaultAsync<SuperAdminExpenseResponseDto>(
            @"
            INSERT INTO superadmin_expenses (
                id, school_id, is_deleted, created_at, created_by, updated_at, updated_by,
                expense_type, frequency, amount, expense_date, vendor, remarks
            )
            VALUES (
                md5(random()::text || clock_timestamp()::text)::uuid, NULL, FALSE, NOW(), @p_created_by, NULL, NULL,
                @p_expense_type, @p_frequency, @p_amount, @p_expense_date, @p_vendor, @p_remarks
            )
            RETURNING
                id,
                expense_type AS ExpenseType,
                frequency AS Frequency,
                amount AS Amount,
                expense_date AS ExpenseDate,
                vendor AS Vendor,
                remarks AS Remarks,
                created_at AS CreatedAt;",
            p);

        if (created is null)
            return ApiResponse<SuperAdminExpenseResponseDto>.Fail("Failed to create expense.", ErrorCodes.BusinessRule);

        return ApiResponse<SuperAdminExpenseResponseDto>.Ok(created, "Expense created.");
    }

    public async Task<ApiResponse<SuperAdminExpenseSummaryDto>> GetExpenseSummaryAsync(int? month = null, int? year = null)
    {
        var normalizedMonth = month is >= 1 and <= 12 ? month : null;
        var normalizedYear = year is >= 2000 and <= 9999 ? year : null;

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_month", normalizedMonth);
        p.Add("p_year", normalizedYear);

        var totalCollection = await conn.ExecuteScalarAsync<decimal>(
            @"
            SELECT COALESCE(SUM(amount_paid), 0::numeric)
            FROM school_subscription_payments
            WHERE is_deleted = FALSE
              AND ((@p_month)::int IS NULL OR EXTRACT(MONTH FROM payment_date)::int = (@p_month)::int)
              AND ((@p_year)::int IS NULL OR EXTRACT(YEAR FROM payment_date)::int = (@p_year)::int);",
            p);

        var totalExpenses = await conn.ExecuteScalarAsync<decimal>(
            @"
            SELECT COALESCE(SUM(amount), 0::numeric)
            FROM superadmin_expenses
            WHERE is_deleted = FALSE
              AND ((@p_month)::int IS NULL OR EXTRACT(MONTH FROM expense_date)::int = (@p_month)::int)
              AND ((@p_year)::int IS NULL OR EXTRACT(YEAR FROM expense_date)::int = (@p_year)::int);",
            p);

        return ApiResponse<SuperAdminExpenseSummaryDto>.Ok(new SuperAdminExpenseSummaryDto
        {
            TotalCollection = totalCollection,
            TotalExpenses = totalExpenses,
            NetProfitLoss = totalCollection - totalExpenses
        });
    }

    public async Task<ApiResponse<SuperAdminDashboardSummaryDto>> GetDashboardSummaryAsync()
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var summary = await conn.QueryFirstOrDefaultAsync<SuperAdminDashboardSummaryDto>(
            @"
            SELECT
                COALESCE((
                    SELECT COUNT(*)
                    FROM schools
                    WHERE is_deleted = FALSE
                ), 0)::int AS TotalSchoolRegistered,
                COALESCE((
                    SELECT SUM(
                        GREATEST(
                            COALESCE(sp.price_per_month, 0::numeric) - COALESCE(paid.total_paid, 0::numeric),
                            0::numeric
                        )
                    )
                    FROM school_subscriptions ss
                    LEFT JOIN subscription_plans sp
                      ON sp.id = ss.subscription_plan_id
                     AND sp.is_deleted = FALSE
                    LEFT JOIN (
                        SELECT school_subscription_id, SUM(amount_paid) AS total_paid
                        FROM school_subscription_payments
                        WHERE is_deleted = FALSE
                        GROUP BY school_subscription_id
                    ) paid ON paid.school_subscription_id = ss.id
                    WHERE ss.is_deleted = FALSE
                      AND ss.is_active = TRUE
                ), 0::numeric) AS TotalPendingAmount,
                COALESCE((
                    SELECT SUM(amount_paid)
                    FROM school_subscription_payments
                    WHERE is_deleted = FALSE
                ), 0::numeric) AS TotalCollection,
                COALESCE((
                    SELECT COUNT(*)
                    FROM students
                    WHERE is_deleted = FALSE
                ), 0)::int AS TotalStudents,
                COALESCE((
                    SELECT COUNT(*)
                    FROM staff
                    WHERE is_deleted = FALSE
                ), 0)::int AS TotalStaff;");
        return ApiResponse<SuperAdminDashboardSummaryDto>.Ok(summary ?? new SuperAdminDashboardSummaryDto());
    }

    public async Task<ApiResponse<object>> ActivateSchoolAsync(Guid schoolId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_user_id", Guid.Empty);
        await conn.ExecuteAsync("CALL sp_school_activate(@p_school_id,@p_user_id)", p);
        return ApiResponse<object>.Ok(null, "School activated.");
    }

    public async Task<ApiResponse<object>> DeactivateSchoolAsync(Guid schoolId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_user_id", Guid.Empty);
        await conn.ExecuteAsync("CALL sp_school_deactivate(@p_school_id,@p_user_id)", p);
        return ApiResponse<object>.Ok(null, "School deactivated.");
    }

    public async Task<ApiResponse<object>> SoftDeleteSchoolAsync(Guid schoolId)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", schoolId);
        p.Add("p_user_id", Guid.Empty);
        await conn.ExecuteAsync("CALL sp_school_soft_delete(@p_school_id,@p_user_id)", p);
        return ApiResponse<object>.Ok(null, "School soft deleted.");
    }

    public async Task<ApiResponse<PagedResult<SchoolResponseDto>>> GetSchoolsAsync(int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var offset = (page - 1) * pageSize;
        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_limit", pageSize);
        p.Add("p_offset", offset);
        var items = await conn.QueryAsync<SchoolResponseDto>(
            @"
            SELECT
                s.id,
                s.code,
                s.name,
                s.admin_name AS AdminName,
                s.is_active AS IsActive,
                s.is_soft_deleted AS IsSoftDeleted,
                s.mobile_no AS ContactPhone,
                s.address_line AS AddressLine,
                (
                    SELECT u.email
                    FROM users u
                    JOIN roles r ON r.id = u.role_id
                    WHERE u.school_id = s.id
                      AND u.is_deleted = FALSE
                      AND r.is_deleted = FALSE
                      AND r.name = 'SchoolAdmin'
                    ORDER BY u.created_at ASC
                    LIMIT 1
                ) AS Email,
                s.created_at AS CreatedAt,
                CASE
                    WHEN ss.id IS NULL THEN 0::numeric
                    ELSE GREATEST(
                        COALESCE(sp.price_per_month, 0::numeric) - COALESCE((
                            SELECT SUM(ssp.amount_paid)
                            FROM school_subscription_payments ssp
                            WHERE ssp.school_subscription_id = ss.id
                              AND ssp.is_deleted = FALSE
                        ), 0::numeric),
                        0::numeric
                    )
                END AS DueAmount,
                sp.name AS SubscriptionName,
                ss.end_date AS SubscriptionEnd
            FROM schools s
            LEFT JOIN school_subscriptions ss
              ON ss.school_id_fk = s.id
             AND ss.is_deleted = FALSE
             AND ss.is_active = TRUE
            LEFT JOIN subscription_plans sp
              ON sp.id = ss.subscription_plan_id
             AND sp.is_deleted = FALSE
            WHERE s.is_deleted = FALSE
            ORDER BY s.created_at DESC
            LIMIT @p_limit OFFSET @p_offset", p);
        var totalCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM schools s WHERE s.is_deleted = FALSE");
        return ApiResponse<PagedResult<SchoolResponseDto>>.Ok(new PagedResult<SchoolResponseDto>
        {
            Items = items.AsList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SchoolResponseDto>> GetCurrentSchoolAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<SchoolResponseDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);

        var school = await conn.QueryFirstOrDefaultAsync<SchoolResponseDto>(
            @"
            SELECT
                s.id,
                s.code,
                s.name,
                s.email,
                s.admin_name AS AdminName,
                s.is_active AS IsActive,
                s.mobile_no AS ContactPhone,
                s.address_line AS AddressLine,
                sp.name AS SubscriptionName,
                ss.end_date AS SubscriptionEnd
            FROM schools s
            LEFT JOIN school_subscriptions ss
              ON ss.school_id_fk = s.id
             AND ss.is_deleted = FALSE
             AND ss.is_active = TRUE
            LEFT JOIN subscription_plans sp
              ON sp.id = ss.subscription_plan_id
             AND sp.is_deleted = FALSE
            WHERE s.id = @p_school_id
              AND s.is_deleted = FALSE
            LIMIT 1", p);

        if (school is null)
            return ApiResponse<SchoolResponseDto>.Fail("School not found.", ErrorCodes.NotFound);

        return ApiResponse<SchoolResponseDto>.Ok(school);
    }
}

