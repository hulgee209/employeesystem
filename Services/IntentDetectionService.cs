using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public interface IIntentDetectionService
{
    IntentDetectionResult DetectIntent(string question, DatabaseMetadata metadata);
}

public class IntentDetectionService : IIntentDetectionService
{
    public IntentDetectionResult DetectIntent(string question, DatabaseMetadata metadata)
    {
        var normalized = question?.Trim() ?? string.Empty;
        var lower = normalized.ToLowerInvariant();
        var tokens = NormalizeTextForSearch(normalized);

        var departmentName = ExtractDepartmentName(tokens, metadata);

        if (ContainsAny(lower, "highest salary", "top salary", "хамгийн өндөр цалин", "top-paid", "top paid"))
        {
            return new IntentDetectionResult(IntentType.TopSalaryEmployee, departmentName);
        }

        if (ContainsAny(lower, "average salary", "дундж цалин", "salary average", "средняя зарплата") && ContainsAny(lower, "department", "хэлтэс", "heltes"))
        {
            return new IntentDetectionResult(IntentType.AverageSalaryByDepartment, departmentName);
        }

        if (ContainsAny(lower, "how many employees", "employee count", "ажилтан", "employees") && ContainsAny(lower, "department", "хэлтэс", "heltes"))
        {
            return new IntentDetectionResult(IntentType.EmployeeCountByDepartment, departmentName);
        }

        if (ContainsAny(lower, "how many employees", "employee count", "ажилтан", "employees"))
        {
            return new IntentDetectionResult(IntentType.EmployeeCountTotal, departmentName);
        }

        if (ContainsAny(lower, "attendance rate", "ирц", "present rate", "ауранс") || ContainsAny(tokens, "attendance", "ирц"))
        {
            return new IntentDetectionResult(IntentType.AttendanceRate, departmentName);
        }

        if (ContainsAny(lower, "absent", "absentees", "absentees", " absent ", " absent", "absent ", "илэрсэн"))
        {
            return new IntentDetectionResult(IntentType.AbsentEmployees, departmentName);
        }

        if (ContainsAny(lower, "late", "tardy", "хоцрол", "hoцrol"))
        {
            return new IntentDetectionResult(IntentType.LateEmployees, departmentName);
        }

        if (ContainsAny(lower, "leave", "leave summary", "chuti", "амралт", "илүү", "өлз"))
        {
            return new IntentDetectionResult(IntentType.LeaveSummary, departmentName);
        }

        if (ContainsAny(lower, "training", "training summary", "сургалт", "тасалбар"))
        {
            return new IntentDetectionResult(IntentType.TrainingSummary, departmentName);
        }

        if (ContainsAny(lower, "performance", "үнэлгээ", "score", "point", "rating"))
        {
            return new IntentDetectionResult(IntentType.PerformanceSummary, departmentName);
        }

        if (ContainsAny(lower, "department statistics", "телтэс", "statistics", "статистика") || ContainsAny(tokens, "department", "хэлтэс"))
        {
            return new IntentDetectionResult(IntentType.DepartmentStatistics, departmentName);
        }

        if (ContainsAny(lower, "payroll", "total payroll", "salary expense", "цалин", "payroll summary"))
        {
            return new IntentDetectionResult(IntentType.PayrollSummary, departmentName);
        }

        return new IntentDetectionResult(IntentType.Unknown, departmentName);
    }

    private static string? ExtractDepartmentName(HashSet<string> tokens, DatabaseMetadata metadata)
    {
        var departmentTable = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, "Departments", StringComparison.OrdinalIgnoreCase));
        if (departmentTable == null)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in departmentTable.SampleRows)
        {
            if (row.TryGetValue("DepartmentName", out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
            {
                names.Add(text.Trim());
            }
        }

        foreach (var token in tokens)
        {
            var match = names.FirstOrDefault(name => name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        foreach (var name in names)
        {
            var lower = name.ToLowerInvariant();
            if (tokens.Contains(lower))
            {
                return name;
            }
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(HashSet<string> tokens, params string[] patterns)
    {
        return patterns.Any(tokens.Contains);
    }

    private static HashSet<string> NormalizeTextForSearch(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }
}

public enum IntentType
{
    Unknown,
    EmployeeCountByDepartment,
    EmployeeCountTotal,
    AverageSalaryByDepartment,
    TopSalaryEmployee,
    AttendanceRate,
    AbsentEmployees,
    LateEmployees,
    LeaveSummary,
    TrainingSummary,
    PerformanceSummary,
    DepartmentStatistics,
    PayrollSummary
}

public sealed record IntentDetectionResult(IntentType Type, string? DepartmentName);
