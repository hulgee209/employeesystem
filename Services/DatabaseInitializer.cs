using EmployeeSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();

        if (configuration.GetValue<bool>("ApplyMigrations"))
        {
            if (context.Database.IsNpgsql())
            {
                await context.Database.EnsureCreatedAsync();
            }
            else
            {
                await context.Database.MigrateAsync();
            }
        }

        if (configuration.GetValue<bool>("SeedDefaultUsers"))
        {
            await SeedDefaultUsersAsync(context, configuration);
        }

        if (configuration.GetValue<bool>("SeedSampleData"))
        {
            await SeedSampleDataAsync(context);
        }
    }

    private static async Task SeedDefaultUsersAsync(EmployeeDbContext context, IConfiguration configuration)
    {
        var passwordHasher = new PasswordHasher<User>();
        var seedUsers = new[]
        {
            new SeedUser("admin", configuration["SeedUsers:AdminPassword"] ?? "Admin123!", "Admin"),
            new SeedUser("hr", configuration["SeedUsers:HrPassword"] ?? "Hr123!", "HR"),
            new SeedUser("manager", configuration["SeedUsers:ManagerPassword"] ?? "Manager123!", "Manager"),
            new SeedUser("employee", configuration["SeedUsers:EmployeePassword"] ?? "Employee123!", "Employee")
        };

        foreach (var seedUser in seedUsers)
        {
            var role = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == seedUser.RoleName);
            if (role == null)
            {
                role = new Role { RoleName = seedUser.RoleName };
                context.Roles.Add(role);
                await context.SaveChangesAsync();
            }

            var user = await context.Users
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
                user.PasswordHash = passwordHasher.HashPassword(user, seedUser.Password);
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }
            else if (!user.IsActive)
            {
                user.IsActive = true;
            }

            if (!user.UserRoles.Any(ur => ur.RoleId == role.RoleId))
            {
                user.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = role.RoleId
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedSampleDataAsync(EmployeeDbContext context)
    {
        if (await context.Employees.AnyAsync())
        {
            return;
        }

        var departments = new[]
        {
            "Хүний нөөц",
            "Санхүү",
            "Мэдээлэл технологи",
            "Борлуулалт",
            "Үйл ажиллагаа"
        };

        foreach (var departmentName in departments)
        {
            if (!await context.Departments.AnyAsync(d => d.DepartmentName == departmentName))
            {
                context.Departments.Add(new Department { DepartmentName = departmentName });
            }
        }

        var positions = new[]
        {
            "HR менежер",
            "Нягтлан бодогч",
            "Програм хөгжүүлэгч",
            "Системийн админ",
            "Борлуулалтын менежер",
            "Үйл ажиллагааны ажилтан",
            "Ерөнхий менежер"
        };

        foreach (var positionName in positions)
        {
            if (!await context.Positions.AnyAsync(p => p.PositionName == positionName))
            {
                context.Positions.Add(new Position { PositionName = positionName });
            }
        }

        await context.SaveChangesAsync();

        var departmentMap = await context.Departments.ToDictionaryAsync(d => d.DepartmentName, d => d.DepartmentId);
        var positionMap = await context.Positions.ToDictionaryAsync(p => p.PositionName, p => p.PositionId);

        var manager = new Employee
        {
            FirstName = "Бат",
            LastName = "Эрдэнэ",
            DepartmentId = departmentMap["Үйл ажиллагаа"],
            PositionId = positionMap["Ерөнхий менежер"],
            Phone = "99000001",
            Email = "bat.erdene@example.com",
            HireDate = new DateOnly(2021, 2, 15),
            IsActive = true
        };

        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var employees = new[]
        {
            new Employee { FirstName = "Саруул", LastName = "Наран", DepartmentId = departmentMap["Хүний нөөц"], PositionId = positionMap["HR менежер"], Phone = "99000002", Email = "saruul.naran@example.com", HireDate = new DateOnly(2022, 4, 8), ManagerId = manager.EmployeeId, IsActive = true },
            new Employee { FirstName = "Мөнх", LastName = "Тулга", DepartmentId = departmentMap["Санхүү"], PositionId = positionMap["Нягтлан бодогч"], Phone = "99000003", Email = "munkh.tulga@example.com", HireDate = new DateOnly(2023, 1, 12), ManagerId = manager.EmployeeId, IsActive = true },
            new Employee { FirstName = "Анужин", LastName = "Болд", DepartmentId = departmentMap["Мэдээлэл технологи"], PositionId = positionMap["Програм хөгжүүлэгч"], Phone = "99000004", Email = "anujin.bold@example.com", HireDate = new DateOnly(2023, 6, 1), ManagerId = manager.EmployeeId, IsActive = true },
            new Employee { FirstName = "Тэмүүлэн", LastName = "Очир", DepartmentId = departmentMap["Мэдээлэл технологи"], PositionId = positionMap["Системийн админ"], Phone = "99000005", Email = "temuulen.ochir@example.com", HireDate = new DateOnly(2022, 11, 21), ManagerId = manager.EmployeeId, IsActive = true },
            new Employee { FirstName = "Энхжин", LastName = "Ганбат", DepartmentId = departmentMap["Борлуулалт"], PositionId = positionMap["Борлуулалтын менежер"], Phone = "99000006", Email = "enkhjin.ganbat@example.com", HireDate = new DateOnly(2024, 3, 4), ManagerId = manager.EmployeeId, IsActive = true },
            new Employee { FirstName = "Номин", LastName = "Алтан", DepartmentId = departmentMap["Үйл ажиллагаа"], PositionId = positionMap["Үйл ажиллагааны ажилтан"], Phone = "99000007", Email = "nomin.altan@example.com", HireDate = new DateOnly(2024, 8, 19), ManagerId = manager.EmployeeId, IsActive = true }
        };

        context.Employees.AddRange(employees);
        await context.SaveChangesAsync();

        var allEmployees = await context.Employees.OrderBy(e => e.EmployeeId).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var statuses = new[] { "Present", "Present", "Present", "Late", "Absent" };

        foreach (var employee in allEmployees)
        {
            for (var dayOffset = 1; dayOffset <= 20; dayOffset++)
            {
                var date = today.AddDays(-dayOffset);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var status = statuses[(employee.EmployeeId + dayOffset) % statuses.Length];
                context.Attendance.Add(new Attendance
                {
                    EmployeeId = employee.EmployeeId,
                    AttendanceDate = date,
                    CheckInTime = status == "Absent" ? null : new TimeOnly(status == "Late" ? 9 : 8, status == "Late" ? 35 : 55),
                    CheckOutTime = status == "Absent" ? null : new TimeOnly(18, 0),
                    Status = status
                });
            }

            for (var monthOffset = 0; monthOffset < 3; monthOffset++)
            {
                var payrollMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-monthOffset);
                var baseSalary = 1_800_000m + employee.EmployeeId * 120_000m;
                var bonus = monthOffset == 0 ? 200_000m + employee.EmployeeId * 10_000m : 100_000m;
                var deductions = 120_000m + employee.EmployeeId * 5_000m;

                context.Payroll.Add(new Payroll
                {
                    EmployeeId = employee.EmployeeId,
                    PayrollMonth = payrollMonth,
                    BaseSalary = baseSalary,
                    Bonus = bonus,
                    Deductions = deductions,
                    NetSalary = baseSalary + bonus - deductions
                });
            }

            context.PerformanceReviews.Add(new PerformanceReview
            {
                EmployeeId = employee.EmployeeId,
                ReviewDate = today.AddDays(-(employee.EmployeeId * 5)),
                Score = 70 + employee.EmployeeId % 5 * 5,
                Comments = "Жишээ гүйцэтгэлийн үнэлгээ."
            });
        }

        await LinkSeedUsersToEmployeesAsync(context, manager.EmployeeId, allEmployees.FirstOrDefault(e => e.EmployeeId != manager.EmployeeId)?.EmployeeId);
        await context.SaveChangesAsync();
    }

    private static async Task LinkSeedUsersToEmployeesAsync(EmployeeDbContext context, int managerEmployeeId, int? employeeId)
    {
        var managerUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "manager");
        if (managerUser != null)
        {
            managerUser.EmployeeId = managerEmployeeId;
        }

        var employeeUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "employee");
        if (employeeUser != null && employeeId.HasValue)
        {
            employeeUser.EmployeeId = employeeId.Value;
        }
    }

    private sealed record SeedUser(string Username, string Password, string RoleName);
}
