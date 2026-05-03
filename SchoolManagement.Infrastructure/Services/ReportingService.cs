using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Application.Common;
using SchoolManagement.Application.Reporting.Dtos;
using SchoolManagement.Application.Reporting.Services;

namespace SchoolManagement.Infrastructure.Services;

public class ReportingService : IReportingService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;

    public ReportingService(IDbConnectionFactory connectionFactory, ITenantContext tenantContext, ICurrentUserService currentUser)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<StudentListResultDto>> GetStudentsAsync(StudentListQueryDto query)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentListResultDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_page", query.Page);
        p.Add("p_page_size", query.PageSize);
        p.Add("p_search", query.Search);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_reporting_students_list(@p_school_id,@p_page,@p_page_size,@p_search)", p);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StudentListResultDto>.Ok(new StudentListResultDto());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new StudentListResultDto
        {
            TotalCount = root.GetProperty("TotalCount").GetInt32(),
            Page = root.GetProperty("Page").GetInt32(),
            PageSize = root.GetProperty("PageSize").GetInt32(),
            Items = MapStudentItems(root.GetProperty("Items"))
        };

        return ApiResponse<StudentListResultDto>.Ok(result);
    }

    public async Task<ApiResponse<StudentOverviewDto>> GetStudentOverviewAsync(Guid studentId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentOverviewDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_reporting_student_overview(@p_school_id,@p_student_id)", p);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StudentOverviewDto>.Fail("Student not found.", ErrorCodes.NotFound);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = root.GetProperty("Student");

        var dto = new StudentOverviewDto
        {
            Id = s.GetProperty("Id").GetGuid(),
            AdmissionNo = s.GetProperty("AdmissionNo").GetString() ?? string.Empty,
            FullName = s.GetProperty("FullName").GetString() ?? string.Empty,
            Email = s.TryGetProperty("Email", out var emailProp) && emailProp.ValueKind != JsonValueKind.Null
                ? emailProp.GetString()
                : null,
            ClassId = s.GetProperty("ClassId").GetGuid(),
            SectionId = s.GetProperty("SectionId").GetGuid(),
            ClassName = s.TryGetProperty("ClassName", out var cn) && cn.ValueKind != JsonValueKind.Null
                ? cn.GetString()
                : null,
            SectionName = s.TryGetProperty("SectionName", out var sn) && sn.ValueKind != JsonValueKind.Null
                ? sn.GetString()
                : null,
            ParentMobileNo = s.TryGetProperty("ParentMobileNo", out var pm) && pm.ValueKind != JsonValueKind.Null
                ? pm.GetString()
                : null,
            AttendancePercentage = root.GetProperty("AttendancePercentage").GetDecimal(),
            TotalFeeDue = root.GetProperty("TotalFeeDue").GetDecimal(),
            TotalFeePaid = root.GetProperty("TotalFeePaid").GetDecimal(),
            Outstanding = root.GetProperty("Outstanding").GetDecimal()
        };

        return ApiResponse<StudentOverviewDto>.Ok(dto);
    }

    public async Task<ApiResponse<StaffPayrollSummaryDto>> GetStaffPayrollSummaryAsync(Guid staffId, int year)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StaffPayrollSummaryDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var perms = _currentUser.Permissions ?? Array.Empty<string>();
        var canViewAllPayroll = perms.Contains("Payroll.View.All")
                                || perms.Contains("Payroll.History.View.All")
                                || perms.Contains("Payroll.Pay.All")
                                || perms.Contains("Staff.Manage");
        var canViewOwnPayroll = perms.Contains("Payroll.View")
                                || perms.Contains("Payroll.History.View")
                                || perms.Contains("Reporting.Staff.Payroll");
        if (!canViewAllPayroll && !canViewOwnPayroll)
            return ApiResponse<StaffPayrollSummaryDto>.Fail("You do not have permission to view payroll.", ErrorCodes.Forbidden);
        if (!canViewAllPayroll && canViewOwnPayroll)
        {
            var ownStaffId = await ResolveCurrentStaffIdAsync(conn);
            if (ownStaffId == null || ownStaffId.Value != staffId)
                return ApiResponse<StaffPayrollSummaryDto>.Fail("You can only view your own payroll.", ErrorCodes.Forbidden);
        }

        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_staff_id", staffId);
        p.Add("p_year", year);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_reporting_staff_payroll_summary(@p_school_id,@p_staff_id,@p_year)", p);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StaffPayrollSummaryDto>.Fail("Staff or payroll not found.", ErrorCodes.NotFound);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = root.GetProperty("Staff");

        var summary = new StaffPayrollSummaryDto
        {
            StaffId = s.GetProperty("Id").GetGuid(),
            StaffCode = s.GetProperty("StaffCode").GetString() ?? string.Empty,
            FullName = s.GetProperty("FullName").GetString() ?? string.Empty,
            Email = s.TryGetProperty("Email", out var emailProp) && emailProp.ValueKind != JsonValueKind.Null
                ? emailProp.GetString()
                : null,
            Year = root.GetProperty("Year").GetInt32(),
            Payroll = MapPayrollItems(root.GetProperty("Payroll"))
        };

        return ApiResponse<StaffPayrollSummaryDto>.Ok(summary);
    }

    public async Task<ApiResponse<PayrollListResultDto>> GetPayrollListAsync(int year, int? month, int page, int pageSize)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<PayrollListResultDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_year", year == 0 ? null : year);
        p.Add("p_month", month);
        p.Add("p_page", page);
        p.Add("p_page_size", pageSize);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_payroll_list(@p_school_id,@p_year,@p_month,@p_page,@p_page_size)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<PayrollListResultDto>.Ok(new PayrollListResultDto());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new PayrollListResultDto
        {
            TotalCount = root.GetProperty("TotalCount").GetInt32(),
            Page = root.GetProperty("Page").GetInt32(),
            PageSize = root.GetProperty("PageSize").GetInt32(),
            Items = MapPayrollListItems(root.GetProperty("Items"))
        };

        return ApiResponse<PayrollListResultDto>.Ok(result);
    }

    public async Task<ApiResponse<TimetableResultDto>> GetTimetableForClassAsync(Guid classId, Guid sectionId)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<TimetableResultDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_class_id", classId);
        p.Add("p_section_id", sectionId);

        var json = await conn.ExecuteScalarAsync<string>("SELECT fn_reporting_timetable_for_class(@p_school_id,@p_class_id,@p_section_id)", p);
        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<TimetableResultDto>.Ok(new TimetableResultDto { ClassId = classId, SectionId = sectionId });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var entries = root.GetProperty("Entries");

        var result = new TimetableResultDto
        {
            ClassId = root.GetProperty("ClassId").GetGuid(),
            SectionId = root.GetProperty("SectionId").GetGuid(),
            Entries = MapTimetableEntries(entries)
        };

        return ApiResponse<TimetableResultDto>.Ok(result);
    }

    public async Task<ApiResponse<DashboardSummaryDto>> GetDashboardSummaryAsync()
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<DashboardSummaryDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_dashboard_summary(@p_school_id)",
            new { p_school_id = _tenantContext.SchoolId });

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<DashboardSummaryDto>.Ok(new DashboardSummaryDto());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dto = new DashboardSummaryDto
        {
            TotalStudents = root.GetProperty("TotalStudents").GetInt32(),
            TotalStaff = root.GetProperty("TotalStaff").GetInt32(),
            TotalCollection = root.GetProperty("TotalCollection").GetDecimal(),
            MonthlyCollection = root.GetProperty("MonthlyCollection").GetDecimal(),
            TotalPending = root.GetProperty("TotalPending").GetDecimal(),
            TodayAttendancePercentage = root.GetProperty("TodayAttendancePercentage").GetDecimal(),
            TodayPresentCount = root.GetProperty("TodayPresentCount").GetInt32(),
            TodayMarkedCount = root.GetProperty("TodayMarkedCount").GetInt32()
        };

        return ApiResponse<DashboardSummaryDto>.Ok(dto);
    }

    public async Task<ApiResponse<GlobalSearchResultDto>> GlobalSearchAsync(string query, int limit)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<GlobalSearchResultDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_query", query);
        p.Add("p_limit", limit);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_global_search(@p_school_id,@p_query,@p_limit)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<GlobalSearchResultDto>.Ok(new GlobalSearchResultDto());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var students = MapStudentItems(root.GetProperty("Students"));
        var staff = MapStaffSearchItems(root.GetProperty("Staff"));

        var result = new GlobalSearchResultDto
        {
            Students = students,
            Staff = staff
        };

        return ApiResponse<GlobalSearchResultDto>.Ok(result);
    }

    public async Task<ApiResponse<StudentAttendanceMonthlyDto>> GetStudentAttendanceMonthlyAsync(Guid studentId, int year)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentAttendanceMonthlyDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);
        p.Add("p_year", year);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_student_attendance_monthly(@p_school_id,@p_student_id,@p_year)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StudentAttendanceMonthlyDto>.Ok(new StudentAttendanceMonthlyDto
            {
                StudentId = studentId,
                Year = year
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var itemsJson = root.GetProperty("Items");
        var items = new List<StudentAttendanceMonthlyItemDto>();
        foreach (var item in itemsJson.EnumerateArray())
        {
            items.Add(new StudentAttendanceMonthlyItemDto
            {
                Month = item.GetProperty("Month").GetInt32(),
                PresentDays = item.GetProperty("PresentDays").GetInt32(),
                TotalDays = item.GetProperty("TotalDays").GetInt32(),
                AbsentDays = item.TryGetProperty("AbsentDays", out var a) && a.ValueKind == JsonValueKind.Number
                    ? a.GetInt32()
                    : item.GetProperty("TotalDays").GetInt32() - item.GetProperty("PresentDays").GetInt32(),
                Percentage = item.GetProperty("Percentage").GetDecimal()
            });
        }

        var dto = new StudentAttendanceMonthlyDto
        {
            StudentId = root.GetProperty("StudentId").GetGuid(),
            Year = root.GetProperty("Year").GetInt32(),
            Items = items
        };

        return ApiResponse<StudentAttendanceMonthlyDto>.Ok(dto);
    }

    public async Task<ApiResponse<StudentAttendanceMonthDetailDto>> GetStudentAttendanceMonthDetailAsync(Guid studentId, int year, int month)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StudentAttendanceMonthDetailDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_student_id", studentId);
        p.Add("p_year", year);
        p.Add("p_month", month);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_student_attendance_month_detail(@p_school_id,@p_student_id,@p_year,@p_month)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StudentAttendanceMonthDetailDto>.Ok(new StudentAttendanceMonthDetailDto
            {
                StudentId = studentId,
                Year = year,
                Month = month
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var itemsJson = root.GetProperty("Items");
        var items = new List<StudentAttendanceDayDto>();
        foreach (var item in itemsJson.EnumerateArray())
        {
            items.Add(new StudentAttendanceDayDto
            {
                Date = item.GetProperty("Date").GetDateTime(),
                IsPresent = item.GetProperty("IsPresent").GetBoolean(),
                Status = item.TryGetProperty("Status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null,
                ClassId = item.TryGetProperty("ClassId", out var cid) && cid.TryGetGuid(out var cguid) ? cguid : null,
                SectionId = item.TryGetProperty("SectionId", out var sid) && sid.TryGetGuid(out var sguid) ? sguid : null
            });
        }

        var dto = new StudentAttendanceMonthDetailDto
        {
            StudentId = root.GetProperty("StudentId").GetGuid(),
            Year = root.GetProperty("Year").GetInt32(),
            Month = root.GetProperty("Month").GetInt32(),
            Items = items
        };

        return ApiResponse<StudentAttendanceMonthDetailDto>.Ok(dto);
    }

    public async Task<ApiResponse<StaffAttendanceMonthlyDto>> GetStaffAttendanceMonthlyAsync(Guid staffId, int year)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StaffAttendanceMonthlyDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_staff_id", staffId);
        p.Add("p_year", year);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_staff_attendance_monthly(@p_school_id,@p_staff_id,@p_year)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StaffAttendanceMonthlyDto>.Ok(new StaffAttendanceMonthlyDto
            {
                StaffId = staffId,
                Year = year
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var itemsJson = root.GetProperty("Items");
        var items = new List<StaffAttendanceMonthlyItemDto>();
        foreach (var item in itemsJson.EnumerateArray())
        {
            items.Add(new StaffAttendanceMonthlyItemDto
            {
                Month = item.GetProperty("Month").GetInt32(),
                PresentDays = item.GetProperty("PresentDays").GetInt32(),
                TotalDays = item.GetProperty("TotalDays").GetInt32(),
                AbsentDays = item.TryGetProperty("AbsentDays", out var a) && a.ValueKind == JsonValueKind.Number
                    ? a.GetInt32()
                    : item.GetProperty("TotalDays").GetInt32() - item.GetProperty("PresentDays").GetInt32(),
                Percentage = item.GetProperty("Percentage").GetDecimal()
            });
        }

        var dto = new StaffAttendanceMonthlyDto
        {
            StaffId = root.GetProperty("StaffId").GetGuid(),
            Year = root.GetProperty("Year").GetInt32(),
            Items = items
        };

        return ApiResponse<StaffAttendanceMonthlyDto>.Ok(dto);
    }

    public async Task<ApiResponse<StaffAttendanceMonthDetailDto>> GetStaffAttendanceMonthDetailAsync(Guid staffId, int year, int month)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<StaffAttendanceMonthDetailDto>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_staff_id", staffId);
        p.Add("p_year", year);
        p.Add("p_month", month);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_staff_attendance_month_detail(@p_school_id,@p_staff_id,@p_year,@p_month)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<StaffAttendanceMonthDetailDto>.Ok(new StaffAttendanceMonthDetailDto
            {
                StaffId = staffId,
                Year = year,
                Month = month
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var itemsJson = root.GetProperty("Items");
        var items = new List<StaffAttendanceDayDto>();
        foreach (var item in itemsJson.EnumerateArray())
        {
            items.Add(new StaffAttendanceDayDto
            {
                Date = item.GetProperty("Date").GetDateTime(),
                IsPresent = item.GetProperty("IsPresent").GetBoolean(),
                Status = item.TryGetProperty("Status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null
            });
        }

        var dto = new StaffAttendanceMonthDetailDto
        {
            StaffId = root.GetProperty("StaffId").GetGuid(),
            Year = root.GetProperty("Year").GetInt32(),
            Month = root.GetProperty("Month").GetInt32(),
            Items = items
        };

        return ApiResponse<StaffAttendanceMonthDetailDto>.Ok(dto);
    }

    public async Task<ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>> GetRecentActivityAsync(int limit)
    {
        if (_tenantContext.SchoolId is null)
            return ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>.Fail("Tenant context missing.", ErrorCodes.BusinessRule);

        using var conn = await _connectionFactory.CreateConnectionAsync();
        var p = new DynamicParameters();
        p.Add("p_school_id", _tenantContext.SchoolId);
        p.Add("p_limit", limit);

        var json = await conn.ExecuteScalarAsync<string>(
            "SELECT fn_reporting_recent_activity(@p_school_id,@p_limit)",
            p);

        if (string.IsNullOrWhiteSpace(json))
            return ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>.Ok(Array.Empty<RecentActivityItemDto>());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = new List<RecentActivityItemDto>();

        foreach (var item in root.EnumerateArray())
        {
            list.Add(new RecentActivityItemDto
            {
                Type = item.TryGetProperty("Type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty,
                Title = item.TryGetProperty("Title", out var ti) && ti.ValueKind == JsonValueKind.String ? ti.GetString() ?? string.Empty : string.Empty,
                Description = item.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? string.Empty : string.Empty,
                OccurredAt = item.TryGetProperty("OccurredAt", out var o) && o.ValueKind == JsonValueKind.String
                    ? TimeZones.ToIst(DateTime.Parse(o.GetString() ?? DateTime.UtcNow.ToString("O")))
                    : TimeZones.NowIst()
            });
        }

        return ApiResponse<IReadOnlyCollection<RecentActivityItemDto>>.Ok(list);
    }

    private static IReadOnlyCollection<StudentListItemDto> MapStudentItems(JsonElement itemsElement)
    {
        var list = new List<StudentListItemDto>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            list.Add(new StudentListItemDto
            {
                Id = item.GetProperty("Id").GetGuid(),
                AdmissionNo = item.GetProperty("AdmissionNo").GetString() ?? string.Empty,
                FullName = item.GetProperty("FullName").GetString() ?? string.Empty,
                ClassId = item.GetProperty("ClassId").GetGuid(),
                SectionId = item.GetProperty("SectionId").GetGuid(),
                Email = item.TryGetProperty("Email", out var e) && e.ValueKind != JsonValueKind.Null
                    ? e.GetString()
                    : null,
                ClassName = item.TryGetProperty("ClassName", out var cn) && cn.ValueKind != JsonValueKind.Null
                    ? cn.GetString()
                    : null,
                SectionName = item.TryGetProperty("SectionName", out var sn) && sn.ValueKind != JsonValueKind.Null
                    ? sn.GetString()
                    : null,
                MobileNo = item.TryGetProperty("MobileNo", out var mn) && mn.ValueKind != JsonValueKind.Null
                    ? mn.GetString()
                    : null,
                PendingAmount = item.TryGetProperty("PendingAmount", out var pa) && pa.ValueKind != JsonValueKind.Null && pa.TryGetDecimal(out var p)
                    ? p
                    : 0
            });
        }

        return list;
    }

    private static IReadOnlyCollection<StaffPayrollItemDto> MapPayrollItems(JsonElement itemsElement)
    {
        var list = new List<StaffPayrollItemDto>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            list.Add(new StaffPayrollItemDto
            {
                Month = item.GetProperty("Month").GetInt32(),
                Year = item.GetProperty("Year").GetInt32(),
                GrossAmount = item.GetProperty("GrossAmount").GetDecimal(),
                NetAmount = item.GetProperty("NetAmount").GetDecimal(),
                GeneratedOn = item.GetProperty("GeneratedOn").GetDateTime(),
                PreviousPending = item.GetProperty("PreviousPending").GetDecimal(),
                TotalDue = item.GetProperty("TotalDue").GetDecimal(),
                TotalPaid = item.GetProperty("TotalPaid").GetDecimal(),
                PendingAmount = item.GetProperty("PendingAmount").GetDecimal()
            });
        }

        return list;
    }

    private static IReadOnlyCollection<PayrollListItemDto> MapPayrollListItems(JsonElement itemsElement)
    {
        var list = new List<PayrollListItemDto>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            list.Add(new PayrollListItemDto
            {
                // PG/json may expose UUID as string or other JsonValueKind; avoid requiring String-only
                PayrollRecordId = TryGetGuidNullable(item, "PayrollRecordId"),
                StaffId = item.GetProperty("StaffId").GetGuid(),
                StaffCode = item.GetProperty("StaffCode").GetString() ?? string.Empty,
                FullName = item.GetProperty("FullName").GetString() ?? string.Empty,
                Month = item.GetProperty("Month").GetInt32(),
                Year = item.GetProperty("Year").GetInt32(),
                GrossAmount = GetDecimalOrZero(item, "GrossAmount"),
                Deduction = GetDecimalOrZero(item, "Deduction"),
                NetAmount = GetDecimalOrZero(item, "NetAmount"),
                GeneratedOn = item.TryGetProperty("GeneratedOn", out var go) && go.ValueKind != System.Text.Json.JsonValueKind.Null && go.ValueKind != System.Text.Json.JsonValueKind.Undefined ? go.GetDateTime() : null,
                PreviousPending = GetDecimalOrZero(item, "PreviousPending"),
                TotalDue = GetDecimalOrZero(item, "TotalDue"),
                TotalPaid = GetDecimalOrZero(item, "TotalPaid"),
                PendingAmount = GetDecimalOrZero(item, "PendingAmount")
            });
        }

        return list;
    }

    private static Guid? TryGetGuidNullable(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var el) ||
            el.ValueKind == JsonValueKind.Null ||
            el.ValueKind == JsonValueKind.Undefined)
            return null;
        return el.TryGetGuid(out var g) ? g : null;
    }

    private static decimal GetDecimalOrZero(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var prop) && prop.ValueKind != System.Text.Json.JsonValueKind.Null && prop.ValueKind != System.Text.Json.JsonValueKind.Undefined)
        {
            if (prop.TryGetDecimal(out var d)) return d;
        }
        return 0;
    }

    private static IReadOnlyCollection<TimetableSlotDto> MapTimetableEntries(JsonElement itemsElement)
    {
        var list = new List<TimetableSlotDto>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            list.Add(new TimetableSlotDto
            {
                Id = item.GetProperty("Id").GetGuid(),
                ClassId = item.GetProperty("ClassId").GetGuid(),
                SectionId = item.GetProperty("SectionId").GetGuid(),
                SubjectId = item.GetProperty("SubjectId").GetGuid(),
                TeacherId = item.GetProperty("TeacherId").GetGuid(),
                TeacherName = item.TryGetProperty("TeacherName", out var tn) && tn.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? (tn.GetString() ?? string.Empty)
                    : string.Empty,
                DayOfWeek = item.GetProperty("DayOfWeek").GetInt32(),
                StartTime = TimeSpan.Parse(item.GetProperty("StartTime").GetString() ?? "00:00:00"),
                EndTime = TimeSpan.Parse(item.GetProperty("EndTime").GetString() ?? "00:00:00")
            });
        }

        return list;
    }

    private static IReadOnlyCollection<StaffSearchItemDto> MapStaffSearchItems(JsonElement itemsElement)
    {
        var list = new List<StaffSearchItemDto>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            list.Add(new StaffSearchItemDto
            {
                Id = item.GetProperty("Id").GetGuid(),
                StaffCode = item.GetProperty("StaffCode").GetString() ?? string.Empty,
                FullName = item.GetProperty("FullName").GetString() ?? string.Empty,
                Email = item.TryGetProperty("Email", out var e) && e.ValueKind != JsonValueKind.Null
                    ? e.GetString()
                    : null,
                MobileNo = item.TryGetProperty("MobileNo", out var m) && m.ValueKind != JsonValueKind.Null
                    ? m.GetString()
                    : null,
                IsTeaching = item.TryGetProperty("IsTeaching", out var t) && t.ValueKind == JsonValueKind.True
            });
        }

        return list;
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
}

