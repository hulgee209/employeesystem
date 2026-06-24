using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmployeeSystem.Services;

public interface IResultInterpreterService
{
    /// <summary>
    /// Generates a Mongolian text fallback response from raw SQL result data when AI analysis fails.
    /// Detects common patterns like COUNT, SUM, AVG, TOP, etc. and generates appropriate text.
    /// </summary>
    Task<string> GenerateFallbackAnalysisAsync(
        string question,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows);
}
