using System;
using System.Collections.Generic;
using System.Linq;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public interface ISqlTemplateEngine
{
    bool TryBuildSql(IntentDetectionResult intent, DatabaseMetadata metadata, out SqlTemplateResult templateResult, out string reason);
}

public sealed class SqlTemplateResult
{
    public string Sql { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
}

public class SqlTemplateEngine : ISqlTemplateEngine
{
    public bool TryBuildSql(IntentDetectionResult intent, DatabaseMetadata metadata, out SqlTemplateResult templateResult, out string reason)
    {
        templateResult = new SqlTemplateResult();
        reason = string.Empty;

        var employees = FindTableName(metadata, "Employees");
        var departments = FindTableName(metadata, "Departments");
        var payroll = FindTableName(metadata, "Payroll");
        var attendance = FindTableName(metadata, "Attendance");
        var performance = FindTableName(metadata, "PerformanceReviews");
        var leave = FindTableName(metadata, "LeaveRequests");
        var training = FindTableName(metadata, "EmployeeTraining");
        var projects = FindTableName(metadata, "EmployeeProjects");
        var assets = FindTableName(metadata, "EmployeeAssets");

        if (intent.Type == IntentType.Unknown)
        {
            reason = "No mapped intent found for the question.";
            return false;
        }

        switch (intent.Type)
        {
            case IntentType.EmployeeCountByDepartment:
                if (employees == null || departments == null)
                {
                    reason = "Required tables for employee count by department are not available.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(intent.DepartmentName))
                {
                    templateResult = new SqlTemplateResult
                    {
                        Sql = $"SELECT COUNT(*) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE d.DepartmentName = @DepartmentName",
                        Parameters = new Dictionary<string, object?> { ["DepartmentName"] = intent.DepartmentName }
                    };
                    return true;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.EmployeeCountTotal:
                if (employees == null)
                {
                    reason = "Employees table is not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT COUNT(*) AS EmployeeCount FROM [{employees}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.AverageSalaryByDepartment:
                if (payroll == null || employees == null || departments == null)
                {
                    reason = "Required tables for average salary by department are not available.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(intent.DepartmentName))
                {
                    templateResult = new SqlTemplateResult
                    {
                        Sql = $"SELECT AVG(COALESCE(p.NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE d.DepartmentName = @DepartmentName",
                        Parameters = new Dictionary<string, object?> { ["DepartmentName"] = intent.DepartmentName }
                    };
                }
                else
                {
                    templateResult = new SqlTemplateResult
                    {
                        Sql = $"SELECT d.DepartmentName AS Department, AVG(COALESCE(p.NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY AverageNetSalary DESC",
                        Parameters = new Dictionary<string, object?>()
                    };
                }
                return true;

            case IntentType.TopSalaryEmployee:
                if (payroll == null || employees == null || departments == null)
                {
                    reason = "Required tables for top salary employee are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT TOP 10 e.FirstName + ' ' + e.LastName AS Employee, d.DepartmentName AS Department, p.NetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId ORDER BY p.NetSalary DESC",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.AttendanceRate:
                if (attendance == null || employees == null)
                {
                    reason = "Required tables for attendance rate are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT CAST(SUM(CASE WHEN LOWER(COALESCE(Status, '')) IN ('present', 'late', 'available') THEN 1 ELSE 0 END) AS decimal(10,2)) / NULLIF(COUNT(*), 0) * 100 AS AttendanceRatePercentage FROM [{attendance}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.AbsentEmployees:
                if (attendance == null || employees == null)
                {
                    reason = "Required tables for absent employees are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT COUNT(*) AS AbsentCount FROM [{attendance}] WHERE LOWER(COALESCE(Status, '')) LIKE '%absent%' OR LOWER(COALESCE(Status, '')) LIKE '%илэрсэн%'",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.LateEmployees:
                if (attendance == null || employees == null)
                {
                    reason = "Required tables for late employees are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT COUNT(*) AS LateCount FROM [{attendance}] WHERE LOWER(COALESCE(Status, '')) LIKE '%late%' OR LOWER(COALESCE(Status, '')) LIKE '%хоцрол%'",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.LeaveSummary:
                if (leave == null || employees == null)
                {
                    reason = "Required tables for leave summary are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT COUNT(*) AS LeaveRequestCount FROM [{leave}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.TrainingSummary:
                if (training == null || employees == null)
                {
                    reason = "Required tables for training summary are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT COUNT(*) AS TrainingRecords FROM [{training}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.PerformanceSummary:
                if (performance == null || employees == null)
                {
                    reason = "Required tables for performance summary are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT AVG(CAST(ISNULL(Score, 0) AS decimal(10,2))) AS AveragePerformanceScore, COUNT(*) AS ReviewCount FROM [{performance}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.DepartmentStatistics:
                if (departments == null || employees == null)
                {
                    reason = "Required tables for department statistics are not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            case IntentType.PayrollSummary:
                if (payroll == null)
                {
                    reason = "Payroll table is not available.";
                    return false;
                }

                templateResult = new SqlTemplateResult
                {
                    Sql = $"SELECT SUM(COALESCE(NetSalary, 0)) AS TotalPayroll, AVG(COALESCE(NetSalary, 0)) AS AverageNetSalary, COUNT(*) AS PayrollRows FROM [{payroll}]",
                    Parameters = new Dictionary<string, object?>()
                };
                return true;

            default:
                reason = "Intent mapping is not implemented.";
                return false;
        }
    }

    private static string? FindTableName(DatabaseMetadata metadata, string canonicalName)
    {
        var match = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, canonicalName, StringComparison.OrdinalIgnoreCase));
        return match?.Name;
    }
}
