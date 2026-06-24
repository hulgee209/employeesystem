using System.Data;

namespace EmployeeSystem.Models;

public class Employee360DetailsViewModel
{
    public int EmployeeId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public string DepartmentName { get; set; } = string.Empty;
    public string PositionName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? HireDate { get; set; }
    public Employee360Scores Scores { get; set; } = new();
    public IReadOnlyList<AttendanceRowViewModel> Attendance { get; set; } = [];
    public IReadOnlyList<PayrollRowViewModel> Payroll { get; set; } = [];
    public IReadOnlyList<PerformanceRowViewModel> Performance { get; set; } = [];
    public IReadOnlyList<Employee360SimpleItem> Leave { get; set; } = [];
    public IReadOnlyList<Employee360SimpleItem> Projects { get; set; } = [];
    public IReadOnlyList<Employee360SimpleItem> Training { get; set; } = [];
    public IReadOnlyList<Employee360SimpleItem> Assets { get; set; } = [];
}

public class Employee360Scores
{
    public int AttendanceRiskScore { get; set; }
    public int PerformanceRiskScore { get; set; }
    public int EmployeeHealthScore { get; set; }
    public int PromotionCandidateScore { get; set; }
    public int RetentionRiskScore { get; set; }
    public int TrainingRecommendationScore { get; set; }
    public string AttendanceForecast { get; set; } = "Мэдээлэл хангалтгүй";
    public string PerformanceForecast { get; set; } = "Мэдээлэл хангалтгүй";
}

public class Employee360SimpleItem
{
    public string Title { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DateText { get; set; }
    public string? Detail { get; set; }
}

public class ExecutiveDashboardViewModel
{
    public IReadOnlyList<DashboardMetricItem> TopDepartments { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> HighestPayrollCost { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> BestPerformingDepartments { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> MostAbsences { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> MostActiveProjects { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> TrainingParticipation { get; set; } = [];
    public IReadOnlyList<DashboardMetricItem> EmployeeGrowthTrend { get; set; } = [];

    // KPI totals for executive summary
    public int TotalEmployees { get; set; }
    public int TotalDepartments { get; set; }
    public int TotalPositions { get; set; }
    // Simple growth value (last month count or total hires)
    public int EmployeeGrowthValue { get; set; }
}

public class DashboardMetricItem
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public class AiSqlAgentResult
{
    public string Sql { get; set; } = string.Empty;
    public IReadOnlyList<string> Columns { get; set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];
    public string Analysis { get; set; } = string.Empty;
}

public class SqlExecutionResult
{
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}

public class AiCopilotRequest
{
    public string Question { get; set; } = string.Empty;
    public int? EmployeeId { get; set; }
}

public class AiCopilotResponse
{
    public string Answer { get; set; } = string.Empty;
    public string? Sql { get; set; }
}
