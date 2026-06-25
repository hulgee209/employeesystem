using System.Data;
using System.Net.Http.Headers;
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
    private readonly IConfiguration _configuration;

    public sealed class AiSqlAgentQuotaException : Exception
    {
        public AiSqlAgentQuotaException(string message) : base(message) { }
    }

    public AiSqlAgentService(
        EmployeeDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<AiSqlAgentService> logger,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<AiSqlAgentResult> AnswerAsync(ClaimsPrincipal user, string question, int? employeeId = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new AiSqlAgentResult { Analysis = "Асуултаа бичээд дахин илгээнэ үү." };
        }

        try
        {
            var directAnswer = await TryAnswerDirectlyAsync(question);
            if (directAnswer != null)
            {
                return directAnswer;
            }

            var sql = await GenerateSqlFromAiAsync(question);
            ValidateSelectOnly(sql);

            var execution = await ExecuteSqlAsync(sql, new Dictionary<string, object?>());
            if (execution.Rows.Count == 0)
            {
                return new AiSqlAgentResult
                {
                    Analysis = "Өгөгдөл олдсонгүй.",
                    Sql = sql
                };
            }

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
            _logger.LogWarning(ex, "HTTP request failed while contacting AI provider.");
            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээтэй холбогдож чадсангүй. Render Environment дээр AI API key тохирсон эсэхийг шалгана уу."
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout occurred contacting AI provider.");
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
                Analysis = "AI үйлчилгээ түр боломжгүй байна. API quota эсвэл model тохиргоог шалгаад дахин оролдоно уу."
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

    private async Task<AiSqlAgentResult?> TryAnswerDirectlyAsync(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();

        if ((normalized.Contains("нийт") && normalized.Contains("ажилтан")) ||
            normalized.Contains("heden ajiltan") ||
            normalized.Contains("how many employee") ||
            normalized.Contains("total employee"))
        {
            var count = await _context.Employees.AsNoTracking().CountAsync();
            return new AiSqlAgentResult
            {
                Analysis = $"Нийт ажилтны тоо {count:N0} байна."
            };
        }

        if (normalized.Contains("дугаар") ||
            normalized.Contains("утас") ||
            normalized.Contains("dugaar") ||
            normalized.Contains("dugaartai") ||
            normalized.Contains("utas") ||
            normalized.Contains("phone"))
        {
            var search = ExtractEmployeeSearchText(normalized);
            if (string.IsNullOrWhiteSpace(search))
            {
                return null;
            }

            var tokens = search
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length >= 2)
                .Take(4)
                .ToArray();

            if (tokens.Length == 0)
            {
                return null;
            }

            var allEmployees = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.EmployeeId)
                .Select(e => new
                {
                    e.FirstName,
                    e.LastName,
                    e.Phone,
                    e.Email
                })
                .ToListAsync();
            var employees = allEmployees
                .Where(e => tokens.Any(token =>
                    e.FirstName.ToLowerInvariant().Contains(token) ||
                    e.LastName.ToLowerInvariant().Contains(token) ||
                    (e.Email != null && e.Email.ToLowerInvariant().Contains(token)) ||
                    (e.Phone != null && e.Phone.ToLowerInvariant().Contains(token))))
                .Take(5)
                .ToList();

            if (employees.Count == 0)
            {
                return new AiSqlAgentResult
                {
                    Analysis = "Тийм нэртэй ажилтан олдсонгүй."
                };
            }

            var lines = employees.Select(e =>
            {
                var phone = string.IsNullOrWhiteSpace(e.Phone) ? "утас бүртгэлгүй" : e.Phone;
                return $"{e.FirstName} {e.LastName}: {phone}";
            });

            return new AiSqlAgentResult
            {
                Analysis = string.Join("\n", lines)
            };
        }

        return null;
    }

    private static string ExtractEmployeeSearchText(string normalizedQuestion)
    {
        var stopWords = new[]
        {
            "ямар", "дугаартай", "дугаар", "утас", "байна", "вэ", "yu", "yamar",
            "dugaartai", "dugaar", "utas", "baina", "be", "ve", "phone", "number",
            "what", "is", "the", "of", "hi", "sain", "uu"
        };

        var search = normalizedQuestion;
        foreach (var word in stopWords)
        {
            search = search.Replace(word, " ", StringComparison.OrdinalIgnoreCase);
        }

        return string.Join(' ', search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private async Task<string> GenerateSqlFromAiAsync(string question)
    {
        var systemPrompt = """
            You are an HR analytics SQL assistant.
            Generate PostgreSQL SELECT queries only. Return only SQL, no markdown and no explanation.
            Use quoted identifiers exactly as shown because table and column names are PascalCase.
            Do not use SQL Server syntax. Use LIMIT instead of TOP. Use COALESCE instead of ISNULL.
            Never modify data.

            Available tables:
            "Employees"("EmployeeId","FirstName","LastName","DepartmentId","PositionId","Phone","Email","HireDate","IsActive","ManagerId")
            "Departments"("DepartmentId","DepartmentName")
            "Positions"("PositionId","PositionName")
            "Attendance"("AttendanceId","EmployeeId","AttendanceDate","CheckInTime","CheckOutTime","Status")
            "Payroll"("PayrollId","EmployeeId","PayMonth","Salary","Bonus","Deduction","NetSalary")
            "PerformanceReviews"("ReviewId","EmployeeId","ReviewDate","Score","Comments")
            "Users"("UserId","Username","PasswordHash","EmployeeId","IsActive")
            "Roles"("RoleId","RoleName")
            "UserRoles"("UserRoleId","UserId","RoleId")

            Useful joins:
            "Employees"."DepartmentId" = "Departments"."DepartmentId"
            "Employees"."PositionId" = "Positions"."PositionId"
            "Attendance"."EmployeeId" = "Employees"."EmployeeId"
            "Payroll"."EmployeeId" = "Employees"."EmployeeId"
            "PerformanceReviews"."EmployeeId" = "Employees"."EmployeeId"

            For broad lists, add LIMIT 100.
            """;

        return NormalizeSql(await GenerateChatTextAsync(systemPrompt, question, maxTokens: 700));
    }

    private async Task<string> GenerateAnalysisFromAiAsync(string question, SqlExecutionResult execution)
    {
        try
        {
            var resultJson = JsonSerializer.Serialize(execution.Rows.Take(100));
            var systemPrompt = """
                You are an HR analytics assistant.
                Explain the SQL result in Mongolian in 1-3 concise sentences.
                Do not mention raw SQL unless the user asks.
                """;
            var userPrompt = $"Question: {question}\nResult JSON: {resultJson}";
            return await GenerateChatTextAsync(systemPrompt, userPrompt, maxTokens: 500);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analysis generation error, using fallback.");
            return "Үр дүн амжилттай боловсруулагдсан.";
        }
    }

    private async Task<string> GenerateChatTextAsync(string systemPrompt, string userPrompt, int maxTokens)
    {
        var providers = BuildProviders();
        if (providers.Count == 0)
        {
            throw new HttpRequestException("No AI provider API key is configured.");
        }

        foreach (var provider in providers)
        {
            try
            {
                var text = provider.Kind == AiProviderKind.Gemini
                    ? await SendGeminiRequestAsync(provider, systemPrompt, userPrompt, maxTokens)
                    : await SendOpenAiCompatibleRequestAsync(provider, systemPrompt, userPrompt, maxTokens);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (AiSqlAgentQuotaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI provider {Provider} failed, trying next provider.", provider.Name);
            }
        }

        throw new AiSqlAgentQuotaException("All configured AI providers failed.");
    }

    private async Task<string?> SendOpenAiCompatibleRequestAsync(
        AiProvider provider,
        string systemPrompt,
        string userPrompt,
        int maxTokens)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);

        using var request = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        if (provider.Name.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://employeesystem-xba0.onrender.com");
            request.Headers.TryAddWithoutValidation("X-Title", "EmployeeSystem");
        }

        request.Content = JsonContent.Create(new
        {
            model = provider.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = maxTokens
        });

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
        {
            throw new AiSqlAgentQuotaException($"{provider.Name} unavailable: {response.StatusCode}");
        }

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private async Task<string?> SendGeminiRequestAsync(
        AiProvider provider,
        string systemPrompt,
        string userPrompt,
        int maxTokens)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);

        var endpoint = $"{provider.Endpoint.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(provider.Model)}:generateContent?key={Uri.EscapeDataString(provider.ApiKey)}";
        using var response = await client.PostAsJsonAsync(endpoint, new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = maxTokens
            }
        });

        var body = await response.Content.ReadAsStringAsync();

        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
        {
            throw new AiSqlAgentQuotaException($"{provider.Name} unavailable: {response.StatusCode}");
        }

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        return parts.EnumerateArray()
            .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private List<AiProvider> BuildProviders()
    {
        var providers = new List<AiProvider>();

        var geminiKey = _configuration["GEMINI_API_KEY"] ??
            _configuration["GEMINI_API_KEY_1"] ??
            _configuration["GEMINI_API_KEY_2"] ??
            _configuration["GEMINI_API_KEY_3"];
        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            var configuredGeminiModel = _configuration["GEMINI_MODEL"] ?? "gemini-2.5-flash";
            providers.Add(new AiProvider(
                "Gemini",
                AiProviderKind.Gemini,
                "https://generativelanguage.googleapis.com",
                geminiKey,
                configuredGeminiModel));

            if (!string.Equals(configuredGeminiModel, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new AiProvider(
                    "Gemini fallback",
                    AiProviderKind.Gemini,
                    "https://generativelanguage.googleapis.com",
                    geminiKey,
                    "gemini-2.5-flash"));
            }
        }

        var groqKey = _configuration["GROQ_API_KEY"];
        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            providers.Add(new AiProvider(
                "Groq",
                AiProviderKind.OpenAiCompatible,
                "https://api.groq.com/openai/v1/chat/completions",
                groqKey,
                _configuration["GROQ_MODEL"] ?? "llama-3.1-8b-instant"));
        }

        var openRouterKey = _configuration["OPENROUTER_API_KEY"];
        if (!string.IsNullOrWhiteSpace(openRouterKey))
        {
            providers.Add(new AiProvider(
                "OpenRouter",
                AiProviderKind.OpenAiCompatible,
                "https://openrouter.ai/api/v1/chat/completions",
                openRouterKey,
                _configuration["OPENROUTER_MODEL"] ?? "meta-llama/llama-3.1-8b-instruct:free"));
        }

        return providers;
    }

    private async Task<SqlExecutionResult> ExecuteSqlAsync(string sql, Dictionary<string, object?> sqlParameters)
    {
        var result = new SqlExecutionResult
        {
            Columns = new List<string>(),
            Rows = new List<Dictionary<string, object?>>()
        };

        var connection = _context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        try
        {
            if (closeConnection)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;

            foreach (var kvp in sqlParameters)
            {
                var param = command.CreateParameter();
                param.ParameterName = kvp.Key;
                param.Value = kvp.Value ?? DBNull.Value;
                command.Parameters.Add(param);
            }

            await using var reader = await command.ExecuteReaderAsync();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                result.Columns.Add(reader.GetName(i));
            }

            var rowCount = 0;
            while (await reader.ReadAsync() && rowCount < 500)
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[result.Columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                result.Rows.Add(row);
                rowCount++;
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static void ValidateSelectOnly(string sql)
    {
        var normalized = sql.Trim().ToUpperInvariant();

        if (!normalized.StartsWith("SELECT") && !normalized.StartsWith("WITH"))
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
            .Replace("```sql", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "")
            .Trim()
            .TrimEnd(';');
    }

    private enum AiProviderKind
    {
        Gemini,
        OpenAiCompatible
    }

    private sealed record AiProvider(string Name, AiProviderKind Kind, string Endpoint, string ApiKey, string Model);
}

public class SqlResponse
{
    public string Sql { get; set; } = string.Empty;
}

public class AnalysisResponse
{
    public string Analysis { get; set; } = string.Empty;
}
