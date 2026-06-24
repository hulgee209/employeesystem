using System.Data;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public interface IAiSqlAgentService
{
    Task<AiSqlAgentResult> AnswerAsync(ClaimsPrincipal user, string question, int? employeeId = null);
}

/// <summary>
/// Pure AI-driven SQL generation: ZERO hardcoded rules, ZERO keyword validation.
/// Schema + question → AI thinks → SQL → result.
/// </summary>
public class AiSqlAgentService : IAiSqlAgentService
{
    private static readonly string[] BlockedSqlTokens =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "EXEC",
        "EXECUTE", "MERGE", "CREATE", "GRANT", "REVOKE", "BACKUP", "RESTORE"
    ];

    private readonly EmployeeDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiSqlAgentService> _logger;
    private readonly IMemoryCache _cache;

    public sealed class AiSqlAgentQuotaException : Exception
    {
        public AiSqlAgentQuotaException(string message) : base(message) { }
    }

    public AiSqlAgentService(
        EmployeeDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<AiSqlAgentService> logger,
        IMemoryCache cache)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Main entry point: question → AI backend → SQL → execute → result.
    /// </summary>
    public async Task<AiSqlAgentResult> AnswerAsync(ClaimsPrincipal user, string question, int? employeeId = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new AiSqlAgentResult { Analysis = "Асуултаа бичээд дахин илгээнэ үү." };
        }

        try
        {
            // Let AI think about the question and generate SQL
            var sql = await GenerateSqlFromAiAsync(question);

            // Validate SQL safety
            ValidateSelectOnly(sql);

            // Execute the SQL
            var execution = await ExecuteSqlAsync(sql, new Dictionary<string, object?>());

            if (execution.Rows.Count == 0)
            {
                return new AiSqlAgentResult
                {
                    Analysis = "Өгөгдөл олдсонгүй.",
                    Sql = sql
                };
            }

            // Get 1-2 sentence analysis in Mongolian
            var analysis = await GenerateAnalysisFromAiAsync(question, execution);

            return new AiSqlAgentResult
            {
                Sql = sql,
                Columns = execution.Columns,
                Rows = execution.Rows.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
                Analysis = analysis
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP request failed while contacting AI backend.");
            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээтэй холбогдож чадсангүй. FastAPI/Gemini серверийн холболтыг шалгаа."
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout occurred contacting AI backend.");
            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээ хариу өгөхөд удаан байна. Дахин оролдоно уу."
            };
        }
        catch (AiSqlAgentQuotaException ex)
        {
            _logger.LogWarning(ex, "AI provider quota or service unavailable.");
            return new AiSqlAgentResult
            {
                Analysis = "Таны асуултыг одоогоор боловсруулах боломжгүй. AI үйлчилгээ түр боломжгүй. Дахин оролдоно уу."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AnswerAsync.");
            return new AiSqlAgentResult
            {
                Analysis = "Системд алдаа гарлаа. Админ руу мэдэгдэнэ үү."
            };
        }
    }

    /// <summary>
    /// Call Python backend to generate SQL from question.
    /// </summary>
    private async Task<string> GenerateSqlFromAiAsync(string question)
    {
        const int maxAttempts = 3;
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(120);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await client.PostAsJsonAsync(
                    "http://127.0.0.1:8000/sql-agent",
                    new { question });

                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<SqlResponse>();
                    if (payload?.Sql != null)
                    {
                        var sql = NormalizeSql(payload.Sql);
                        _logger.LogInformation("AI generated SQL: {Sql}", sql.Substring(0, Math.Min(100, sql.Length)));
                        return sql;
                    }
                }

                var error = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                    response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                    response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                    error.Contains("quota", StringComparison.OrdinalIgnoreCase))
                {
                    if (attempt == maxAttempts)
                    {
                        throw new AiSqlAgentQuotaException("AI service quota exceeded or unavailable.");
                    }

                    var backoff = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                    await Task.Delay(backoff);
                    continue;
                }

                throw new InvalidOperationException($"AI backend error: {error}");
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "SQL generation attempt {Attempt} failed, retrying...", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        throw new AiSqlAgentQuotaException("Failed to generate SQL after all attempts.");
    }

    /// <summary>
    /// Call Python backend to generate 1-2 sentence Mongolian analysis.
    /// </summary>
    private async Task<string> GenerateAnalysisFromAiAsync(string question, SqlExecutionResult execution)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        try
        {
            var resultJson = JsonSerializer.Serialize(execution.Rows.Take(100));
            var response = await client.PostAsJsonAsync(
                "http://127.0.0.1:8000/sql-analysis",
                new { question, results = resultJson });

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
                if (payload?.Analysis != null)
                {
                    _logger.LogInformation("AI analysis: {Analysis}", payload.Analysis.Substring(0, Math.Min(80, payload.Analysis.Length)));
                    return payload.Analysis;
                }
            }

            _logger.LogWarning("Analysis generation failed, returning fallback.");
            return "Үр дүн амжилттай боловсруулагдсан.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analysis generation error, using fallback.");
            return "Үр дүн амжилттай боловсруулагдсан.";
        }
    }

    /// <summary>
    /// Execute SQL query and return results.
    /// </summary>
    private async Task<SqlExecutionResult> ExecuteSqlAsync(string sql, Dictionary<string, object?> sqlParameters)
    {
        var result = new SqlExecutionResult
        {
            Columns = new List<string>(),
            Rows = new List<Dictionary<string, object?>>()
        };

        try
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandTimeout = 60;

                foreach (var kvp in sqlParameters)
                {
                    var param = command.CreateParameter();
                    param.ParameterName = kvp.Key;
                    param.Value = kvp.Value ?? DBNull.Value;
                    command.Parameters.Add(param);
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    // Read column names
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        result.Columns.Add(reader.GetName(i));
                    }

                    // Read rows (limit to 500 for safety)
                    int rowCount = 0;
                    while (await reader.ReadAsync() && rowCount < 500)
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[result.Columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        result.Rows.Add(row);
                        rowCount++;
                    }
                }
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL execution failed.");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Ensure SQL is SELECT-only (no DML/DDL).
    /// </summary>
    private static void ValidateSelectOnly(string sql)
    {
        var normalized = sql.Trim().ToUpperInvariant();

        if (!normalized.StartsWith("SELECT"))
        {
            throw new InvalidOperationException("SQL must be a SELECT query.");
        }

        foreach (var token in BlockedSqlTokens)
        {
            if (normalized.Contains(token))
            {
                throw new InvalidOperationException($"Blocked SQL token: {token}");
            }
        }
    }

    private static string NormalizeSql(string sql)
    {
        return sql
            .Replace("```sql", "")
            .Replace("```", "")
            .Trim()
            .TrimEnd(';');
    }
}

// Pydantic model from Python backend
public class SqlResponse
{
    public string Sql { get; set; } = string.Empty;
}

public class AnalysisResponse
{
    public string Analysis { get; set; } = string.Empty;
}
