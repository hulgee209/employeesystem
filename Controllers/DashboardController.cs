using EmployeeSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "Admin,HR,Manager,Employee")]
public class DashboardController : Controller
{
    private readonly IEmployeeAnalyticsService _analyticsService;
    private readonly IDashboardService _dashboardService;

    public DashboardController(IEmployeeAnalyticsService analyticsService, IDashboardService dashboardService)
    {
        _analyticsService = analyticsService;
        _dashboardService = dashboardService;
    }

    public async Task<IActionResult> Index()
    {
        // Determine which dashboard to show based on role
        if (User.IsInRole("HR") || User.IsInRole("Admin"))
        {
            var hrDashboard = await _dashboardService.GetHrDashboardAsync();
            return View("HrDashboard", hrDashboard);
        }

        if (User.IsInRole("Manager"))
        {
            var managerId = GetCurrentEmployeeId();
            var managerDashboard = await _dashboardService.GetManagerDashboardAsync(managerId);
            return View("ManagerDashboard", managerDashboard);
        }

        // Employee dashboard
        var employeeId = GetCurrentEmployeeId();
        var employeeDashboard = await _dashboardService.GetEmployeeDashboardAsync(employeeId);
        return View("EmployeeDashboard", employeeDashboard);
    }

    private int GetCurrentEmployeeId()
    {
        var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
        return int.TryParse(employeeIdStr, out var employeeId) ? employeeId : 0;
    }

    private int GetCurrentUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdStr, out var userId) ? userId : 0;
    }
}
