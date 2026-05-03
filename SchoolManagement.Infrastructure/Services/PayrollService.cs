using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Payroll.Dtos;
using SchoolManagement.Application.Payroll.Services;
using SchoolManagement.Application.Notifications.Services;

namespace SchoolManagement.Infrastructure.Services;

    public class PayrollService : IPayrollService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<PayrollService> _logger;

    public PayrollService(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        INotificationService notifications,
        IEmailSender emailSender,
        ILogger<PayrollService> logger)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<ApiResponse<object>> GeneratePayrollAsync(GeneratePayrollRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        if (request.Month is < 1 or > 12)
            return ApiResponse<object>.Fail("Invalid month.", ErrorCodes.Validation);
        if (request.Year is < 2000 or > 2100)
            return ApiResponse<object>.Fail("Invalid year.", ErrorCodes.Validation);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_month", request.Month);
        p.Add("p_year", request.Year);
        p.Add("p_generated_on", DateTime.UtcNow);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        var schoolId = _tenantContext.SchoolId.Value;
        var createdBy = _currentUser.UserId ?? Guid.Empty;

        if (request.GenerateForAllStaff || request.StaffId is null || request.StaffId == Guid.Empty)
        {
            await conn.ExecuteAsync(
                "CALL sp_payroll_generate_all(@p_school_id,@p_month,@p_year,@p_generated_on,@p_created_by)",
                p);

            var userIds = (await conn.QueryAsync<Guid>(@"
SELECT DISTINCT u.id
FROM payroll_records pr
INNER JOIN staff s ON s.id = pr.staff_id AND s.school_id = pr.school_id AND s.is_deleted = FALSE
INNER JOIN users u ON u.school_id = s.school_id
  AND LOWER(TRIM(u.email)) = LOWER(TRIM(s.email))
  AND u.is_deleted = FALSE
WHERE pr.school_id = @SchoolId
  AND pr.month = @Month
  AND pr.year = @Year
  AND pr.is_deleted = FALSE",
                new { SchoolId = schoolId, Month = request.Month, Year = request.Year })).ToList();

            var title = "Payroll generated";
            var msg = $"Payroll for {request.Month:D2}/{request.Year} has been generated.";
            foreach (var uid in userIds)
            {
                await _notifications.CreateAsync(uid, schoolId, title, msg, createdBy, "/payroll");
            }
            await SendPayslipsForGeneratedPayrollAsync(conn, schoolId, request.Month, request.Year, null);

            return ApiResponse<object>.Ok(null, "Payroll generated for all staff with salary structures.");
        }

        p.Add("p_staff_id", request.StaffId.Value);

        await conn.ExecuteAsync(
            "CALL sp_payroll_generate(@p_school_id,@p_staff_id,@p_month,@p_year,@p_generated_on,@p_created_by)",
            p);

        var singleUserId = await ResolveUserIdForStaffAsync(conn, schoolId, request.StaffId.Value);
        if (singleUserId != null)
        {
            await _notifications.CreateAsync(
                singleUserId.Value,
                schoolId,
                "Payroll generated",
                $"Payroll for {request.Month:D2}/{request.Year} has been generated for you.",
                createdBy,
                "/payroll");
        }
        await SendPayslipsForGeneratedPayrollAsync(conn, schoolId, request.Month, request.Year, request.StaffId.Value);

        return ApiResponse<object>.Ok(null, "Payroll generated for staff.");
    }

    public async Task<ApiResponse<object>> RecordStaffPaymentAsync(StaffPaymentRequestDto request)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<object>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);
        if (request.AmountPaid <= 0)
            return ApiResponse<object>.Fail("Amount must be positive.", ErrorCodes.Validation);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_payroll_record_id", request.PayrollRecordId);
        p.Add("p_staff_id", request.StaffId);
        p.Add("p_amount_paid", request.AmountPaid);
        p.Add("p_payment_date", request.PaymentDate == default ? DateTime.UtcNow : request.PaymentDate);
        p.Add("p_payment_mode", request.PaymentMode);
        p.Add("p_reference_no", request.ReferenceNo);
        p.Add("p_remarks", request.Remarks);
        p.Add("p_created_by", _currentUser.UserId ?? Guid.Empty);

        await conn.ExecuteAsync(
            "CALL sp_staff_payment_add(@p_school_id,@p_payroll_record_id,@p_staff_id,@p_amount_paid,@p_payment_date,@p_payment_mode,@p_reference_no,@p_remarks,@p_created_by)",
            p);

        var paySchoolId = _tenantContext.SchoolId.Value;
        var payUserId = await ResolveUserIdForStaffAsync(conn, paySchoolId, request.StaffId);
        if (payUserId != null && _currentUser.UserId != null)
        {
            await _notifications.CreateAsync(
                payUserId.Value,
                paySchoolId,
                "Salary payment recorded",
                $"A payment of {request.AmountPaid:F2} was recorded for your payroll.",
                _currentUser.UserId.Value,
                "/payroll");
        }

        return ApiResponse<object>.Ok(null, "Payment recorded.");
    }

    public async Task<ApiResponse<StaffSalaryPaymentHistoryResultDto>> GetStaffSalaryPaymentHistoryAsync(
        int? year,
        int? month,
        Guid? staffId,
        int page,
        int pageSize,
        string? search)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        var perms = _currentUser.Permissions ?? Array.Empty<string>();
        // School-wide history: .All / admin / pay-all. "Payroll.History.View" (no .All) = own rows only (like Payroll.View).
        var canViewAll = perms.Contains("Payroll.History.View.All")
                         || perms.Contains("Payroll.Pay.All")
                         || perms.Contains("Payroll.View.All")
                         || perms.Contains("Staff.Manage");
        var canViewOwn = perms.Contains("Payroll.View")
                         || perms.Contains("Payroll.History.View")
                         || perms.Contains("Reporting.Staff.Payroll");
        if (!canViewAll && !canViewOwn)
            return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Fail("You do not have permission to view salary payment history.", ErrorCodes.Forbidden);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        Guid? effectiveStaffId = staffId;
        if (!canViewAll && canViewOwn)
        {
            var ownId = await ResolveCurrentStaffIdAsync(conn);
            if (ownId is null)
                return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Ok(new StaffSalaryPaymentHistoryResultDto
                {
                    Page = page,
                    PageSize = pageSize
                });
            effectiveStaffId = ownId;
        }

        if (year == 0)
            year = null;
        if (month == 0)
            month = null;

        if (year is < 2000 or > 2100)
            return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Fail("Invalid year filter.", ErrorCodes.Validation);
        if (month is < 1 or > 12)
            return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Fail("Invalid month filter.", ErrorCodes.Validation);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var schoolId = _tenantContext.SchoolId.Value;
        // Never pass NULL for search text — PG cannot infer type for $n in "IS NULL OR ... ILIKE @p".
        var searchPattern = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : $"%{search.Trim()}%";

        // COALESCE(@Year, pr.year) avoids untyped NULL parameters (42P08 with Npgsql).
        const string whereSql = @"
