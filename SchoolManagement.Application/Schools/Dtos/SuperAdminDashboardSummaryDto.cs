namespace SchoolManagement.Application.Schools.Dtos;

public class SuperAdminDashboardSummaryDto
{
    public int TotalSchoolRegistered { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public decimal TotalCollection { get; set; }
    public int TotalStudents { get; set; }
    public int TotalStaff { get; set; }
}
