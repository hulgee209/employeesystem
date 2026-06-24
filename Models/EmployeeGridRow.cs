namespace EmployeeSystem.Models;

public class EmployeeGridRow
{
    public int EmployeeId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public string PositionName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateOnly? HireDate { get; set; }

    public decimal AverageSalary { get; set; }

    public decimal LatestSalary { get; set; }

    public decimal AttendanceRate { get; set; }

    public int LateCount { get; set; }

    public int AbsentCount { get; set; }

    public decimal PerformanceScore { get; set; }

    public int LeaveBalance { get; set; }

    public int ProjectCount { get; set; }

    public int TrainingCount { get; set; }

    public int AssetCount { get; set; }

    public bool IsActive { get; set; }

    public int DepartmentId { get; set; }

    public int PositionId { get; set; }

    public int? ManagerId { get; set; }

    public string? ManagerName { get; set; }
}