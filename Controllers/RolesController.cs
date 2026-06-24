using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RolesController : Controller
    {
        private readonly EmployeeDbContext _context;

        public RolesController(EmployeeDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var roles =
                await _context.Roles
                    .AsNoTracking()
                    .OrderBy(r => r.RoleName)
                    .Select(r => new RoleListItemViewModel
                    {
                        RoleId = r.RoleId,
                        RoleName = r.RoleName,
                        UserCount = r.UserRoles.Count
                    })
                    .ToListAsync();

            return View(roles);
        }
    }
}
