using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly EmployeeDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public UsersController(EmployeeDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var users =
                await _context.Users
                    .AsNoTracking()
                    .Include(u => u.Employee)
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .OrderBy(u => u.Username)
                    .Select(u => new UserListItemViewModel
                    {
                        UserId = u.UserId,
                        Username = u.Username,
                        EmployeeName = u.Employee == null
                            ? "-"
                            : u.Employee.FirstName + " " + u.Employee.LastName,
                        IsActive = u.IsActive,
                        Roles = string.Join(", ", u.UserRoles.Select(ur => ur.Role.RoleName))
                    })
                    .ToListAsync();

            return View(users);
        }

        public async Task<IActionResult> Create()
        {
            var model =
                await BuildUserEditViewModelAsync(new UserEditViewModel());

            return View("Edit", model);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var user =
                await _context.Users
                    .Include(u => u.UserRoles)
                    .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            var model =
                new UserEditViewModel
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    EmployeeId = user.EmployeeId,
                    IsActive = user.IsActive,
                    SelectedRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList()
                };

            return View(await BuildUserEditViewModelAsync(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(UserEditViewModel model)
        {
            if (!model.UserId.HasValue && string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "Шинэ хэрэглэгчид нууц үг шаардлагатай.");
            }

            if (await _context.Users.AnyAsync(u =>
                    u.Username == model.Username &&
                    u.UserId != (model.UserId ?? 0)))
            {
                ModelState.AddModelError(nameof(model.Username), "Энэ нэвтрэх нэр бүртгэлтэй байна.");
            }

            if (model.EmployeeId.HasValue &&
                await _context.Users.AnyAsync(u =>
                    u.EmployeeId == model.EmployeeId &&
                    u.UserId != (model.UserId ?? 0)))
            {
                ModelState.AddModelError(nameof(model.EmployeeId), "Энэ ажилтанд хэрэглэгчийн эрх аль хэдийн үүссэн байна.");
            }

            if (!ModelState.IsValid)
            {
                return View("Edit", await BuildUserEditViewModelAsync(model));
            }

            User user;

            if (model.UserId.HasValue)
            {
                user =
                    await _context.Users
                        .Include(u => u.UserRoles)
                        .FirstAsync(u => u.UserId == model.UserId.Value);
            }
            else
            {
                user =
                    new User();

                _context.Users.Add(user);
            }

            user.Username = model.Username.Trim();
            user.EmployeeId = model.EmployeeId;
            user.IsActive = model.IsActive;

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                user.PasswordHash =
                    _passwordHasher.HashPassword(user, model.Password);
            }

            user.UserRoles.Clear();
            foreach (var roleId in model.SelectedRoleIds.Distinct())
            {
                user.UserRoles.Add(new UserRole
                {
                    RoleId = roleId
                });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Disable(int id)
        {
            var user =
                await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = false;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<UserEditViewModel> BuildUserEditViewModelAsync(UserEditViewModel model)
        {
            model.Employees =
                await _context.Employees
                    .AsNoTracking()
                    .OrderBy(e => e.FirstName)
                    .ThenBy(e => e.LastName)
                    .Select(e => new SelectListItem
                    {
                        Value = e.EmployeeId.ToString(),
                        Text = e.FirstName + " " + e.LastName,
                        Selected = model.EmployeeId == e.EmployeeId
                    })
                    .ToListAsync();

            model.Roles =
                await _context.Roles
                    .AsNoTracking()
                    .OrderBy(r => r.RoleName)
                    .Select(r => new SelectListItem
                    {
                        Value = r.RoleId.ToString(),
                        Text = r.RoleName,
                        Selected = model.SelectedRoleIds.Contains(r.RoleId)
                    })
                    .ToListAsync();

            return model;
        }
    }
}