WHERE sp.school_id = @SchoolId
  AND sp.is_deleted = FALSE
  AND st.is_deleted = FALSE
  AND pr.is_deleted = FALSE
  AND pr.year = COALESCE(@Year::integer, pr.year)
  AND pr.month = COALESCE(@Month::integer, pr.month)
  AND sp.staff_id = COALESCE(@StaffId::uuid, sp.staff_id)
  AND (@SearchPattern = '' OR st.full_name ILIKE @SearchPattern OR st.staff_code ILIKE @SearchPattern)";

        var countSql = $@"
SELECT COUNT(1)
FROM staff_payments sp
INNER JOIN staff st ON st.id = sp.staff_id AND st.school_id = sp.school_id
INNER JOIN payroll_records pr ON pr.id = sp.payroll_record_id AND pr.school_id = sp.school_id
{whereSql}";

        var listSql = $@"
SELECT
    sp.id AS Id,
    sp.payroll_record_id AS PayrollRecordId,
    sp.staff_id AS StaffId,
    st.staff_code AS StaffCode,
    st.full_name AS FullName,
    pr.month AS Month,
    pr.year AS Year,
    sp.amount_paid AS AmountPaid,
    sp.payment_date AS PaymentDate,
    sp.payment_mode AS PaymentMode,
    sp.reference_no AS ReferenceNo,
    sp.remarks AS Remarks,
    sp.created_at AS RecordedAt
