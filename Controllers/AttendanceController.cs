using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "Admin,HR,Manager,Employee")]
public class AttendanceController : Controller
{
    private readonly EmployeeDbContext _context;

    public AttendanceController(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(
        string? search,
        int? departmentId,
        DateOnly? date,
        string? status)
    {
        var query = BuildAuthorizedAttendanceQuery()
            .AsNoTracking();

        query = ApplyFilters(query, search, departmentId, date, status);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var widgetQuery = BuildAuthorizedAttendanceQuery()
            .AsNoTracking()
            .Where(a => a.AttendanceDate == (date ?? today));

        var records = await query
            .OrderByDescending(a => a.AttendanceDate)
            .ThenBy(a => a.Employee.LastName)
            .ThenBy(a => a.Employee.FirstName)
            .Take(500)
            .Select(a => new AttendanceRowViewModel
            {
                AttendanceId = a.AttendanceId,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                DepartmentName = a.Employee.Department.DepartmentName,
                AttendanceDate = a.AttendanceDate,
                CheckInTime = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                Status = a.Status ?? "-"
            })
            .ToListAsync();

        var model = new AttendanceIndexViewModel
        {
            Search = search,
            DepartmentId = departmentId,
            Date = date,
            Status = status,
            Departments = await BuildDepartmentItemsAsync(),
            Statuses = await BuildAttendanceStatusItemsAsync(),
            PresentEmployees = await widgetQuery.CountAsync(a => a.Status == "Present"),
            LateEmployees = await widgetQuery.CountAsync(a => a.Status == "Late"),
            AbsentEmployees = await widgetQuery.CountAsync(a => a.Status == "Absent"),
            Records = records
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> StatusChart(DateOnly? date)
    {
        var query = BuildAuthorizedAttendanceQuery()
            .AsNoTracking();

        if (date.HasValue)
        {
            query = query.Where(a => a.AttendanceDate == date.Value);
        }

        var data = await query
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        return Json(data);
    }

    [HttpGet]
    public async Task<IActionResult> MonthlyTrend()
    {
        var startMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);

        var rows = await BuildAuthorizedAttendanceQuery()
            .AsNoTracking()
            .Where(a => a.AttendanceDate >= startMonth)
            .GroupBy(a => new { a.AttendanceDate.Year, a.AttendanceDate.Month, a.Status })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Status,
                Count = g.Count()
            })
            .ToListAsync();

        var data = Enumerable.Range(0, 6)
            .Select(offset =>
            {
                var month = startMonth.AddMonths(offset);
                return new
                {
                    label = $"{month.Year}-{month.Month:00}",
                    present = rows.Where(r => r.Year == month.Year && r.Month == month.Month && r.Status == "Present").Sum(r => r.Count),
                    late = rows.Where(r => r.Year == month.Year && r.Month == month.Month && r.Status == "Late").Sum(r => r.Count),
                    absent = rows.Where(r => r.Year == month.Year && r.Month == month.Month && r.Status == "Absent").Sum(r => r.Count)
                };
            })
            .ToList();

        return Json(data);
    }

    private IQueryable<Attendance> ApplyFilters(
        IQueryable<Attendance> query,
        string? search,
        int? departmentId,
        DateOnly? date,
        string? status)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a =>
                a.Employee.FirstName.Contains(search) ||
                a.Employee.LastName.Contains(search) ||
                (a.Employee.Email != null && a.Employee.Email.Contains(search)));
        }

        if (departmentId.HasValue)
        {
            query = query.Where(a => a.Employee.DepartmentId == departmentId.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(a => a.AttendanceDate == date.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        return query;
    }

    private IQueryable<Attendance> BuildAuthorizedAttendanceQuery()
    {
        return _context.Attendance.AsQueryable();
    }

    private async Task<IReadOnlyList<SelectListItem>> BuildDepartmentItemsAsync()
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.DepartmentName)
            .Select(d => new SelectListItem(d.DepartmentName, d.DepartmentId.ToString()))
            .ToListAsync();
    }

    private async Task<IReadOnlyList<SelectListItem>> BuildAttendanceStatusItemsAsync()
    {
        return await BuildAuthorizedAttendanceQuery()
            .AsNoTracking()
            .Select(a => a.Status)
            .Distinct()
            .OrderBy(s => s)
            .Where(s => s != null)
            .Select(s => new SelectListItem(ToMongolianStatus(s!), s!))
            .ToListAsync();
    }

    private static string ToMongolianStatus(string status)
    {
        return status switch
        {
            "Present" => "Ирсэн",
            "Late" => "Хоцорсон",
            "Absent" => "Тасалсан",
            _ => status
        };
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
