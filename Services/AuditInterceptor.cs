using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditInterceptor> _logger;

    public AuditInterceptor(IHttpContextAccessor httpContextAccessor, ILogger<AuditInterceptor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userId = 0;
        var userName = "Unknown";
        var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";

        // Get user ID from claims
        if (httpContext?.User is not null)
        {
            if (int.TryParse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid))
                userId = uid;
            userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        }

        var auditLogs = new List<AuditLog>();
        var dbContext = eventData.Context;

        if (dbContext == null) return await base.SavingChangesAsync(eventData, result, cancellationToken);

        // Get all changes
        var entries = dbContext.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            try
            {
                var entityName = entry.Entity.GetType().Name;
                var recordId = GetPrimaryKeyValue(entry);

                // Skip if we can't get the ID
                if (recordId <= 0) continue;

                var action = entry.State switch
                {
                    EntityState.Added => "CREATE",
                    EntityState.Modified => "UPDATE",
                    EntityState.Deleted => "DELETE",
                    _ => "UNKNOWN"
                };

                var oldValues = entry.State == EntityState.Modified
                    ? GetPropertyValues(entry, entry.OriginalValues)
                    : (entry.State == EntityState.Deleted ? GetPropertyValues(entry, entry.CurrentValues) : null);

                var newValues = entry.State switch
                {
                    EntityState.Added or EntityState.Modified => GetPropertyValues(entry, entry.CurrentValues),
                    _ => null
                };

                var auditLog = new AuditLog
                {
                    UserId = userId,
                    UserName = userName,
                    Action = action,
                    TableName = entityName,
                    RecordId = recordId,
                    OldValues = oldValues,
                    NewValues = newValues,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow
                };

                auditLogs.Add(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create audit log for {Entity}", entry.Entity.GetType().Name);
            }
        }

        // Add audit logs without triggering another interceptor call
        if (auditLogs.Count > 0)
        {
            // Temporarily disable change tracking for AuditLog adds
            var previousState = dbContext.ChangeTracker.AutoDetectChangesEnabled;
            try
            {
                dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
                dbContext.Set<AuditLog>().AddRange(auditLogs);
                _logger.LogInformation("Created {AuditLogCount} audit logs", auditLogs.Count);
            }
            finally
            {
                dbContext.ChangeTracker.AutoDetectChangesEnabled = previousState;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static int GetPrimaryKeyValue(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return 0;

        var keyProperty = key.Properties.First();
        var value = entry.CurrentValues[keyProperty];

        return value is int intValue ? intValue : 0;
    }

    private static string? GetPropertyValues(EntityEntry entry, PropertyValues values)
    {
        var dictionary = new Dictionary<string, object?>();

        foreach (var property in entry.Metadata.GetProperties())
        {
            // Skip primary keys and calculated properties
            if (entry.Metadata.FindPrimaryKey()?.Properties.Contains(property) == true)
                continue;

            var value = values[property];

            // Only include non-null values and actual changes
            if (value != null && value != DBNull.Value)
            {
                dictionary[property.Name] = value;
            }
        }

        return dictionary.Count > 0 ? JsonSerializer.Serialize(dictionary) : null;
    }
}