FROM staff_payments sp
INNER JOIN staff st ON st.id = sp.staff_id AND st.school_id = sp.school_id
INNER JOIN payroll_records pr ON pr.id = sp.payroll_record_id AND pr.school_id = sp.school_id
{whereSql}
ORDER BY sp.payment_date DESC, sp.created_at DESC
OFFSET @Offset LIMIT @Limit";

        var dp = new DynamicParameters();
        dp.Add("SchoolId", schoolId, DbType.Guid);
        dp.Add("Year", year, DbType.Int32);
        dp.Add("Month", month, DbType.Int32);
        dp.Add("StaffId", effectiveStaffId, DbType.Guid);
        dp.Add("SearchPattern", searchPattern, DbType.String);
        dp.Add("Offset", offset, DbType.Int32);
        dp.Add("Limit", pageSize, DbType.Int32);

        var total = await conn.ExecuteScalarAsync<int>(countSql, dp);
        var rows = (await conn.QueryAsync<StaffSalaryPaymentHistoryItemDto>(listSql, dp)).ToList();

        return ApiResponse<StaffSalaryPaymentHistoryResultDto>.Ok(new StaffSalaryPaymentHistoryResultDto
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Items = rows
        });
    }

    private static async Task<Guid?> ResolveUserIdForStaffAsync(System.Data.IDbConnection conn, Guid schoolId, Guid staffId)
    {
        const string sql = @"
SELECT u.id
FROM staff s
INNER JOIN users u ON u.school_id = s.school_id
  AND LOWER(TRIM(u.email)) = LOWER(TRIM(s.email))
  AND u.is_deleted = FALSE
WHERE s.school_id = @SchoolId AND s.id = @StaffId AND s.is_deleted = FALSE
LIMIT 1";
        return await conn.ExecuteScalarAsync<Guid?>(sql, new { SchoolId = schoolId, StaffId = staffId });
    }

    private async Task<Guid?> ResolveCurrentStaffIdAsync(System.Data.IDbConnection conn)
    {
        if (_tenantContext.SchoolId == null || _currentUser.UserId == null)
            return null;

        const string userSql = @"
SELECT email
FROM users
WHERE id = @UserId
  AND school_id = @SchoolId
  AND is_deleted = FALSE
LIMIT 1;";
        var email = await conn.ExecuteScalarAsync<string?>(
            userSql,
            new
            {
                UserId = _currentUser.UserId.Value,
                SchoolId = _tenantContext.SchoolId.Value
            });

        if (string.IsNullOrWhiteSpace(email))
            return null;

        const string staffSql = @"
SELECT id
FROM staff
WHERE school_id = @SchoolId
  AND LOWER(email) = LOWER(@Email)
  AND is_deleted = FALSE
LIMIT 1;";
        return await conn.ExecuteScalarAsync<Guid?>(
            staffSql,
            new
            {
                SchoolId = _tenantContext.SchoolId.Value,
                Email = email
            });
    }

    private async Task SendPayslipsForGeneratedPayrollAsync(IDbConnection conn, Guid schoolId, int month, int year, Guid? onlyStaffId)
    {
        var recipients = (await conn.QueryAsync<PayslipRecipientRow>(@"
SELECT
    pr.staff_id AS StaffId,
    pr.month AS Month,
    pr.year AS Year,
    pr.generated_on AS GeneratedOn,
    pr.gross_amount AS GrossAmount,
    pr.net_amount AS NetAmount,
    pr.previous_pending AS PreviousPending,
    pr.total_due AS TotalDue,
    pr.total_paid AS TotalPaid,
    pr.pending_amount AS PendingAmount,
    st.full_name AS FullName,
    st.staff_code AS StaffCode,
    COALESCE(NULLIF(TRIM(st.email), ''), NULLIF(TRIM(u.email), '')) AS Email,
    COALESCE(att.totalworkingdays, 0) AS TotalWorkingDays,
    COALESCE(att.totalpresentdays, 0) AS TotalPresentDays,
    COALESCE(ss.basic, 0) AS Basic,
    COALESCE(ss.allowances, 0) AS Allowances,
    COALESCE(ss.deductions, 0) AS Deductions,
    sbd.bank_name AS BankName,
    sbd.account_no AS AccountNumber,
    sbd.ifsc_code AS IFSC,
    pm.payment_mode AS PaymentMode,
    NULL::text AS UAN,
    NULL::text AS PAN,
    s.name AS SchoolName,
    s.code AS SchoolCode
FROM payroll_records pr
INNER JOIN staff st
    ON st.id = pr.staff_id
   AND st.school_id = pr.school_id
   AND st.is_deleted = FALSE
LEFT JOIN users u
    ON u.school_id = st.school_id
   AND LOWER(TRIM(u.email)) = LOWER(TRIM(st.email))
   AND u.is_deleted = FALSE
LEFT JOIN salary_structures ss
    ON ss.staff_id = pr.staff_id
   AND ss.school_id = pr.school_id
   AND ss.is_deleted = FALSE
LEFT JOIN staff_bank_details sbd
    ON sbd.staff_id = pr.staff_id
   AND sbd.school_id = pr.school_id
   AND sbd.is_deleted = FALSE
LEFT JOIN LATERAL (
    SELECT
        COUNT(*)::int AS TotalWorkingDays,
        COUNT(sar.id)::int AS TotalPresentDays
    FROM staff_attendance_days sad
    LEFT JOIN staff_attendance_records sar
      ON sar.attendance_day_id = sad.id
     AND sar.staff_id = pr.staff_id
     AND sar.school_id = sad.school_id
     AND sar.is_deleted = FALSE
     AND sar.is_present = TRUE
    WHERE sad.school_id = pr.school_id
      AND sad.is_deleted = FALSE
      AND EXTRACT(MONTH FROM sad.attendance_date) = pr.month
      AND EXTRACT(YEAR FROM sad.attendance_date) = pr.year
) att ON TRUE
LEFT JOIN LATERAL (
    SELECT sp.payment_mode
    FROM staff_payments sp
    WHERE sp.school_id = pr.school_id
      AND sp.staff_id = pr.staff_id
      AND sp.payroll_record_id = pr.id
      AND sp.is_deleted = FALSE
    ORDER BY sp.payment_date DESC, sp.created_at DESC
    LIMIT 1
) pm ON TRUE
INNER JOIN schools s
    ON s.id = pr.school_id
   AND s.is_deleted = FALSE
WHERE pr.school_id = @SchoolId
  AND pr.month = @Month
  AND pr.year = @Year
  AND pr.is_deleted = FALSE
  AND (@StaffId IS NULL OR pr.staff_id = @StaffId);",
            new
            {
                SchoolId = schoolId,
                Month = month,
                Year = year,
                StaffId = onlyStaffId
            })).AsList();

        if (recipients.Count == 0) return;

        foreach (var r in recipients)
        {
            if (string.IsNullOrWhiteSpace(r.Email))
            {
                _logger.LogWarning(
                    "Payslip email skipped: no email for staff {StaffId} ({Name}) for {Month}/{Year}.",
                    r.StaffId, r.FullName, month, year);
                continue;
            }

            try
            {
                var pdf = GeneratePayslipPdf(r);
                var subject = $"Payslip - {month:D2}/{year} - {r.FullName}";
                var body = $@"
<p>Hello {r.FullName},</p>
<p>Your payroll for <strong>{month:D2}/{year}</strong> has been generated.</p>
<ul>
  <li><strong>Gross:</strong> {r.GrossAmount:0.00}</li>
  <li><strong>Net:</strong> {r.NetAmount:0.00}</li>
  <li><strong>Total Due:</strong> {r.TotalDue:0.00}</li>
  <li><strong>Pending:</strong> {r.PendingAmount:0.00}</li>
</ul>
<p>Your payslip PDF is attached.</p>";

                await _emailSender.SendAsync(
                    r.Email!,
                    subject,
                    body,
                    [
                        new EmailAttachment
                        {
                            FileName = $"Payslip-{year}-{month:D2}-{SanitizeFilePart(r.FullName)}.pdf",
                            ContentType = "application/pdf",
                            Content = pdf
                        }
                    ]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Payslip email failed for staff {StaffId} ({Name}) for {Month}/{Year}.",
                    r.StaffId, r.FullName, month, year);
            }
        }
    }

    private static string SanitizeFilePart(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "staff";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = input.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }

    private static byte[] GeneratePayslipPdf(PayslipRecipientRow row)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(10));

                // 🎨 HEADER
                page.Header().Background("#4F46E5").Padding(10).Column(col =>
                {
                    col.Item().Text("SkoolManage Payslip")
                        .FontColor(Colors.White)
                        .FontSize(18)
                        .Bold();

                    col.Item().Text($"{row.SchoolName} ({row.SchoolCode})")
                        .FontColor(Colors.White);

                    col.Item().Text($"Payroll Month: {row.Month:D2}/{row.Year}")
                        .FontColor(Colors.White);
                });

                // 📄 CONTENT
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    // 👤 STAFF INFO
                    col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                    {
                        c.Item().Text($"👤 {row.FullName} ({row.StaffCode ?? "-"})").Bold();
                        c.Item().Text($"📧 {row.Email ?? "-"}");
                    });

                    // 📊 ATTENDANCE
                    col.Item().Text("Attendance").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });

                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(1).Padding(4);

                        t.Cell().Element(Cell).Text("Working Days");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.TotalWorkingDays}");

                        t.Cell().Element(Cell).Text("Present Days");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.TotalPresentDays}");
                    });

                    // 💰 SALARY
                    col.Item().Text("Salary Breakdown").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });

                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(1).Padding(4);

                        t.Cell().Background("#E0E7FF").Element(Cell).Text("Basic");
                        t.Cell().Background("#E0E7FF").Element(Cell).AlignRight().Text($"{row.Basic:0.00}");

                        t.Cell().Element(Cell).Text("Allowances");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.Allowances:0.00}");

                        t.Cell().Element(Cell).Text("Deductions");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.Deductions:0.00}");

                        t.Cell().Element(Cell).Text("Gross");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.GrossAmount:0.00}");

                        // 🔥 Highlight NET
                        t.Cell().Background("#D1FAE5").Element(Cell).Text("Net Salary").Bold();
                        t.Cell().Background("#D1FAE5").Element(Cell).AlignRight().Text($"{row.NetAmount:0.00}").Bold();
                    });

                    // 💳 PAYMENT DETAILS
                    col.Item().Text("Payment Details").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });

                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(1).Padding(4);

                        t.Cell().Element(Cell).Text("Bank Name");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.BankName ?? "-"}");

                        t.Cell().Element(Cell).Text("Account Number");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.AccountNumber ?? "-"}");

                        t.Cell().Element(Cell).Text("IFSC");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.IFSC ?? "-"}");

                        t.Cell().Element(Cell).Text("Payment Mode");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.PaymentMode ?? "Bank Transfer"}");
                    });

                    // 🧾 DUE
                    col.Item().Text("Due & Payment").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });

                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(1).Padding(4);

                        t.Cell().Element(Cell).Text("Previous Pending");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.PreviousPending:0.00}");

                        t.Cell().Element(Cell).Text("Total Due");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.TotalDue:0.00}");

                        t.Cell().Element(Cell).Text("Total Paid");
                        t.Cell().Element(Cell).AlignRight().Text($"{row.TotalPaid:0.00}");

                        t.Cell().Background("#FEE2E2").Element(Cell).Text("Pending");
                        t.Cell().Background("#FEE2E2").Element(Cell).AlignRight().Text($"{row.PendingAmount:0.00}");
                    });
                });

                // 🧾 FOOTER
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated by SkoolManage on ");
                    x.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")).SemiBold();
                });
            });
        }).GeneratePdf();
    }

    private sealed class PayslipRecipientRow
    {
        // Basic info
        public Guid StaffId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? StaffCode { get; set; }
        public string? Email { get; set; }

        // School info
        public string SchoolName { get; set; } = string.Empty;
        public string? SchoolCode { get; set; }

        // Payroll info
        public int Month { get; set; }
        public int Year { get; set; }
        public DateTime? GeneratedOn { get; set; }

        // Attendance
        public int TotalWorkingDays { get; set; }
        public int TotalPresentDays { get; set; }
        public int TotalAbsentDays => Math.Max(0, TotalWorkingDays - TotalPresentDays);

        // Salary breakdown
        public decimal GrossAmount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal Basic { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }

        // Payment status
        public decimal PreviousPending { get; set; }
        public decimal TotalDue { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal PendingAmount { get; set; }

        // Bank details
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? IFSC { get; set; }
        public string? PaymentMode { get; set; }

        // Extra placeholders
        public string? UAN { get; set; }
        public string? PAN { get; set; }
    }
}

