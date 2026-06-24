using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using EmployeeSystem.Services;
using DevExtreme.AspNet.Mvc;
using System.Text.Json;
using System.Collections.Generic;

namespace EmployeeSystem.Controllers
{
    [Authorize(Roles = "Admin,HR,Manager,Employee")]
    public class EmployeesController : Controller
    {
        private readonly EmployeeDbContext _context;
        private readonly IEmployeeAnalyticsService _analyticsService;
        private readonly IAiSqlAgentService _aiSqlAgentService;

        public EmployeesController(
            EmployeeDbContext context,
            IEmployeeAnalyticsService analyticsService,
            IAiSqlAgentService aiSqlAgentService)
        {
            _context = context;
            _analyticsService = analyticsService;
            _aiSqlAgentService = aiSqlAgentService;
        }

        // ==========================
        // EMPLOYEE LIST + SEARCH
        // ==========================

        public async Task<IActionResult> Index(
            string search,
            int? departmentId,
            int page = 1,
            int pageSize = 25)
        {
            ViewBag.Departments =
                await _context.Departments.ToListAsync();

            if (page < 1) page = 1;
            if (pageSize < 10) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Search = search;
            ViewBag.DepartmentId = departmentId;

            return View(Array.Empty<EmployeeGridRow>());
        }

        [HttpGet]
        public async Task<IActionResult> GridData(
            int skip = 0,
            int take = 25,
            string? search = null,
            int? departmentId = null,
            int? positionId = null,
            bool? status = null,
            decimal? salaryMin = null,
            decimal? salaryMax = null,
            decimal? attendanceMin = null,
            decimal? attendanceMax = null,
            decimal? performanceMin = null,
            decimal? performanceMax = null,
            DateOnly? hireFrom = null,
            DateOnly? hireTo = null)
        {
            var pageSize = take > 0 ? take : 25;
            if (pageSize < 10) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var page = skip > 0
                ? (skip / pageSize) + 1
                : 1;

            var employees = await _analyticsService.GetEmployee360GridAsync(
                User,
                search,
                departmentId,
                positionId,
                status,
                salaryMin,
                salaryMax,
                attendanceMin,
                attendanceMax,
                performanceMin,
                performanceMax,
                hireFrom,
                hireTo,
                page,
                pageSize);

            var totalCount = await _analyticsService.GetEmployee360GridCountAsync(
                User,
                search,
                departmentId,
                positionId,
                status,
                salaryMin,
                salaryMax,
                attendanceMin,
                attendanceMax,
                performanceMin,
                performanceMax,
                hireFrom,
                hireTo);

            return new JsonResult(
                new Dictionary<string, object?>
                {
                    { "data", employees },
                    { "totalCount", totalCount }
                });
        }

        public async Task<IActionResult> Details(int id)
        {
            var employee =
                await _analyticsService.GetEmployee360DetailsAsync(User, id);

            if (employee == null)
            {
                return Forbid();
            }

            return View(employee);
        }

        [HttpGet]
        public async Task<IActionResult> Chart(int id, string type)
        {
            var data =
                await _analyticsService.GetEmployeeChartAsync(User, id, type);

            return Json(data);
        }

        [HttpPost]
        public async Task<IActionResult> AiAnalysis([FromBody] AiCopilotRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest(new AiCopilotResponse
                {
                    Answer = "Асуултаа бичээд дэхийн илгээнэ үү."
                });
            }

            var result =
                await _aiSqlAgentService.AnswerAsync(
                    User,
                    request.Question,
                    request.EmployeeId);

            return Ok(new AiCopilotResponse
            {
                Answer = result.Analysis,
                Sql = result.Sql
            });
        }
        
        // ==========================
        // INLINE UPDATE
        // ==========================

