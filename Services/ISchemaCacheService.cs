using System.Threading.Tasks;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public interface ISchemaCacheService
{
    Task<DatabaseMetadata> GetSchemaAsync();
    Task InvalidateAsync();
}
