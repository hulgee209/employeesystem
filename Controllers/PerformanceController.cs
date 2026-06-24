using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "Admin,HR,Manager,Employee")]
public class PerformanceController : Controller
{
    private readonly EmployeeDbContext _context;

    public PerformanceController(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, int? departmentId, DateOnly? date)
    {
        var query = BuildAuthorizedPerformanceQuery().AsNoTracking();
        query = ApplyFilters(query, search, departmentId, date);

        var averageScore = await query.AnyAsync()
            ? await query.AverageAsync(r => (decimal?)r.Score) ?? 0
            : 0;

        var topEmployee = await query
            .GroupBy(r => new { r.EmployeeId, r.Employee.FirstName, r.Employee.LastName })
            .Select(g => new
            {
                Name = g.Key.FirstName + " " + g.Key.LastName,
                Average = g.Average(r => (decimal?)r.Score) ?? 0
            })
            .OrderByDescending(x => x.Average)
            .FirstOrDefaultAsync();

        var topDepartment = await query
            .GroupBy(r => r.Employee.Department.DepartmentName)
            .Select(g => new
            {
                Name = g.Key,
                Average = g.Average(r => (decimal?)r.Score) ?? 0
            })
            .OrderByDescending(x => x.Average)
            .FirstOrDefaultAsync();

        var records = await query
            .OrderByDescending(r => r.ReviewDate)
            .ThenBy(r => r.Employee.LastName)
            .ThenBy(r => r.Employee.FirstName)
            .Take(500)
            .Select(r => new PerformanceRowViewModel
            {
                ReviewId = r.ReviewId,
                EmployeeId = r.EmployeeId,
                EmployeeName = r.Employee.FirstName + " " + r.Employee.LastName,
                DepartmentName = r.Employee.Department.DepartmentName,
                ReviewDate = r.ReviewDate,
                Score = r.Score,
                Reviewer = null,
                Comments = r.Comments
            })
            .ToListAsync();

        var model = new PerformanceIndexViewModel
        {
            Search = search,
            DepartmentId = departmentId,
            Date = date,
            Departments = await BuildDepartmentItemsAsync(),
            AveragePerformanceScore = averageScore,
            TopEmployee = topEmployee == null ? "-" : $"{topEmployee.Name} ({topEmployee.Average:N2})",
            HighestDepartmentScore = topDepartment == null ? "-" : $"{topDepartment.Name} ({topDepartment.Average:N2})",
            Records = records
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> DepartmentComparison()
    {
        var data = await BuildAuthorizedPerformanceQuery()
            .AsNoTracking()
            .GroupBy(r => r.Employee.Department.DepartmentName)
            .Select(g => new { label = g.Key, score = g.Average(r => (decimal?)r.Score) ?? 0 })
            .OrderByDescending(x => x.score)
            .Take(10)
            .ToListAsync();

        return Json(data);
    }

    [HttpGet]
    public async Task<IActionResult> ScoreDistribution()
    {
        var scores = await BuildAuthorizedPerformanceQuery()
            .AsNoTracking()
            .Where(r => r.Score.HasValue)
            .Select(r => r.Score!.Value)
            .ToListAsync();

        var buckets = new[]
        {
            new { label = "0-1.9", min = 0m, max = 1.99m },
            new { label = "2.0-2.9", min = 2m, max = 2.99m },
            new { label = "3.0-3.9", min = 3m, max = 3.99m },
            new { label = "4.0-4.4", min = 4m, max = 4.49m },
            new { label = "4.5-5.0", min = 4.5m, max = 5m }
        };

        var data = buckets
            .Select(b => new
            {
                b.label,
                count = scores.Count(s => s >= b.min && s <= b.max)
            })
            .ToList();

        return Json(data);
    }

    private IQueryable<PerformanceReview> ApplyFilters(
        IQueryable<PerformanceReview> query,
        string? search,
        int? departmentId,
        DateOnly? date)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.Employee.FirstName.Contains(search) ||
                r.Employee.LastName.Contains(search) ||
                (r.Employee.Email != null && r.Employee.Email.Contains(search)));
        }

        if (departmentId.HasValue)
        {
            query = query.Where(r => r.Employee.DepartmentId == departmentId.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(r => r.ReviewDate == date.Value);
        }

        return query;
    }

    private IQueryable<PerformanceReview> BuildAuthorizedPerformanceQuery()
    {
        return _context.PerformanceReviews.AsQueryable();
    }

    private async Task<IReadOnlyList<SelectListItem>> BuildDepartmentItemsAsync()
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.DepartmentName)
            .Select(d => new SelectListItem(d.DepartmentName, d.DepartmentId.ToString()))
            .ToListAsync();
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
