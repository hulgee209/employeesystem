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

        if (IsObviousGeneralQuestion(question))
        {
            return await AnswerGeneralQuestionAsync(question);
        }

        try
        {
            var (sql, execution) = await GenerateAndExecuteSqlWithRepairAsync(question);
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
            var directAnswer = await TryAnswerDirectlyAsync(question);
            if (directAnswer != null)
            {
                return directAnswer;
            }

            if (!IsObviousGeneralQuestion(question))
            {
                return await AnswerGeneralQuestionAsync(question);
            }

            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээтэй холбогдож чадсангүй. Render Environment дээр AI API key тохирсон эсэхийг шалгана уу."
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout occurred contacting AI provider.");
            var directAnswer = await TryAnswerDirectlyAsync(question);
            if (directAnswer != null)
            {
                return directAnswer;
            }

            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээ хариу өгөхөд удаан байна. Дахин оролдоно уу."
            };
        }
        catch (AiSqlAgentQuotaException ex)
        {
            _logger.LogWarning(ex, "AI provider quota or service unavailable.");
            var directAnswer = await TryAnswerDirectlyAsync(question);
            if (directAnswer != null)
            {
                return directAnswer;
            }

            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээ түр боломжгүй байна. API quota эсвэл model тохиргоог шалгаад дахин оролдоно уу."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AnswerAsync.");
            var directAnswer = await TryAnswerDirectlyAsync(question);
            if (directAnswer != null)
            {
                return directAnswer;
            }

            return new AiSqlAgentResult
            {
                Analysis = "Системд алдаа гарлаа. Админ руу мэдэгдэнэ үү."
            };
        }
    }

    private async Task<(string Sql, SqlExecutionResult Execution)> GenerateAndExecuteSqlWithRepairAsync(string question)
    {
        const int maxAttempts = 3;
        string? previousSql = null;
        string? previousError = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var sql = await GenerateSqlFromAiAsync(question, previousSql, previousError);
                previousSql = sql;
                ValidateSelectOnly(sql);
                var execution = await ExecuteSqlAsync(sql, new Dictionary<string, object?>());
                return (sql, execution);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                previousError = ex.Message;
                _logger.LogWarning(ex, "AI SQL attempt {Attempt} failed. Asking AI to repair the query.", attempt);
            }
        }

        throw lastException ?? new InvalidOperationException("AI SQL generation failed.");
    }

    private async Task<AiSqlAgentResult> AnswerGeneralQuestionAsync(string question)
    {
        try
        {
            var systemPrompt = """
                You are a helpful assistant inside an employee management system.
                The user's question is not asking for database data.
                Answer naturally in Mongolian unless the user clearly uses another language.
                Do not generate SQL.
                Keep the answer concise.
                """;

            var answer = await GenerateChatTextAsync(systemPrompt, question, maxTokens: 500);
            return new AiSqlAgentResult { Analysis = answer };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "General AI answer failed.");

            if (IsGreeting(question))
            {
                return new AiSqlAgentResult
                {
                    Analysis = "Сайн байна уу? HR болон ажилтны мэдээлэлтэй холбоотой асуултаа бичээрэй."
                };
            }

            return new AiSqlAgentResult
            {
                Analysis = "AI үйлчилгээ түр боломжгүй байна. Дахин оролдоно уу."
            };
        }
    }

    private static bool IsObviousGeneralQuestion(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();
        if (IsGreeting(question))
        {
            return true;
        }

        var generalOnlyPhrases = new[]
        {
            "bayarlalaa", "thanks", "thank you", "юу хийж чаддаг", "chi hen be",
            "чи хэн бэ", "тусламж", "help", "яаж ашиглах", "how to use"
        };

        return generalOnlyPhrases.Any(normalized.Contains);
    }

    private static bool IsGreeting(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();
        return normalized is "hi" or "hello" or "hey" or "sain uu" or "сайн уу" or "сайн байна уу";
    }

    private async Task<AiSqlAgentResult?> TryAnswerDirectlyAsync(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "medeelel", "medeelliig", "buren", "info", "heltes", "department"))
        {
            var infoAnswer = await TryAnswerEmployeeInfoDirectlyAsync(normalized);
            if (infoAnswer != null)
            {
                return infoAnswer;
            }
        }

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

            var tokens = ExtractEmployeeSearchTokens(normalized);

            if (tokens.Length == 0)
            {
                return null;
            }

            var allEmployees = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.EmployeeId)
                .ToListAsync();
            var employees = allEmployees
                .Select(e => new { Employee = e, Score = GetEmployeeMatchScore(e, tokens) })
                .Where(x => x.Score.HasValue)
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Employee.EmployeeId)
                .Take(5)
                .Select(x => x.Employee)
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

    private async Task<AiSqlAgentResult?> TryAnswerEmployeeInfoDirectlyAsync(string normalizedQuestion)
    {
        var tokens = ExtractEmployeeSearchTokens(normalizedQuestion);
        if (tokens.Length == 0)
        {
            return null;
        }

        var employees = await _context.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Position)
            .OrderBy(e => e.EmployeeId)
            .ToListAsync();

        var employee = employees
            .Select(e => new { Employee = e, Score = GetEmployeeMatchScore(e, tokens) })
            .Where(x => x.Score.HasValue)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Employee.EmployeeId)
            .Select(x => x.Employee)
            .FirstOrDefault();
        if (employee == null)
        {
            return new AiSqlAgentResult { Analysis = "Тийм нэртэй ажилтан олдсонгүй." };
        }

        if (ContainsAny(normalizedQuestion, "heltes", "department"))
        {
            return new AiSqlAgentResult
            {
                Analysis = $"{employee.FirstName} {employee.LastName} нь {employee.Department.DepartmentName} хэлтэст ажилладаг."
            };
        }

        var latestPayroll = await _context.Payroll
            .AsNoTracking()
            .Where(p => p.EmployeeId == employee.EmployeeId)
            .OrderByDescending(p => p.PayrollMonth)
            .FirstOrDefaultAsync();
        var latestPerformance = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(p => p.EmployeeId == employee.EmployeeId)
            .OrderByDescending(p => p.ReviewDate)
            .FirstOrDefaultAsync();

        return new AiSqlAgentResult
        {
            Analysis =
                $"{employee.FirstName} {employee.LastName}\n" +
                $"Хэлтэс: {employee.Department.DepartmentName}\n" +
                $"Албан тушаал: {employee.Position.PositionName}\n" +
                $"Утас: {employee.Phone ?? "-"}\n" +
                $"Имэйл: {employee.Email ?? "-"}\n" +
                $"Ажилд орсон: {employee.HireDate?.ToString() ?? "-"}\n" +
                $"Төлөв: {(employee.IsActive ? "Идэвхтэй" : "Идэвхгүй")}\n" +
                $"Сүүлийн цэвэр цалин: {(latestPayroll?.NetSalary == null ? "-" : latestPayroll.NetSalary.Value.ToString("N0"))}\n" +
                $"Сүүлийн үнэлгээ: {(latestPerformance?.Score == null ? "-" : latestPerformance.Score.ToString())}"
        };
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(text.Contains);
    }

    private static string[] ExtractEmployeeSearchTokens(string normalizedQuestion)
    {
        return ExtractEmployeeSearchText(normalizedQuestion)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeNameToken)
            .Where(token => token.Length >= 2)
            .Distinct()
            .Take(8)
            .ToArray();
    }

    private static bool EmployeeMatchesToken(Employee employee, string token)
    {
        return GetEmployeeSearchValues(employee).Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static int? GetEmployeeMatchScore(Employee employee, IEnumerable<string> tokens)
    {
        int? bestScore = null;
        var values = GetEmployeeSearchValues(employee).ToList();

        foreach (var token in tokens)
        {
            var score =
                values.Any(value => string.Equals(value, token, StringComparison.OrdinalIgnoreCase)) ? 0 :
                values.Any(value => value.StartsWith(token, StringComparison.OrdinalIgnoreCase)) ? 1 :
                values.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)) ? 2 :
                (int?)null;

            if (score.HasValue && (!bestScore.HasValue || score.Value < bestScore.Value))
            {
                bestScore = score.Value;
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> GetEmployeeSearchValues(Employee employee)
    {
        yield return employee.FirstName.ToLowerInvariant();
        yield return employee.LastName.ToLowerInvariant();
        yield return TransliterateMongolian(employee.FirstName);
        yield return TransliterateMongolian(employee.LastName);

        if (!string.IsNullOrWhiteSpace(employee.Email))
            yield return employee.Email.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(employee.Phone))
            yield return employee.Phone.ToLowerInvariant();
    }

    private static string NormalizeNameToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        foreach (var suffix in new[] { "giin", "iin", "iin", "yn", "gii", "ii", "iig" })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                normalized.Length > suffix.Length + 1)
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        return normalized;
    }

    private static string TransliterateMongolian(string value)
    {
        var map = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
            ['е'] = "e", ['ё'] = "yo", ['ж'] = "j", ['з'] = "z", ['и'] = "i",
            ['й'] = "i", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
            ['о'] = "o", ['ө'] = "u", ['п'] = "p", ['р'] = "r", ['с'] = "s",
            ['т'] = "t", ['у'] = "u", ['ү'] = "u", ['ф'] = "f", ['х'] = "h",
            ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "",
            ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
        };

        var chars = value.ToLowerInvariant()
            .Select(ch => map.TryGetValue(ch, out var latin) ? latin : ch.ToString());

        return string.Concat(chars);
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

    private async Task<string> GenerateSqlFromAiAsync(string question, string? previousSql = null, string? previousError = null)
    {
        var systemPrompt = """
            You are an HR analytics SQL assistant.
            Generate PostgreSQL SELECT queries only. Return only SQL, no markdown and no explanation.
            Decide the needed tables and joins from the user's natural-language question.
            Use quoted identifiers exactly as shown because table and column names are PascalCase.
            Do not use SQL Server syntax. Use LIMIT instead of TOP. Use COALESCE instead of ISNULL.
            Never modify data.
            Understand Mongolian, English, and Mongolian Latin transliteration.
            For person lookup questions, search "FirstName", "LastName", "Email", and "Phone" with ILIKE.
            For Latin transliteration names, use ILIKE against both name columns and email; do not require exact spelling.
            If the user asks for "full information", include employee name, department, position, phone, email, hire date, status, latest salary, attendance summary, and latest performance when possible.
            If the user asks about "most late department", group Attendance where Status='Late' by department and order by count descending.
            If the user asks average salary by a department, join Payroll -> Employees -> Departments and average "NetSalary".

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

        var prompt = string.IsNullOrWhiteSpace(previousSql)
            ? question
            : $"""
                Original question:
                {question}

                Your previous SQL failed:
                {previousSql}

                Database error:
                {previousError}

                Return a corrected PostgreSQL SELECT query only.
                """;

        return NormalizeSql(await GenerateChatTextAsync(systemPrompt, prompt, maxTokens: 700));
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
            _logger.LogWarning(ex, "Analysis generation error, using deterministic fallback.");
            return FormatExecutionResult(execution);
        }
    }

    private static string FormatExecutionResult(SqlExecutionResult execution)
    {
        if (execution.Rows.Count == 0)
        {
            return "Өгөгдөл олдсонгүй.";
        }

        if (execution.Rows.Count == 1)
        {
            var row = execution.Rows[0];
            if (row.Count == 1)
            {
                return $"Хариу: {FormatValue(row.Values.FirstOrDefault())}.";
            }

            return string.Join("\n", row.Take(8).Select(kvp => $"{HumanizeColumn(kvp.Key)}: {FormatValue(kvp.Value)}"));
        }

        var lines = execution.Rows
            .Take(5)
            .Select(row => string.Join(", ", row.Take(4).Select(kvp => $"{HumanizeColumn(kvp.Key)}: {FormatValue(kvp.Value)}")))
            .ToList();

        var suffix = execution.Rows.Count > 5
            ? $"\n... нийт {execution.Rows.Count} мөрөөс эхний 5 мөрийг харууллаа."
            : string.Empty;

        return string.Join("\n", lines) + suffix;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "-",
            decimal d => d.ToString("N0"),
            double d => d.ToString("N2"),
            float f => f.ToString("N2"),
            DateTime date => date.ToString("yyyy-MM-dd"),
            _ => value.ToString() ?? "-"
        };
    }

    private static string HumanizeColumn(string column)
    {
        return column switch
        {
            "DepartmentName" or "Department" => "Хэлтэс",
            "Employee" or "EmployeeName" => "Ажилтан",
            "FirstName" => "Нэр",
            "LastName" => "Овог",
            "Phone" => "Утас",
            "Email" => "Имэйл",
            "LateCount" => "Хоцролт",
            "EmployeeCount" => "Ажилтны тоо",
            "AverageSalary" or "AverageNetSalary" => "Дундаж цалин",
            "NetSalary" => "Цэвэр цалин",
            "Score" or "AverageScore" => "Үнэлгээ",
            _ => column
        };
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
            catch (AiSqlAgentQuotaException ex)
            {
                _logger.LogWarning(ex, "AI provider {Provider} quota/unavailable, trying next provider.", provider.Name);
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

        var configuredGeminiModel = _configuration["GEMINI_MODEL"] ?? "gemini-2.5-flash";
        var geminiKeys = new[]
        {
            _configuration["GEMINI_API_KEY"],
            _configuration["GEMINI_API_KEY_1"],
            _configuration["GEMINI_API_KEY_2"],
            _configuration["GEMINI_API_KEY_3"]
        }
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct()
            .ToList();

        for (var index = 0; index < geminiKeys.Count; index++)
        {
            var geminiKey = geminiKeys[index]!;
            var providerName = index == 0 ? "Gemini" : $"Gemini key {index + 1}";
            providers.Add(new AiProvider(
                providerName,
                AiProviderKind.Gemini,
                "https://generativelanguage.googleapis.com",
                geminiKey,
                configuredGeminiModel));

            if (!string.Equals(configuredGeminiModel, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new AiProvider(
                    $"{providerName} fallback",
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
