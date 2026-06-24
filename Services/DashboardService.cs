using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public interface IDashboardService
{
    Task<HrDashboardViewModel> GetHrDashboardAsync();
    Task<ManagerDashboardViewModel> GetManagerDashboardAsync(int managerId);
    Task<EmployeeDashboardViewModel> GetEmployeeDashboardAsync(int employeeId);
}

public class DashboardService : IDashboardService
{
    private readonly EmployeeDbContext _context;
    private readonly IMemoryCache _cache;
    private const string HR_DASHBOARD_CACHE_KEY = "hr-dashboard-v2";
    private const int CACHE_DURATION_MINUTES = 5;

    public DashboardService(EmployeeDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<HrDashboardViewModel> GetHrDashboardAsync()
    {
        if (_cache.TryGetValue(HR_DASHBOARD_CACHE_KEY, out HrDashboardViewModel? cachedDashboard))
        {
            if (cachedDashboard != null)
                return cachedDashboard;
        }

        var totalEmployees = await _context.Employees.CountAsync();

        var departmentRows = await _context.Employees
            .AsNoTracking()
            .GroupBy(e => new { e.DepartmentId, e.Department!.DepartmentName })
            .Select(g => new { g.Key.DepartmentId, g.Key.DepartmentName, EmployeeCount = g.Count() })
            .ToListAsync();

        var latestPayrollRows = await _context.Payroll
            .AsNoTracking()
            .Where(p => p.NetSalary.HasValue)
            .Select(p => new
            {
                p.EmployeeId,
                p.Employee.DepartmentId,
                NetSalary = p.NetSalary!.Value,
                p.PayrollMonth,
                p.PayrollId
            })
            .ToListAsync();

        var averageSalaryByDepartment = latestPayrollRows
            .GroupBy(p => p.EmployeeId)
            .Select(g => g
                .OrderByDescending(p => p.PayrollMonth)
                .ThenByDescending(p => p.PayrollId)
                .First())
            .GroupBy(p => p.DepartmentId)
            .ToDictionary(g => g.Key, g => g.Average(p => p.NetSalary));

        var byDepartment = departmentRows
            .Select(d => new DepartmentSummary(
                d.DepartmentId,
                d.DepartmentName,
                d.EmployeeCount,
                averageSalaryByDepartment.GetValueOrDefault(d.DepartmentId)))
            .ToList();

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var recentHires = await _context.Employees
            .Where(e => e.HireDate.HasValue && e.HireDate.Value >= DateOnly.FromDateTime(thirtyDaysAgo))
            .OrderByDescending(e => e.HireDate)
            .Take(10)
            .Select(e => new EmployeeHireSummary(e.EmployeeId, e.FirstName, e.LastName, e.Department!.DepartmentName, e.HireDate!.Value.ToDateTime(TimeOnly.MinValue)))
            .ToListAsync();

        var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalPayrollThisMonth = await _context.Payroll
            .AsNoTracking()
            .Where(p =>
                p.PayrollMonth.HasValue &&
                p.PayrollMonth.Value.Year == currentMonth.Year &&
                p.PayrollMonth.Value.Month == currentMonth.Month)
            .SumAsync(p => p.NetSalary ?? 0);

        var activeEmployees = await _context.Employees.CountAsync(e => e.IsActive);

        var dashboard = new HrDashboardViewModel(totalEmployees, activeEmployees, 5, 0, totalPayrollThisMonth, byDepartment, recentHires);

        _cache.Set(HR_DASHBOARD_CACHE_KEY, dashboard, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
        return dashboard;
    }

    public async Task<ManagerDashboardViewModel> GetManagerDashboardAsync(int managerId)
    {
        var teamMembers = await _context.Employees
            .AsNoTracking()
            .Where(e => e.ManagerId == managerId)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new TeamMemberSummary(
                e.EmployeeId,
                e.FirstName,
                e.LastName,
                e.Position!.PositionName,
                _context.PerformanceReviews
                    .Where(r => r.EmployeeId == e.EmployeeId && r.ReviewDate.HasValue)
                    .OrderByDescending(r => r.ReviewDate)
                    .Select(r => (DateTime?)r.ReviewDate!.Value.ToDateTime(TimeOnly.MinValue))
                    .FirstOrDefault()))
            .ToListAsync();

        var reviewCutoff = DateTime.UtcNow.AddMonths(-6);
        var pendingPerformanceReviews = teamMembers.Count(member =>
            !member.LastReviewDate.HasValue ||
            member.LastReviewDate.Value < reviewCutoff);

        return new ManagerDashboardViewModel(teamMembers.Count, teamMembers, pendingPerformanceReviews, 0);
    }

    public async Task<EmployeeDashboardViewModel> GetEmployeeDashboardAsync(int employeeId)
    {
        var employee = await _context.Employees
            .Where(e => e.EmployeeId == employeeId)
            .FirstOrDefaultAsync();

        if (employee == null)
        {
            return new EmployeeDashboardViewModel(
                new EmployeeSummary(0, "N/A", "N/A", "N/A", "N/A", "N/A", "N/A", DateTime.MinValue),
                null,
                20,
                new List<NotificationSummary>()
            );
        }

        var employeeSummary = new EmployeeSummary(
            employee.EmployeeId,
            employee.FirstName,
            employee.LastName,
            employee.Email ?? "N/A",
            employee.Phone ?? "N/A",
            employee.Department!.DepartmentName,
            employee.Position!.PositionName,
            employee.HireDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue
        );

        var latestPayslip = await _context.Payroll
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.PayrollMonth)
            .FirstOrDefaultAsync();

        var payslipSummary = latestPayslip != null
            ? new PayslipSummary(latestPayslip.PayrollId, latestPayslip.BaseSalary ?? 0, latestPayslip.Bonus ?? 0, latestPayslip.Deductions ?? 0, latestPayslip.NetSalary ?? 0, latestPayslip.PayrollMonth?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow)
            : null;

        return new EmployeeDashboardViewModel(employeeSummary, payslipSummary, 20, new List<NotificationSummary>());
    }
}
