using System;
using System.Threading.Tasks;
using EmployeeSystem.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EmployeeSystem.Services;

public class SchemaCacheService : ISchemaCacheService
{
    private readonly ISchemaDiscoveryService _discovery;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _options;

    private const string CacheKey = "__database_schema__";

    public SchemaCacheService(ISchemaDiscoveryService discovery, IMemoryCache cache)
    {
        _discovery = discovery;
        _cache = cache;
        _options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        };
    }

    public Task<DatabaseMetadata> GetSchemaAsync()
    {
        return _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.SetOptions(_options);
            return await _discovery.DiscoverAsync();
        })!;
    }

    public Task InvalidateAsync()
    {
        _cache.Remove(CacheKey);
        return Task.CompletedTask;
    }
}
