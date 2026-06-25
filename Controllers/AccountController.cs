using System.Security.Claims;
using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Controllers
{
    public class AccountController : Controller
    {
        private const string RememberedUsernameCookie = "EmployeeSystem.RememberedUsername";
        private readonly EmployeeDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AccountController(
            EmployeeDbContext context,
            IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [AllowAnonymous]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var rememberedUsername = Request.Cookies[RememberedUsernameCookie];

            return View(new LoginViewModel
            {
                ReturnUrl = returnUrl,
                Username = rememberedUsername ?? string.Empty,
                RememberMe = !string.IsNullOrWhiteSpace(rememberedUsername)
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .Include(u => u.Employee)
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Username == model.Username);

            if (user == null || !IsPasswordValid(user, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Нэвтрэх нэр эсвэл нууц үг буруу байна.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Энэ хэрэглэгчийн эрх идэвхгүй байна.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username)
            };

            var resolvedEmployeeId = await ResolveEmployeeIdForClaimsAsync(user);

            if (resolvedEmployeeId.HasValue)
            {
                claims.Add(new Claim("EmployeeId", resolvedEmployeeId.Value.ToString()));
            }

            foreach (var role in user.UserRoles.Select(ur => ur.Role.RoleName))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe
                        ? DateTimeOffset.UtcNow.AddDays(30)
                        : DateTimeOffset.UtcNow.AddHours(8)
                });

            if (model.RememberMe)
            {
                Response.Cookies.Append(
                    RememberedUsernameCookie,
                    model.Username,
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(30),
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = Request.IsHttps
                    });
            }
            else
            {
                Response.Cookies.Delete(RememberedUsernameCookie);
            }

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) &&
                Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            Response.Cookies.Delete(RememberedUsernameCookie);
            Response.Cookies.Delete("EmployeeSystem.Auth");
            Response.Cookies.Delete(".AspNetCore.Cookies");
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> SeedAdmin()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            var seedUsers = new[]
            {
                new SeedUser("admin", "Admin123!", "Admin"),
                new SeedUser("hr", "Hr123!", "HR"),
                new SeedUser("manager", "Manager123!", "Manager"),
                new SeedUser("employee", "Employee123!", "Employee")
            };

            var messages = new List<string>();

            foreach (var seedUser in seedUsers)
            {
                var message = await EnsureSeedUserAsync(seedUser);
                messages.Add(message);
            }

            await _context.SaveChangesAsync();

            return Content(
                "Development users seeded successfully.\n" +
                string.Join("\n", messages) +
                "\n\nDefault credentials:\n" +
                "admin / Admin123! / Admin\n" +
                "hr / Hr123! / HR\n" +
                "manager / Manager123! / Manager\n" +
                "employee / Employee123! / Employee");
        }

        private bool IsPasswordValid(User user, string password)
        {
            try
            {
                var result = _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    password);

                return result != PasswordVerificationResult.Failed;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private async Task<int?> ResolveEmployeeIdForClaimsAsync(User user)
        {
            if (user.EmployeeId.HasValue)
            {
                return user.EmployeeId.Value;
            }

            var roleNames = user.UserRoles
                .Select(ur => ur.Role.RoleName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int? fallbackEmployeeId = null;

            if (roleNames.Contains("Manager") &&
                !roleNames.Contains("Admin") &&
                !roleNames.Contains("HR"))
            {
                fallbackEmployeeId = await _context.Employees
                    .Where(e => e.Position.PositionName.Contains("Manager"))
                    .OrderBy(e => e.EmployeeId)
                    .Select(e => (int?)e.EmployeeId)
                    .FirstOrDefaultAsync();
            }
            else if (roleNames.Contains("Employee") &&
                !roleNames.Contains("Admin") &&
                !roleNames.Contains("HR") &&
                !roleNames.Contains("Manager"))
            {
                fallbackEmployeeId = await _context.Employees
                    .OrderBy(e => e.EmployeeId)
                    .Select(e => (int?)e.EmployeeId)
                    .FirstOrDefaultAsync();
            }

            if (fallbackEmployeeId.HasValue)
            {
                user.EmployeeId = fallbackEmployeeId.Value;
                await _context.SaveChangesAsync();
            }

            return fallbackEmployeeId;
        }

        private bool HasValidPasswordHash(User user)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                return false;
            }

            try
            {
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    "hash-format-check");

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private async Task<string> EnsureSeedUserAsync(SeedUser seedUser)
        {
            var role = await _context.Roles
                .FirstOrDefaultAsync(r => r.RoleName == seedUser.RoleName);

            if (role == null)
            {
                role = new Role
                {
                    RoleName = seedUser.RoleName
                };

                _context.Roles.Add(role);
                await _context.SaveChangesAsync();
            }

            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Username == seedUser.Username);

            if (user == null)
            {
                user = new User
                {
                    Username = seedUser.Username,
                    IsActive = true,
                    PasswordHash = string.Empty
                };

                user.PasswordHash = _passwordHasher.HashPassword(user, seedUser.Password);

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                user.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = role.RoleId
                });

                return $"{seedUser.Username}: created and assigned {seedUser.RoleName}.";
            }

            var actions = new List<string>();

            if (!HasValidPasswordHash(user))
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, seedUser.Password);
                actions.Add("invalid/plain password hash was replaced");
            }

            if (!user.IsActive)
            {
                user.IsActive = true;
                actions.Add("activated");
            }

            if (!user.UserRoles.Any(ur => ur.RoleId == role.RoleId))
            {
                user.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = role.RoleId
                });

                actions.Add($"assigned {seedUser.RoleName}");
            }

            if (actions.Count == 0)
            {
                return $"{seedUser.Username}: already exists and is ready.";
            }

            return $"{seedUser.Username}: {string.Join(", ", actions)}.";
        }

        private record SeedUser(
            string Username,
            string Password,
            string RoleName);
    }
}