        [HttpPost]
        public async Task<IActionResult> UpdateInline(
            int employeeId,
            string firstName,
            string lastName,
            string? phone,
            string? email)
        {
            var employee = await _context.Employees
                .FindAsync(employeeId);

            if (employee == null)
                return NotFound();

            employee.FirstName = firstName;
            employee.LastName = lastName;
            employee.Phone = phone;
            employee.Email = email;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateInlineField([FromBody] InlineFieldUpdateRequest request)
        {
            if (request == null ||
                request.EmployeeId <= 0 ||
                string.IsNullOrWhiteSpace(request.Field))
            {
                return BadRequest(new { message = "Invalid update request." });
            }

            var employee = await _context.Employees.FindAsync(request.EmployeeId);
            if (employee == null) return NotFound();

            if (!CanEditEmployeeField(employee, request.Field))
            {
                return Forbid();
            }

            var value = request.Value?.Trim() ?? string.Empty;

            switch (request.Field)
            {
                case "FirstName":
                    employee.FirstName = value;
                    break;
                case "LastName":
                    employee.LastName = value;
                    break;
                case "Phone":
                    employee.Phone = value;
                    break;
                case "Email":
                    employee.Email = value;
                    break;
                case "DepartmentId":
                    if (!int.TryParse(value, out var departmentId))
                        return BadRequest(new { message = "Invalid department." });
                    employee.DepartmentId = departmentId;
                    break;
                case "PositionId":
                    if (!int.TryParse(value, out var positionId))
                        return BadRequest(new { message = "Invalid position." });
                    employee.PositionId = positionId;
                    break;
                case "ManagerId":
                    employee.ManagerId = int.TryParse(value, out var managerId) && managerId > 0
                        ? managerId
                        : null;
                    break;
                case "IsActive":
                    if (!bool.TryParse(value, out var isActive))
                        return BadRequest(new { message = "Invalid status." });
                    employee.IsActive = isActive;
                    break;
                default:
                    return BadRequest(new { message = "Field cannot be updated inline." });
            }

            await _context.SaveChangesAsync();
            return Ok(new { request.EmployeeId, request.Field, Value = value });
        }

        // ==========================
        // CREATE
        // ==========================

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Create()
        {
            ViewBag.Departments = _context.Departments.ToList();
            ViewBag.Positions = _context.Positions.ToList();

            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Create(Employee employee)
        {
            ModelState.Remove(nameof(Employee.Department));
            ModelState.Remove(nameof(Employee.Position));

            if (ModelState.IsValid)
            {
                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            ViewBag.Departments = _context.Departments.ToList();
            ViewBag.Positions = _context.Positions.ToList();

            return View(employee);
        }

        // ==========================
        // DELETE
        // ==========================

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Delete(int id)
        {
            var employee =
                await _context.Employees.FindAsync(id);

            if (employee != null)
            {
                _context.Employees.Remove(employee);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> DeleteInline([FromBody] DeleteEmployeeRequest request)
        {
            if (request == null || request.EmployeeId <= 0)
            {
                return BadRequest(new { message = "Устгах ажилтан олдсонгүй." });
            }

            var employee = await _context.Employees.FindAsync(request.EmployeeId);
            if (employee == null)
            {
                return NotFound(new { message = "Ажилтан олдсонгүй." });
            }

            try
            {
                _context.Employees.Remove(employee);
                await _context.SaveChangesAsync();
                return Ok(new { deleted = request.EmployeeId });
            }
            catch (DbUpdateException)
            {
                return Conflict(new
                {
                    message = "Энэ ажилтантай холбоотой бүртгэлүүд байгаа тул устгаж чадсангүй. Эхлээд холбоотой payroll, ирц, төсөл, хөрөнгийн бүртгэлийг шалгана уу."
                });
            }
        }

        // ==========================
        // QUICK VIEW DATA
        // ==========================

        [HttpGet]
        public async Task<IActionResult> QuickViewData(int id)
        {
            var employee = await _analyticsService.GetEmployee360DetailsAsync(User, id);
            if (employee == null) return NotFound();

            return PartialView("_QuickView", employee);
        }

        // ==========================
        // BULK OPERATIONS
        // ==========================

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkUpdateRequest request)
        {
            if (request?.EmployeeIds == null || !request.EmployeeIds.Any())
                return BadRequest();

            foreach (var id in request.EmployeeIds)
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee != null)
                {
                    employee.IsActive = request.IsActive;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { updated = request.EmployeeIds.Count });
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> BulkAssignDepartment([FromBody] BulkAssignRequest request)
        {
            if (request?.EmployeeIds == null || !request.EmployeeIds.Any())
                return BadRequest();

            foreach (var id in request.EmployeeIds)
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee != null && request.DepartmentId > 0)
                {
                    employee.DepartmentId = request.DepartmentId;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { updated = request.EmployeeIds.Count });
        }

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> BulkAssignManager([FromBody] BulkAssignRequest request)
    {
        if (request?.EmployeeIds == null || !request.EmployeeIds.Any())
            return BadRequest();

        foreach (var id in request.EmployeeIds)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee != null && request.ManagerId > 0)
            {
                employee.ManagerId = request.ManagerId;
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { updated = request.EmployeeIds.Count });
    }

    [HttpGet]
    public async Task<IActionResult> GetFilterData()
    {
        var departments = await _context.Departments
            .OrderBy(d => d.DepartmentName)
            .Select(d => new { d.DepartmentId, d.DepartmentName })
            .ToListAsync();

        var positions = await _context.Positions
            .OrderBy(p => p.PositionName)
            .Select(p => new { p.PositionId, p.PositionName })
            .ToListAsync();

        return Json(new { departments, positions });
    }

    [HttpGet]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> EmployeeLookup(string? search = null, int take = 50)
    {
        if (take < 10) take = 10;
        if (take > 100) take = 100;

        var query = _context.Employees.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.Trim().ToLower();
            query = query.Where(e =>
                e.EmployeeId.ToString().Contains(lowered) ||
                e.FirstName.ToLower().Contains(lowered) ||
                e.LastName.ToLower().Contains(lowered) ||
                (e.Email != null && e.Email.ToLower().Contains(lowered)) ||
                (e.Phone != null && e.Phone.ToLower().Contains(lowered)));
        }

        var employees = await query
            .OrderBy(e => e.EmployeeId)
            .Take(take)
            .Select(e => new
            {
                e.EmployeeId,
                FullName = e.FirstName + " " + e.LastName
            })
            .ToListAsync();

        return Json(employees);
    }

    [HttpGet]
    public async Task<IActionResult> DrawerData(int id)
    {
        var employee = await _analyticsService.GetEmployee360DetailsAsync(User, id);
        if (employee == null) return NotFound();

        return PartialView("_EmployeeDrawer", employee);
    }

    [HttpGet]
    public async Task<IActionResult> RowDetailData(int id)
    {
        var employee = await _analyticsService.GetEmployee360DetailsAsync(User, id);
        if (employee == null) return NotFound();

        return PartialView("_RowDetail", employee);
    }

    private bool CanEditEmployeeField(Employee employee, string field)
    {
        if (User.IsInRole("Admin") || User.IsInRole("HR"))
        {
            return true;
        }

        if (User.IsInRole("Manager") &&
            int.TryParse(User.FindFirst("EmployeeId")?.Value, out var managerEmployeeId))
        {
            var managerEditableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Phone",
                "Email",
                "PositionId",
                "ManagerId"
            };

            return employee.ManagerId == managerEmployeeId &&
                managerEditableFields.Contains(field);
        }

        return false;
    }
}
}

// Request models
public class InlineFieldUpdateRequest
{
    public int EmployeeId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class BulkUpdateRequest
{
    public List<int> EmployeeIds { get; set; } = new();
    public bool IsActive { get; set; }
}

public class BulkAssignRequest
{
    public List<int> EmployeeIds { get; set; } = new();
    public int DepartmentId { get; set; }
    public int ManagerId { get; set; }
}

public class DeleteEmployeeRequest
{
    public int EmployeeId { get; set; }
}
