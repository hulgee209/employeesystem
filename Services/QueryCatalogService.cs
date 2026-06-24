using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EmployeeSystem.Models;
using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public interface IQueryCatalogService
{
    bool TryResolveQuery(string question, IntentDetectionResult intent, DatabaseMetadata metadata, out QueryCatalogResult result);
}

public sealed record QueryCatalogResult(string Sql, IReadOnlyDictionary<string, object?> Parameters, string CatalogKey);

public class QueryCatalogService : IQueryCatalogService
{
    private readonly IEntityResolver _entityResolver;
    private readonly ILogger<QueryCatalogService> _logger;
    private readonly IReadOnlyList<QueryCatalogEntry> _entries;

    public QueryCatalogService(IEntityResolver entityResolver, ILogger<QueryCatalogService> logger)
    {
        _entityResolver = entityResolver;
        _logger = logger;
        _entries = BuildCatalogEntries();
    }

    public bool TryResolveQuery(string question, IntentDetectionResult intent, DatabaseMetadata metadata, out QueryCatalogResult result)
    {
        result = default!;
        var normalizedQuestion = question?.Trim() ?? string.Empty;
        var tokens = NormalizeTokens(normalizedQuestion);

        foreach (var entry in _entries)
        {
            if (entry.Intent.HasValue && entry.Intent.Value != intent.Type)
            {
                continue;
            }

            if (!entry.RequiredKeywords.All(tokens.Contains))
            {
                continue;
            }

            var context = new QueryCatalogMatchContext(normalizedQuestion, tokens, intent, metadata, _entityResolver);
            var query = entry.Build(context);
            if (query != null)
            {
                _logger.LogInformation("Query catalog matched entry {CatalogKey} for question '{Question}'.", entry.Key, normalizedQuestion);
                result = query with { };
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> NormalizeTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new System.Text.StringBuilder();

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

    private static List<QueryCatalogEntry> BuildCatalogEntries()
    {
        return new List<QueryCatalogEntry>
        {
            new("headcount_by_department_mn", "Department headcount Mongolian", IntentType.EmployeeCountByDepartment, new[] {"хэлтэс", "хүн", "тоо"}, BuildHeadcountByDepartment),
            new("headcount_by_department_mn_romanized", "Department headcount Mongolian romanized", IntentType.EmployeeCountByDepartment, new[] {"heltes", "hun", "too"}, BuildHeadcountByDepartment),
            new("headcount_by_department_mn_romanized_alt", "Department headcount Mongolian romanized alternate", IntentType.EmployeeCountByDepartment, new[] {"heltes", "ajiltan", "too"}, BuildHeadcountByDepartment),
            new("headcount_total_mn", "Total employee headcount Mongolian", IntentType.EmployeeCountTotal, new[] {"хэд", "хүн"}, BuildEmployeeCountTotal),
            new("headcount_total_mn_romanized", "Total employee headcount Mongolian romanized", IntentType.EmployeeCountTotal, new[] {"hed", "hun"}, BuildEmployeeCountTotal),
            new("headcount_total_mn_romanized_alt", "Total employee headcount Mongolian romanized alternate", IntentType.EmployeeCountTotal, new[] {"ajiltan", "too"}, BuildEmployeeCountTotal),
            new("headcount_total", "Total employee headcount", IntentType.EmployeeCountTotal, new[] {"employee", "count"}, BuildEmployeeCountTotal),
            new("active_employee_count", "Active employee count", null, new[] {"active", "employee", "count"}, BuildActiveEmployeeCount),
            new("employee_count_by_position", "Employee count by position", null, new[] {"position", "count", "employee"}, BuildEmployeeCountByPosition),
            new("hired_this_year", "Employees hired this year", null, new[] {"hired", "this", "year"}, BuildHiredThisYear),
            new("hired_this_month", "Employees hired this month", null, new[] {"hired", "this", "month"}, BuildHiredThisMonth),
            new("payroll_total", "Total payroll amount", IntentType.PayrollSummary, new[] {"payroll", "total", "sum"}, BuildPayrollTotal),
            new("payroll_by_month", "Payroll total by month", null, new[] {"payroll", "month", "total"}, BuildPayrollByMonth),
            new("payroll_by_year", "Payroll total by year", null, new[] {"payroll", "year", "total"}, BuildPayrollByYear),
            new("average_salary", "Average net salary", null, new[] {"average", "salary"}, BuildAverageSalary),
            new("average_salary_by_department", "Average salary by department", IntentType.AverageSalaryByDepartment, new[] {"average", "salary", "department"}, BuildAverageSalaryByDepartment),
            new("top_earners", "Top paid employees", IntentType.TopSalaryEmployee, new[] {"top", "salary", "employee"}, BuildTopEarners),
            new("payroll_count", "Payroll row count", null, new[] {"payroll", "count"}, BuildPayrollCount),
            new("department_payroll_expense", "Payroll expense by department", null, new[] {"department", "payroll", "sum"}, BuildPayrollByDepartment),
            new("bonus_total", "Total bonus paid", null, new[] {"bonus", "total"}, BuildBonusTotal),
            new("deduction_total", "Total deductions", null, new[] {"deduction", "total"}, BuildDeductionsTotal),
            new("average_base_salary_by_department", "Average base salary by department", null, new[] {"average", "salary", "department"}, BuildAverageBaseSalaryByDepartment),
            new("attendance_rate", "Attendance rate", IntentType.AttendanceRate, new[] {"attendance", "rate"}, BuildAttendanceRate),
            new("attendance_records_count", "Attendance record count", null, new[] {"attendance", "count"}, BuildAttendanceCount),
            new("absent_employee_count", "Absent employee count", IntentType.AbsentEmployees, new[] {"absent", "count"}, BuildAbsentCount),
            new("late_employee_count", "Late employee count", IntentType.LateEmployees, new[] {"late", "count"}, BuildLateCount),
            new("recent_attendance_records", "Recent attendance records", null, new[] {"attendance", "recent"}, BuildRecentAttendance),
            new("leave_request_count", "Leave request count", IntentType.LeaveSummary, new[] {"leave", "count"}, BuildLeaveRequestCount),
            new("approved_leave_count", "Approved leave count", null, new[] {"leave", "approved"}, BuildApprovedLeaveCount),
            new("pending_leave_count", "Pending leave count", null, new[] {"leave", "pending"}, BuildPendingLeaveCount),
            new("leave_days_this_year", "Leave days this year", null, new[] {"leave", "year"}, BuildLeaveDaysThisYear),
            new("training_sessions_count", "Training session count", IntentType.TrainingSummary, new[] {"training", "count"}, BuildTrainingCount),
            new("completed_training_count", "Completed training count", null, new[] {"training", "completed"}, BuildCompletedTrainingCount),
            new("training_participants_count", "Training participants count", null, new[] {"training", "participants"}, BuildTrainingParticipantsCount),
            new("employees_on_training", "Employees on training", null, new[] {"training", "employees"}, BuildEmployeesOnTraining),
            new("performance_average_score", "Average performance score", IntentType.PerformanceSummary, new[] {"performance", "average"}, BuildAveragePerformanceScore),
            new("performance_review_count", "Performance review count", IntentType.PerformanceSummary, new[] {"performance", "count"}, BuildPerformanceReviewCount),
            new("performance_below_threshold", "Performance below threshold", null, new[] {"performance", "below", "score"}, BuildPerformanceBelowThreshold),
            new("high_performers_count", "High performers count", null, new[] {"performance", "top", "score"}, BuildHighPerformersCount),
            new("top_performers", "Top performers by score", null, new[] {"top", "performers", "score"}, BuildTopPerformers),
            new("department_average_performance", "Department average performance", null, new[] {"department", "performance"}, BuildDepartmentAveragePerformance),
            new("department_headcount_top", "Top departments by headcount", null, new[] {"department", "top", "headcount"}, BuildTopDepartmentsByHeadcount),
            new("employees_by_department_and_position", "Employees by department and position", null, new[] {"department", "position"}, BuildEmployeesByDepartmentAndPosition),
            new("employees_without_attendance", "Employees without attendance records", null, new[] {"employee", "without", "attendance"}, BuildEmployeesWithoutAttendance),
            new("department_leave_count", "Leave requests by department", null, new[] {"department", "leave"}, BuildLeaveCountByDepartment),
            new("department_payroll_expense_rank", "Departments ranked by payroll expense", null, new[] {"department", "payroll", "rank"}, BuildPayrollRankByDepartment),
            new("assets_total_count", "Total assets count", null, new[] {"asset", "count"}, BuildAssetCount),
            new("assigned_assets_count", "Assigned assets count", null, new[] {"asset", "assigned"}, BuildAssignedAssetCount),
            new("unassigned_assets_count", "Unassigned assets count", null, new[] {"asset", "unassigned"}, BuildUnassignedAssetCount),
            new("assets_by_type", "Assets by type", null, new[] {"asset", "type"}, BuildAssetsByType),
            new("project_count", "Project count", null, new[] {"project", "count"}, BuildProjectCount),
            new("project_members_count", "Project members count", null, new[] {"project", "member"}, BuildProjectMembersCount),
            new("employees_per_project", "Employees per project", null, new[] {"project", "employee"}, BuildEmployeesPerProject),
            new("candidates_count", "Candidate count", null, new[] {"candidate", "count"}, BuildCandidateCount),
            new("employee_salary_details", "Employee salary details", null, new[] {"employee", "salary", "details"}, BuildEmployeeSalaryDetails),
            new("payroll_average_by_month", "Average payroll by month", null, new[] {"payroll", "average", "month"}, BuildPayrollAverageByMonth),
            new("payroll_average_by_department", "Average payroll by department", null, new[] {"payroll", "average", "department"}, BuildPayrollAverageByDepartment),
            new("top_departments_by_employee_count", "Departments with highest employee count", null, new[] {"department", "top", "employees"}, BuildTopDepartmentsByEmployees),
            new("top_paid_employees_by_department", "Top paid employees by department", null, new[] {"top", "paid", "department"}, BuildTopPaidEmployeesByDepartment),
            new("department_salary_expense", "Department salary expense", null, new[] {"department", "salary", "expense"}, BuildDepartmentSalaryExpense),
            new("employee_bonus_top", "Employees with highest bonuses", null, new[] {"bonus", "top", "employee"}, BuildTopBonusEmployees),
            new("employee_leave_stats", "Employee leave statistics", null, new[] {"employee", "leave", "statistics"}, BuildEmployeeLeaveStatistics)
        };
    }

    private static QueryCatalogResult? BuildHeadcountByDepartment(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (employees == null || departments == null)
        {
            return null;
        }

        if (!TryResolveDepartmentName(context, out var departmentName, out var parameterName, out var sqlCondition, out var parameters))
        {
            return new QueryCatalogResult(
                $"SELECT d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC",
                new Dictionary<string, object?>(), "headcount_by_department");
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE {sqlCondition} GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC",
            parameters, "headcount_by_department");
    }

    private static QueryCatalogResult? BuildEmployeeCountTotal(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        if (employees == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS EmployeeCount FROM [{employees}]", new Dictionary<string, object?>(), "headcount_total");
    }

    private static QueryCatalogResult? BuildActiveEmployeeCount(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        if (employees == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS ActiveEmployeeCount FROM [{employees}] WHERE IsActive = 1", new Dictionary<string, object?>(), "active_employee_count");
    }

    private static QueryCatalogResult? BuildEmployeeCountByPosition(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var positions = FindTableName(context.Metadata, "Positions");
        if (employees == null || positions == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT p.PositionName AS Position, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{positions}] p ON e.PositionId = p.PositionId GROUP BY p.PositionName ORDER BY EmployeeCount DESC",
            new Dictionary<string, object?>(), "employee_count_by_position");
    }

    private static QueryCatalogResult? BuildHiredThisYear(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        if (employees == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT COUNT(*) AS EmployeesHiredThisYear FROM [{employees}] WHERE YEAR(HireDate) = @Year",
            new Dictionary<string, object?> { ["Year"] = DateTime.UtcNow.Year }, "hired_this_year");
    }

    private static QueryCatalogResult? BuildHiredThisMonth(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        if (employees == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT COUNT(*) AS EmployeesHiredThisMonth FROM [{employees}] WHERE YEAR(HireDate) = @Year AND MONTH(HireDate) = @Month",
            new Dictionary<string, object?> { ["Year"] = DateTime.UtcNow.Year, ["Month"] = DateTime.UtcNow.Month }, "hired_this_month");
    }

    private static QueryCatalogResult? BuildPayrollTotal(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT SUM(COALESCE(NetSalary, 0)) AS TotalPayroll FROM [{payroll}]",
            new Dictionary<string, object?>(), "payroll_total");
    }

    private static QueryCatalogResult? BuildPayrollByMonth(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        var (month, year) = TryExtractMonthYear(context.Question);
        return new QueryCatalogResult(
            $"SELECT SUM(COALESCE(NetSalary, 0)) AS MonthlyPayrollTotal FROM [{payroll}] WHERE MONTH(PayMonth) = @Month AND YEAR(PayMonth) = @Year",
            new Dictionary<string, object?> { ["Month"] = month, ["Year"] = year }, "payroll_by_month");
    }

    private static QueryCatalogResult? BuildPayrollByYear(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        var year = TryExtractYear(context.Question);
        return new QueryCatalogResult(
            $"SELECT SUM(COALESCE(NetSalary, 0)) AS YearlyPayrollTotal FROM [{payroll}] WHERE YEAR(PayMonth) = @Year",
            new Dictionary<string, object?> { ["Year"] = year }, "payroll_by_year");
    }

    private static QueryCatalogResult? BuildAverageSalary(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT AVG(COALESCE(NetSalary, 0)) AS AverageNetSalary FROM [{payroll}]",
            new Dictionary<string, object?>(), "average_salary");
    }

    private static QueryCatalogResult? BuildAverageSalaryByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        if (!TryResolveDepartmentName(context, out var departmentName, out var parameterName, out var sqlCondition, out var parameters))
        {
            return new QueryCatalogResult(
                $"SELECT d.DepartmentName AS Department, AVG(COALESCE(p.NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY AverageNetSalary DESC",
                new Dictionary<string, object?>(), "average_salary_by_department");
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, AVG(COALESCE(p.NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE {sqlCondition} GROUP BY d.DepartmentName ORDER BY AverageNetSalary DESC",
            parameters, "average_salary_by_department");
    }

    private static QueryCatalogResult? BuildTopEarners(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        if (payroll == null || employees == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT TOP 20 e.FirstName + ' ' + e.LastName AS Employee, p.NetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId ORDER BY p.NetSalary DESC",
            new Dictionary<string, object?>(), "top_earners");
    }

    private static QueryCatalogResult? BuildPayrollCount(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS PayrollRecordCount FROM [{payroll}]", new Dictionary<string, object?>(), "payroll_count");
    }

    private static QueryCatalogResult? BuildPayrollByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, SUM(COALESCE(p.NetSalary, 0)) AS TotalPayroll FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY TotalPayroll DESC",
            new Dictionary<string, object?>(), "department_payroll_expense");
    }

    private static QueryCatalogResult? BuildBonusTotal(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT SUM(COALESCE(Bonus, 0)) AS TotalBonusPaid FROM [{payroll}]", new Dictionary<string, object?>(), "bonus_total");
    }

    private static QueryCatalogResult? BuildDeductionsTotal(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT SUM(COALESCE(Deduction, 0)) AS TotalDeductions FROM [{payroll}]", new Dictionary<string, object?>(), "deduction_total");
    }

    private static QueryCatalogResult? BuildAverageBaseSalaryByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, AVG(COALESCE(p.Salary, 0)) AS AverageSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY AverageSalary DESC",
            new Dictionary<string, object?>(), "average_base_salary_by_department");
    }

    private static QueryCatalogResult? BuildAttendanceRate(QueryCatalogMatchContext context)
    {
        var attendance = FindTableName(context.Metadata, "Attendance");
        if (attendance == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT CAST(SUM(CASE WHEN LOWER(COALESCE(Status, '')) IN ('present', 'late', 'available') THEN 1 ELSE 0 END) AS decimal(10,2)) / NULLIF(COUNT(*), 0) * 100 AS AttendanceRatePercentage FROM [{attendance}]",
            new Dictionary<string, object?>(), "attendance_rate");
    }

    private static QueryCatalogResult? BuildAttendanceCount(QueryCatalogMatchContext context)
    {
        var attendance = FindTableName(context.Metadata, "Attendance");
        if (attendance == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS AttendanceRecordCount FROM [{attendance}]", new Dictionary<string, object?>(), "attendance_records_count");
    }

    private static QueryCatalogResult? BuildAbsentCount(QueryCatalogMatchContext context)
    {
        var attendance = FindTableName(context.Metadata, "Attendance");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (attendance == null)
        {
            return null;
        }

        if (TryResolveDepartmentName(context, out var departmentName, out var parameterName, out var sqlCondition, out var parameters)
            && employees != null && departments != null)
        {
            var query = $"SELECT COUNT(*) AS AbsentCount FROM [{attendance}] a JOIN [{employees}] e ON a.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE ({sqlCondition}) AND (LOWER(COALESCE(a.Status, '')) LIKE '%absent%' OR LOWER(COALESCE(a.Status, '')) LIKE '%илэрсэн%')";
            return new QueryCatalogResult(query, parameters, "absent_employee_count");
        }

        return new QueryCatalogResult(
            $"SELECT COUNT(*) AS AbsentCount FROM [{attendance}] WHERE LOWER(COALESCE(Status, '')) LIKE '%absent%' OR LOWER(COALESCE(Status, '')) LIKE '%илэрсэн%'",
            new Dictionary<string, object?>(), "absent_employee_count");
    }

    private static QueryCatalogResult? BuildLateCount(QueryCatalogMatchContext context)
    {
        var attendance = FindTableName(context.Metadata, "Attendance");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (attendance == null)
        {
            return null;
        }

        if (TryResolveDepartmentName(context, out var departmentName, out var parameterName, out var sqlCondition, out var parameters)
            && employees != null && departments != null)
        {
            var query = $"SELECT COUNT(*) AS LateCount FROM [{attendance}] a JOIN [{employees}] e ON a.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE ({sqlCondition}) AND (LOWER(COALESCE(a.Status, '')) LIKE '%late%' OR LOWER(COALESCE(a.Status, '')) LIKE '%хоцрол%')";
            return new QueryCatalogResult(query, parameters, "late_employee_count");
        }

        return new QueryCatalogResult(
            $"SELECT COUNT(*) AS LateCount FROM [{attendance}] WHERE LOWER(COALESCE(Status, '')) LIKE '%late%' OR LOWER(COALESCE(Status, '')) LIKE '%хоцрол%'",
            new Dictionary<string, object?>(), "late_employee_count");
    }

    private static QueryCatalogResult? BuildRecentAttendance(QueryCatalogMatchContext context)
    {
        var attendance = FindTableName(context.Metadata, "Attendance");
        if (attendance == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT TOP 20 * FROM [{attendance}] ORDER BY AttendanceDate DESC",
            new Dictionary<string, object?>(), "recent_attendance_records");
    }

    private static QueryCatalogResult? BuildLeaveRequestCount(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        if (leave == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS LeaveRequestCount FROM [{leave}]", new Dictionary<string, object?>(), "leave_request_count");
    }

    private static QueryCatalogResult? BuildApprovedLeaveCount(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        if (leave == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS ApprovedLeaveCount FROM [{leave}] WHERE LOWER(COALESCE(Status, '')) = 'approved'", new Dictionary<string, object?>(), "approved_leave_count");
    }

    private static QueryCatalogResult? BuildPendingLeaveCount(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (leave == null)
        {
            return null;
        }

        if (TryResolveDepartmentName(context, out var departmentName, out var parameterName, out var sqlCondition, out var parameters)
            && employees != null && departments != null)
        {
            var query = $"SELECT COUNT(*) AS PendingLeaveCount FROM [{leave}] l JOIN [{employees}] e ON l.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId WHERE ({sqlCondition}) AND LOWER(COALESCE(l.Status, '')) IN ('pending', 'submitted')";
            return new QueryCatalogResult(query, parameters, "pending_leave_count");
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS PendingLeaveCount FROM [{leave}] WHERE LOWER(COALESCE(Status, '')) IN ('pending', 'submitted')", new Dictionary<string, object?>(), "pending_leave_count");
    }

    private static QueryCatalogResult? BuildLeaveDaysThisYear(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        if (leave == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT SUM(COALESCE(DurationDays, 0)) AS LeaveDaysThisYear FROM [{leave}] WHERE YEAR(StartDate) = @Year",
            new Dictionary<string, object?> { ["Year"] = DateTime.UtcNow.Year }, "leave_days_this_year");
    }

    private static QueryCatalogResult? BuildTrainingCount(QueryCatalogMatchContext context)
    {
        var training = FindTableName(context.Metadata, "Trainings");
        if (training == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS TrainingCount FROM [{training}]", new Dictionary<string, object?>(), "training_sessions_count");
    }

    private static QueryCatalogResult? BuildCompletedTrainingCount(QueryCatalogMatchContext context)
    {
        var training = FindTableName(context.Metadata, "TrainingRecords");
        if (training == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS CompletedTrainingCount FROM [{training}] WHERE LOWER(COALESCE(Status, '')) IN ('completed', 'finished')", new Dictionary<string, object?>(), "completed_training_count");
    }

    private static QueryCatalogResult? BuildTrainingParticipantsCount(QueryCatalogMatchContext context)
    {
        var training = FindTableName(context.Metadata, "TrainingRecords");
        if (training == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(DISTINCT EmployeeId) AS TrainingParticipants FROM [{training}]", new Dictionary<string, object?>(), "training_participants_count");
    }

    private static QueryCatalogResult? BuildEmployeesOnTraining(QueryCatalogMatchContext context)
    {
        var training = FindTableName(context.Metadata, "TrainingRecords");
        if (training == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT DISTINCT EmployeeId FROM [{training}] WHERE LOWER(COALESCE(Status, '')) IN ('active', 'in progress', 'started')", new Dictionary<string, object?>(), "employees_on_training");
    }

    private static QueryCatalogResult? BuildAveragePerformanceScore(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        if (performance == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT AVG(CAST(ISNULL(Score, 0) AS decimal(10,2))) AS AveragePerformanceScore FROM [{performance}]", new Dictionary<string, object?>(), "performance_average_score");
    }

    private static QueryCatalogResult? BuildPerformanceReviewCount(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        if (performance == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS PerformanceReviewCount FROM [{performance}]", new Dictionary<string, object?>(), "performance_review_count");
    }

    private static QueryCatalogResult? BuildPerformanceBelowThreshold(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        if (performance == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS ReviewsBelowThreshold FROM [{performance}] WHERE COALESCE(Score, 0) < @ScoreThreshold", new Dictionary<string, object?> { ["ScoreThreshold"] = 60 }, "performance_below_threshold");
    }

    private static QueryCatalogResult? BuildHighPerformersCount(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        if (performance == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS HighPerformers FROM [{performance}] WHERE COALESCE(Score, 0) >= @ScoreThreshold", new Dictionary<string, object?> { ["ScoreThreshold"] = 85 }, "high_performers_count");
    }

    private static QueryCatalogResult? BuildTopPerformers(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        var employees = FindTableName(context.Metadata, "Employees");
        if (performance == null || employees == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT TOP 10 e.FirstName + ' ' + e.LastName AS Employee, CAST(AVG(COALESCE(p.Score, 0)) AS decimal(10,2)) AS AverageScore " +
            $"FROM [{performance}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId " +
            "GROUP BY e.FirstName, e.LastName ORDER BY AverageScore DESC",
            new Dictionary<string, object?>(), "top_performers");
    }

    private static QueryCatalogResult? BuildDepartmentAveragePerformance(QueryCatalogMatchContext context)
    {
        var performance = FindTableName(context.Metadata, "PerformanceReviews");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (performance == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, AVG(CAST(ISNULL(p.Score, 0) AS decimal(10,2))) AS AverageScore FROM [{performance}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY AverageScore DESC",
            new Dictionary<string, object?>(), "department_average_performance");
    }

    private static QueryCatalogResult? BuildTopDepartmentsByHeadcount(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT TOP 10 d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC",
            new Dictionary<string, object?>(), "department_headcount_top");
    }

    private static QueryCatalogResult? BuildEmployeesByDepartmentAndPosition(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        var positions = FindTableName(context.Metadata, "Positions");
        if (employees == null || departments == null || positions == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, p.PositionName AS Position, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId JOIN [{positions}] p ON e.PositionId = p.PositionId GROUP BY d.DepartmentName, p.PositionName ORDER BY d.DepartmentName, EmployeeCount DESC",
            new Dictionary<string, object?>(), "employees_by_department_and_position");
    }

    private static QueryCatalogResult? BuildEmployeesWithoutAttendance(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var attendance = FindTableName(context.Metadata, "Attendance");
        if (employees == null || attendance == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT e.EmployeeId, e.FirstName, e.LastName FROM [{employees}] e WHERE e.EmployeeId NOT IN (SELECT DISTINCT EmployeeId FROM [{attendance}])",
            new Dictionary<string, object?>(), "employees_without_attendance");
    }

    private static QueryCatalogResult? BuildLeaveCountByDepartment(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (leave == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, COUNT(l.LeaveRequestId) AS LeaveRequests FROM [{leave}] l JOIN [{employees}] e ON l.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY LeaveRequests DESC",
            new Dictionary<string, object?>(), "department_leave_count");
    }

    private static QueryCatalogResult? BuildPayrollRankByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult(
            $"SELECT d.DepartmentName AS Department, SUM(COALESCE(p.NetSalary, 0)) AS TotalPayroll FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY TotalPayroll DESC",
            new Dictionary<string, object?>(), "department_payroll_expense_rank");
    }

    private static QueryCatalogResult? BuildAssetCount(QueryCatalogMatchContext context)
    {
        var assets = FindTableName(context.Metadata, "Assets");
        if (assets == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS AssetCount FROM [{assets}]", new Dictionary<string, object?>(), "assets_total_count");
    }

    private static QueryCatalogResult? BuildAssignedAssetCount(QueryCatalogMatchContext context)
    {
        var assets = FindTableName(context.Metadata, "Assets");
        if (assets == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS AssignedAssetCount FROM [{assets}] WHERE EmployeeId IS NOT NULL", new Dictionary<string, object?>(), "assigned_assets_count");
    }

    private static QueryCatalogResult? BuildUnassignedAssetCount(QueryCatalogMatchContext context)
    {
        var assets = FindTableName(context.Metadata, "Assets");
        if (assets == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS UnassignedAssetCount FROM [{assets}] WHERE EmployeeId IS NULL", new Dictionary<string, object?>(), "unassigned_assets_count");
    }

    private static QueryCatalogResult? BuildAssetsByType(QueryCatalogMatchContext context)
    {
        var assets = FindTableName(context.Metadata, "Assets");
        if (assets == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT AssetType, COUNT(*) AS AssetCount FROM [{assets}] GROUP BY AssetType ORDER BY AssetCount DESC", new Dictionary<string, object?>(), "assets_by_type");
    }

    private static QueryCatalogResult? BuildProjectCount(QueryCatalogMatchContext context)
    {
        var projects = FindTableName(context.Metadata, "Projects");
        if (projects == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS ProjectCount FROM [{projects}]", new Dictionary<string, object?>(), "project_count");
    }

    private static QueryCatalogResult? BuildProjectMembersCount(QueryCatalogMatchContext context)
    {
        var members = FindTableName(context.Metadata, "ProjectMembers");
        if (members == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS ProjectMemberCount FROM [{members}]", new Dictionary<string, object?>(), "project_members_count");
    }

    private static QueryCatalogResult? BuildEmployeesPerProject(QueryCatalogMatchContext context)
    {
        var members = FindTableName(context.Metadata, "ProjectMembers");
        var projects = FindTableName(context.Metadata, "Projects");
        if (members == null || projects == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT p.ProjectName AS Project, COUNT(pm.EmployeeId) AS EmployeeCount FROM [{members}] pm JOIN [{projects}] p ON pm.ProjectId = p.ProjectId GROUP BY p.ProjectName ORDER BY EmployeeCount DESC", new Dictionary<string, object?>(), "employees_per_project");
    }

    private static QueryCatalogResult? BuildCandidateCount(QueryCatalogMatchContext context)
    {
        var candidates = FindTableName(context.Metadata, "Candidates");
        if (candidates == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT COUNT(*) AS CandidateCount FROM [{candidates}]", new Dictionary<string, object?>(), "candidates_count");
    }

    private static QueryCatalogResult? BuildEmployeeSalaryDetails(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        if (payroll == null || employees == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT e.EmployeeId, e.FirstName, e.LastName, p.PayMonth AS PayrollMonth, p.NetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId ORDER BY p.PayMonth DESC", new Dictionary<string, object?>(), "employee_salary_details");
    }

    private static QueryCatalogResult? BuildPayrollAverageByMonth(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        if (payroll == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT MONTH(PayMonth) AS PayrollMonth, AVG(COALESCE(NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] GROUP BY MONTH(PayMonth) ORDER BY PayrollMonth", new Dictionary<string, object?>(), "payroll_average_by_month");
    }

    private static QueryCatalogResult? BuildPayrollAverageByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT d.DepartmentName AS Department, AVG(COALESCE(p.NetSalary, 0)) AS AverageNetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY AverageNetSalary DESC", new Dictionary<string, object?>(), "payroll_average_by_department");
    }

    private static QueryCatalogResult? BuildTopDepartmentsByEmployees(QueryCatalogMatchContext context)
    {
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT TOP 10 d.DepartmentName AS Department, COUNT(e.EmployeeId) AS EmployeeCount FROM [{employees}] e JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY EmployeeCount DESC", new Dictionary<string, object?>(), "top_departments_by_employee_count");
    }

    private static QueryCatalogResult? BuildTopPaidEmployeesByDepartment(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT TOP 10 d.DepartmentName AS Department, e.FirstName + ' ' + e.LastName AS Employee, p.NetSalary FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId ORDER BY p.NetSalary DESC", new Dictionary<string, object?>(), "top_paid_employees_by_department");
    }

    private static QueryCatalogResult? BuildDepartmentSalaryExpense(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        var departments = FindTableName(context.Metadata, "Departments");
        if (payroll == null || employees == null || departments == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT d.DepartmentName AS Department, SUM(COALESCE(p.NetSalary, 0)) AS TotalPayrollExpense FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId JOIN [{departments}] d ON e.DepartmentId = d.DepartmentId GROUP BY d.DepartmentName ORDER BY TotalPayrollExpense DESC", new Dictionary<string, object?>(), "department_salary_expense");
    }

    private static QueryCatalogResult? BuildTopBonusEmployees(QueryCatalogMatchContext context)
    {
        var payroll = FindTableName(context.Metadata, "Payroll");
        var employees = FindTableName(context.Metadata, "Employees");
        if (payroll == null || employees == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT TOP 20 e.EmployeeId, e.FirstName + ' ' + e.LastName AS Employee, p.Bonus FROM [{payroll}] p JOIN [{employees}] e ON p.EmployeeId = e.EmployeeId ORDER BY p.Bonus DESC", new Dictionary<string, object?>(), "employee_bonus_top");
    }

    private static QueryCatalogResult? BuildEmployeeLeaveStatistics(QueryCatalogMatchContext context)
    {
        var leave = FindTableName(context.Metadata, "LeaveRequests");
        if (leave == null)
        {
            return null;
        }

        return new QueryCatalogResult($"SELECT EmployeeId, COUNT(*) AS LeaveRequestCount, SUM(COALESCE(DurationDays, 0)) AS LeaveDays FROM [{leave}] GROUP BY EmployeeId ORDER BY LeaveDays DESC", new Dictionary<string, object?>(), "employee_leave_stats");
    }

    private static bool TryResolveDepartmentName(QueryCatalogMatchContext context, out string? departmentName, out string parameterName, out string sqlCondition, out Dictionary<string, object?> parameters)
    {
        departmentName = null;
        parameterName = "DepartmentName";
        sqlCondition = string.Empty;
        parameters = new Dictionary<string, object?>();

        var entityResolution = context.EntityResolver.ResolveDepartmentNameAsync(context.Question, context.Metadata).GetAwaiter().GetResult();
        if (entityResolution.CanonicalValue != null)
        {
            if (entityResolution.UseLikeFallback)
            {
                sqlCondition = $"d.DepartmentName LIKE @{parameterName}Pattern";
                parameters[$"{parameterName}Pattern"] = entityResolution.LikePattern;
            }
            else
            {
                sqlCondition = $"d.DepartmentName = @{parameterName}";
                parameters[parameterName] = entityResolution.CanonicalValue;
            }

            departmentName = entityResolution.CanonicalValue;
            return true;
        }

        return false;
    }

    private static string? FindTableName(DatabaseMetadata metadata, string canonicalName)
    {
        var match = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, canonicalName, StringComparison.OrdinalIgnoreCase));
        return match?.Name;
    }

    private static (int Month, int Year) TryExtractMonthYear(string question)
    {
        var month = DateTime.UtcNow.Month;
        var year = DateTime.UtcNow.Year;

        if (!string.IsNullOrWhiteSpace(question))
        {
            var monthNames = DateTimeFormatInfo.InvariantInfo.MonthNames
                .Where(m => !string.IsNullOrEmpty(m))
                .Select((name, index) => (name, index: index + 1));

            foreach (var (name, index) in monthNames)
            {
                if (question.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    month = index;
                }
            }

            var yearMatch = Regex.Match(question, "(20\\d{2}|19\\d{2})");
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out var parsedYear))
            {
                year = parsedYear;
            }

            var monthNumberMatch = Regex.Match(question, "\\b(1[0-2]|0?[1-9])\\b");
            if (monthNumberMatch.Success && int.TryParse(monthNumberMatch.Value, out var parsedMonth) && parsedMonth >= 1 && parsedMonth <= 12)
            {
                month = parsedMonth;
            }
        }

        return (month, year);
    }

    private static int TryExtractYear(string question)
    {
        var year = DateTime.UtcNow.Year;
        if (string.IsNullOrWhiteSpace(question))
        {
            return year;
        }

        var match = Regex.Match(question, "(20\\d{2}|19\\d{2})");
        if (match.Success && int.TryParse(match.Value, out var extractedYear))
        {
            return extractedYear;
        }

        return year;
    }

    private sealed record QueryCatalogEntry(string Key, string Description, IntentType? Intent, string[] RequiredKeywords, Func<QueryCatalogMatchContext, QueryCatalogResult?> Build);

    private sealed record QueryCatalogMatchContext(string Question, HashSet<string> Tokens, IntentDetectionResult Intent, DatabaseMetadata Metadata, IEntityResolver EntityResolver);
}
