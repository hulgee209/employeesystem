using Microsoft.AspNetCore.Mvc.Rendering;

namespace EmployeeSystem.Models;

public class AttendanceIndexViewModel
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public DateOnly? Date { get; set; }
    public string? Status { get; set; }
    public int PresentEmployees { get; set; }
    public int LateEmployees { get; set; }
    public int AbsentEmployees { get; set; }
    public IReadOnlyList<SelectListItem> Departments { get; set; } = [];
    public IReadOnlyList<SelectListItem> Statuses { get; set; } = [];
    public IReadOnlyList<AttendanceRowViewModel> Records { get; set; } = [];
}

public class AttendanceRowViewModel
{
    public int AttendanceId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly AttendanceDate { get; set; }
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PayrollIndexViewModel
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public string? Month { get; set; }
    public decimal AverageSalary { get; set; }
    public decimal HighestSalary { get; set; }
    public decimal LowestSalary { get; set; }
    public decimal TotalPayrollCost { get; set; }
    public IReadOnlyList<SelectListItem> Departments { get; set; } = [];
    public IReadOnlyList<PayrollRowViewModel> Records { get; set; } = [];
}

public class PayrollRowViewModel
{
    public int PayrollId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly? PayrollMonth { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Bonus { get; set; }
    public decimal Deductions { get; set; }
    public decimal NetSalary { get; set; }
}

public class PerformanceIndexViewModel
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public DateOnly? Date { get; set; }
    public decimal AveragePerformanceScore { get; set; }
    public string TopEmployee { get; set; } = "-";
    public string HighestDepartmentScore { get; set; } = "-";
    public IReadOnlyList<SelectListItem> Departments { get; set; } = [];
    public IReadOnlyList<PerformanceRowViewModel> Records { get; set; } = [];
}

public class PerformanceRowViewModel
{
    public int ReviewId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly? ReviewDate { get; set; }
    public int? Score { get; set; }
    public string? Reviewer { get; set; }
    public string? Comments { get; set; }
}
