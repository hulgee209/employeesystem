using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "Admin,HR,Employee")]
public class PayrollController : Controller
{
    private readonly EmployeeDbContext _context;

    public PayrollController(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, int? departmentId, string? month)
    {
        var query = BuildAuthorizedPayrollQuery().AsNoTracking();
        query = ApplyFilters(query, search, departmentId, month);

        var salaryStats = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Average = g.Average(p => p.NetSalary) ?? 0,
                Highest = g.Max(p => p.NetSalary) ?? 0,
                Lowest = g.Min(p => p.NetSalary) ?? 0,
                Total = g.Sum(p => p.NetSalary) ?? 0
            })
            .FirstOrDefaultAsync();

        var records = await query
            .OrderByDescending(p => p.PayrollMonth)
            .ThenBy(p => p.Employee.LastName)
            .ThenBy(p => p.Employee.FirstName)
            .Take(500)
            .Select(p => new PayrollRowViewModel
            {
                PayrollId = p.PayrollId,
                EmployeeId = p.EmployeeId,
                EmployeeName = p.Employee.FirstName + " " + p.Employee.LastName,
                DepartmentName = p.Employee.Department.DepartmentName,
                PayrollMonth = p.PayrollMonth,
                BaseSalary = p.BaseSalary ?? 0,
                Bonus = p.Bonus ?? 0,
                Deductions = p.Deductions ?? 0,
                NetSalary = p.NetSalary ?? 0
            })
            .ToListAsync();

        var model = new PayrollIndexViewModel
        {
            Search = search,
            DepartmentId = departmentId,
            Month = month,
            Departments = await BuildDepartmentItemsAsync(),
            AverageSalary = salaryStats?.Average ?? 0,
            HighestSalary = salaryStats?.Highest ?? 0,
            LowestSalary = salaryStats?.Lowest ?? 0,
            TotalPayrollCost = salaryStats?.Total ?? 0,
            Records = records
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> SalaryDistribution(string? month)
    {
        var query = BuildAuthorizedPayrollQuery().AsNoTracking();

        if (TryParseMonth(month, out var selectedMonth))
        {
            query = query.Where(p =>
                p.PayrollMonth.HasValue &&
                p.PayrollMonth.Value.Year == selectedMonth.Year &&
                p.PayrollMonth.Value.Month == selectedMonth.Month);
        }

        var salaries = await query
            .Select(p => p.NetSalary ?? 0)
            .ToListAsync();

        var buckets = new[]
        {
            new { label = "0-999", min = 0m, max = 999m },
            new { label = "1,000-2,999", min = 1000m, max = 2999m },
            new { label = "3,000-4,999", min = 3000m, max = 4999m },
            new { label = "5,000-7,499", min = 5000m, max = 7499m },
            new { label = "7,500+", min = 7500m, max = decimal.MaxValue }
        };

        var data = buckets
            .Select(b => new
            {
                b.label,
                count = salaries.Count(s => s >= b.min && s <= b.max)
            })
            .ToList();

        return Json(data);
    }

    [HttpGet]
    public async Task<IActionResult> DepartmentSalaryCost(string? month)
    {
        var query = BuildAuthorizedPayrollQuery().AsNoTracking();

        if (TryParseMonth(month, out var selectedMonth))
        {
            query = query.Where(p =>
                p.PayrollMonth.HasValue &&
                p.PayrollMonth.Value.Year == selectedMonth.Year &&
                p.PayrollMonth.Value.Month == selectedMonth.Month);
        }

        var data = await query
            .GroupBy(p => p.Employee.Department.DepartmentName)
            .Select(g => new { label = g.Key, amount = g.Sum(p => p.NetSalary) ?? 0 })
            .OrderByDescending(x => x.amount)
            .Take(10)
            .ToListAsync();

        return Json(data);
    }

    private IQueryable<Payroll> ApplyFilters(
        IQueryable<Payroll> query,
        string? search,
        int? departmentId,
        string? month)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Employee.FirstName.Contains(search) ||
                p.Employee.LastName.Contains(search) ||
                (p.Employee.Email != null && p.Employee.Email.Contains(search)));
        }

        if (departmentId.HasValue)
        {
            query = query.Where(p => p.Employee.DepartmentId == departmentId.Value);
        }

        if (TryParseMonth(month, out var selectedMonth))
        {
            query = query.Where(p =>
                p.PayrollMonth.HasValue &&
                p.PayrollMonth.Value.Year == selectedMonth.Year &&
                p.PayrollMonth.Value.Month == selectedMonth.Month);
        }

        return query;
    }

    private IQueryable<Payroll> BuildAuthorizedPayrollQuery()
    {
        return _context.Payroll.AsQueryable();
    }

    private async Task<IReadOnlyList<SelectListItem>> BuildDepartmentItemsAsync()
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.DepartmentName)
            .Select(d => new SelectListItem(d.DepartmentName, d.DepartmentId.ToString()))
            .ToListAsync();
    }

    private static bool TryParseMonth(string? month, out DateOnly selectedMonth)
    {
        selectedMonth = default;

        if (string.IsNullOrWhiteSpace(month))
        {
            return false;
        }

        return DateOnly.TryParse($"{month}-01", out selectedMonth);
    }

    private bool IsLimitedEmployeeUser()
    {
        return User.IsInRole("Employee") &&
            !User.IsInRole("Admin") &&
            !User.IsInRole("HR") &&
            !User.IsInRole("Manager");
    }

    private bool TryGetEmployeeId(out int employeeId)
    {
        return int.TryParse(User.FindFirst("EmployeeId")?.Value, out employeeId);
    }
}
