using EmployeeSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Services;

public static class DatabaseInitializer
{
    private const int TargetSampleEmployeeCount = 5000;
    private const int BatchSize = 500;

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

        if (configuration.GetValue<bool>("SeedSampleData") ||
            configuration.GetValue<bool>("SeedDefaultUsers"))
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
        await EnsureLookupDataAsync(context);

        var departmentIds = await context.Departments
            .OrderBy(d => d.DepartmentId)
            .Select(d => d.DepartmentId)
            .ToArrayAsync();
        var positionIds = await context.Positions
            .OrderBy(p => p.PositionId)
            .Select(p => p.PositionId)
            .ToArrayAsync();

        var managerEmployeeId = await EnsureManagerEmployeeAsync(context, departmentIds, positionIds);
        await TopUpEmployeesAsync(context, managerEmployeeId, departmentIds, positionIds);

        var employeeIds = await context.Employees
            .OrderBy(e => e.EmployeeId)
            .Select(e => e.EmployeeId)
            .ToListAsync();

        await SeedRelatedDataForMissingEmployeesAsync(context, employeeIds);
        await LinkSeedUsersToEmployeesAsync(
            context,
            managerEmployeeId,
            employeeIds.FirstOrDefault(id => id != managerEmployeeId));
        await context.SaveChangesAsync();
    }

    private static async Task EnsureLookupDataAsync(EmployeeDbContext context)
    {
        var departments = new[] { "HR", "Finance", "IT", "Sales", "Operations" };
        foreach (var departmentName in departments)
        {
            if (!await context.Departments.AnyAsync(d => d.DepartmentName == departmentName))
            {
                context.Departments.Add(new Department { DepartmentName = departmentName });
            }
        }

        var positions = new[]
        {
            "HR Manager",
            "Accountant",
            "Software Developer",
            "System Administrator",
            "Sales Manager",
            "Operations Specialist",
            "General Manager"
        };

        foreach (var positionName in positions)
        {
            if (!await context.Positions.AnyAsync(p => p.PositionName == positionName))
            {
                context.Positions.Add(new Position { PositionName = positionName });
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task<int> EnsureManagerEmployeeAsync(
        EmployeeDbContext context,
        int[] departmentIds,
        int[] positionIds)
    {
        var managerEmployeeId = await context.Employees
            .Where(e => e.ManagerId == null)
            .OrderBy(e => e.EmployeeId)
            .Select(e => e.EmployeeId)
            .FirstOrDefaultAsync();

        if (managerEmployeeId != 0)
        {
            return managerEmployeeId;
        }

        var manager = new Employee
        {
            FirstName = "Manager",
            LastName = "User",
            DepartmentId = departmentIds[0],
            PositionId = positionIds[0],
            Phone = "99000001",
            Email = "manager@example.com",
            HireDate = new DateOnly(2021, 2, 15),
            IsActive = true
        };

        context.Employees.Add(manager);
        await context.SaveChangesAsync();
        return manager.EmployeeId;
    }

    private static async Task TopUpEmployeesAsync(
        EmployeeDbContext context,
        int managerEmployeeId,
        int[] departmentIds,
        int[] positionIds)
    {
        var existingCount = await context.Employees.CountAsync();
        if (existingCount >= TargetSampleEmployeeCount)
        {
            return;
        }

        var batch = new List<Employee>(BatchSize);
        for (var number = existingCount + 1; number <= TargetSampleEmployeeCount; number++)
        {
            batch.Add(new Employee
            {
                FirstName = $"Ajiltan{number:D4}",
                LastName = $"Test{number:D4}",
                DepartmentId = departmentIds[number % departmentIds.Length],
                PositionId = positionIds[number % positionIds.Length],
                Phone = $"99{number:D6}",
                Email = $"employee{number:D4}@example.com",
                HireDate = new DateOnly(2020 + number % 5, number % 12 + 1, number % 27 + 1),
                ManagerId = managerEmployeeId,
                IsActive = number % 20 != 0
            });

            if (batch.Count == BatchSize)
            {
                context.Employees.AddRange(batch);
                await context.SaveChangesAsync();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            context.Employees.AddRange(batch);
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedRelatedDataForMissingEmployeesAsync(
        EmployeeDbContext context,
        IReadOnlyList<int> employeeIds)
    {
        var attendanceEmployeeIds = (await context.Attendance
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        var payrollEmployeeIds = (await context.Payroll
            .Select(p => p.EmployeeId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        var performanceEmployeeIds = (await context.PerformanceReviews
            .Select(p => p.EmployeeId)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var statuses = new[] { "Present", "Present", "Present", "Late", "Absent" };
        var attendanceBatch = new List<Attendance>(1000);
        var payrollBatch = new List<Payroll>(1000);
        var performanceBatch = new List<PerformanceReview>(1000);

        foreach (var employeeId in employeeIds)
        {
            if (!attendanceEmployeeIds.Contains(employeeId))
            {
                for (var dayOffset = 1; dayOffset <= 10; dayOffset++)
                {
                    var date = today.AddDays(-dayOffset);
                    if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    {
                        continue;
                    }

                    var status = statuses[(employeeId + dayOffset) % statuses.Length];
                    attendanceBatch.Add(new Attendance
                    {
                        EmployeeId = employeeId,
                        AttendanceDate = date,
                        CheckInTime = status == "Absent" ? null : new TimeOnly(status == "Late" ? 9 : 8, status == "Late" ? 35 : 55),
                        CheckOutTime = status == "Absent" ? null : new TimeOnly(18, 0),
                        Status = status
                    });
                }
            }

            if (!payrollEmployeeIds.Contains(employeeId))
            {
                for (var monthOffset = 0; monthOffset < 2; monthOffset++)
                {
                    var payrollMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-monthOffset);
                    var baseSalary = 1_500_000m + employeeId % 30 * 80_000m;
                    var bonus = monthOffset == 0 ? 100_000m + employeeId % 20 * 10_000m : 50_000m;
                    var deductions = 100_000m + employeeId % 10 * 5_000m;

                    payrollBatch.Add(new Payroll
                    {
                        EmployeeId = employeeId,
                        PayrollMonth = payrollMonth,
                        BaseSalary = baseSalary,
                        Bonus = bonus,
                        Deductions = deductions,
                        NetSalary = baseSalary + bonus - deductions
                    });
                }
            }

            if (!performanceEmployeeIds.Contains(employeeId))
            {
                performanceBatch.Add(new PerformanceReview
                {
                    EmployeeId = employeeId,
                    ReviewDate = today.AddDays(-(employeeId % 60)),
                    Score = 60 + employeeId % 41,
                    Comments = "Sample performance review."
                });
            }

            if (attendanceBatch.Count >= 1000)
            {
                context.Attendance.AddRange(attendanceBatch);
                await context.SaveChangesAsync();
                attendanceBatch.Clear();
            }

            if (payrollBatch.Count >= 1000)
            {
                context.Payroll.AddRange(payrollBatch);
                await context.SaveChangesAsync();
                payrollBatch.Clear();
            }

            if (performanceBatch.Count >= 1000)
            {
                context.PerformanceReviews.AddRange(performanceBatch);
                await context.SaveChangesAsync();
                performanceBatch.Clear();
            }
        }

        context.Attendance.AddRange(attendanceBatch);
        context.Payroll.AddRange(payrollBatch);
        context.PerformanceReviews.AddRange(performanceBatch);
        await context.SaveChangesAsync();
    }

    private static async Task LinkSeedUsersToEmployeesAsync(
        EmployeeDbContext context,
        int managerEmployeeId,
        int? employeeId)
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
