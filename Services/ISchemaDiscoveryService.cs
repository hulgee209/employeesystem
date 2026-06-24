using System.Threading.Tasks;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public interface ISchemaDiscoveryService
{
    Task<DatabaseMetadata> DiscoverAsync();
}
