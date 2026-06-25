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

    private sealed record SeedUser(string Username, string Password, string RoleName);
}
