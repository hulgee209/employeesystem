namespace EmployeeSystem.Models;

public record HrDashboardViewModel(
    int TotalEmployees,
    int ActiveEmployees,
    int OpenPositions,
    int PendingLeaveRequests,
    decimal TotalPayrollThisMonth,
    List<DepartmentSummary> ByDepartment,
    List<EmployeeHireSummary> RecentHires
);

public record ManagerDashboardViewModel(
    int MyTeamSize,
    List<TeamMemberSummary> TeamMembers,
    int PendingPerformanceReviews,
    int PendingLeaveApprovals
);

public record EmployeeDashboardViewModel(
    EmployeeSummary Me,
    PayslipSummary? LatestPayslip,
    int RemainingLeaveDays,
    List<NotificationSummary> MyNotifications
);

public record DepartmentSummary(
    int DepartmentId,
    string DepartmentName,
    int EmployeeCount,
    decimal AverageSalary
);

public record EmployeeHireSummary(
    int EmployeeId,
    string FirstName,
    string LastName,
    string DepartmentName,
    DateTime HireDate
);

public record TeamMemberSummary(
    int EmployeeId,
    string FirstName,
    string LastName,
    string PositionName,
    DateTime? LastReviewDate
);

public record EmployeeSummary(
    int EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string DepartmentName,
    string PositionName,
    DateTime HireDate
);

public record PayslipSummary(
    int PayrollId,
    decimal BaseSalary,
    decimal Bonus,
    decimal Deductions,
    decimal NetSalary,
    DateTime PayMonth
);

public record NotificationSummary(
    int NotificationId,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAt
);
