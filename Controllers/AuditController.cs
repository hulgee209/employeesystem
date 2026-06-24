using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EmployeeSystem.Models;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "HR,Admin")]
public class AuditController : Controller
{
    private readonly EmployeeDbContext _context;

    public AuditController(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(
        [FromQuery] string? tableName = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _context.AuditLogs.AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(tableName))
            query = query.Where(a => a.TableName == tableName);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (startDate.HasValue)
            query = query.Where(a => a.CreatedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.CreatedAt <= endDate.Value.AddDays(1));

        // Total count for pagination
        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Get paginated results
        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.TotalPages = totalPages;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TableName = tableName;
        ViewBag.Action = action;
        ViewBag.StartDate = startDate;
        ViewBag.EndDate = endDate;

        // Get unique table names and actions for filter dropdowns (from ALL logs, not filtered)
        var allLogs = _context.AuditLogs.AsQueryable();
        ViewBag.TableNames = await allLogs
            .Select(a => a.TableName)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        ViewBag.Actions = await allLogs
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();

        return View(logs);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var log = await _context.AuditLogs.FindAsync(id);
        if (log == null)
            return NotFound();

        return View(log);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOlderThan(int daysOld)
    {
        if (daysOld < 7)
            return BadRequest("Cannot delete logs newer than 7 days old");

        var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
        var logsToDelete = await _context.AuditLogs
            .Where(a => a.CreatedAt < cutoffDate)
            .ToListAsync();

        _context.AuditLogs.RemoveRange(logsToDelete);
        await _context.SaveChangesAsync();

        TempData["Message"] = $"Deleted {logsToDelete.Count} audit logs older than {daysOld} days.";
        return RedirectToAction(nameof(Index));
    }
}
