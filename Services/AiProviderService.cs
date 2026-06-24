using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EmployeeSystem.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Wrap;

namespace EmployeeSystem.Services;

public interface IAiProviderService
{
    Task<AiProviderResponse> GenerateSqlAsync(string question, string schema, string rolePolicy, IReadOnlyList<string> selectedTables, string selectionReason);
    Task<AiProviderResponse> GenerateAnalysisAsync(string question, string schema, string rolePolicy, string sql, string resultJson);
}

public sealed record AiProviderResponse(bool Success, string? Text, string? Error, bool IsQuota, string ProviderName);

public class AiProviderService : IAiProviderService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<AiProviderConfig> _providers;
    private readonly ILogger<AiProviderService> _logger;
    private readonly ConcurrentDictionary<string, AsyncPolicy<HttpResponseMessage>> _policies = new();

    private const int DefaultTimeoutSeconds = 120;

    public AiProviderService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AiProviderService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _providers = BuildProviders(configuration).ToList();
    }

    public async Task<AiProviderResponse> GenerateSqlAsync(string question, string schema, string rolePolicy, IReadOnlyList<string> selectedTables, string selectionReason)
    {
        var payload = new SqlAgentPromptRequest(question, schema, rolePolicy, selectedTables, selectionReason);
        return await SendPromptAsync("sql-agent", payload);
    }

    public async Task<AiProviderResponse> GenerateAnalysisAsync(string question, string schema, string rolePolicy, string sql, string resultJson)
    {
        var payload = new SqlAnalysisRequest(question, schema, rolePolicy, sql, resultJson);
        return await SendPromptAsync("sql-analysis", payload);
    }

    private async Task<AiProviderResponse> SendPromptAsync(string route, object payload)
    {
        foreach (var provider in _providers)
        {
            var result = await TrySendRequestAsync(provider, route, payload);
            if (result.Success)
            {
                return result;
            }

            if (!result.IsQuota)
            {
                _logger.LogWarning("AI provider {Provider} returned a non-quota failure and will not be retried. Error: {Error}", provider.Name, result.Error);
                continue;
            }

            _logger.LogWarning("AI provider {Provider} failed with quota or transient error: {Error}", provider.Name, result.Error);
        }

        return new AiProviderResponse(false, null, "No AI provider was available for this request.", false, string.Empty);
    }

    private async Task<AiProviderResponse> TrySendRequestAsync(AiProviderConfig provider, string route, object payload)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return new AiProviderResponse(false, null, "Provider endpoint is not configured.", false, provider.Name);
        }

        var policy = _policies.GetOrAdd(provider.Name, _ => CreatePolicy(provider.Name));
        var requestUri = provider.BaseUrl.TrimEnd('/') + "/" + route.TrimStart('/');
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        try
        {
            var response = await policy.ExecuteAsync(() => client.PostAsJsonAsync(requestUri, payload));

            if (response.IsSuccessStatusCode)
            {
                if (string.Equals(route, "sql-agent", StringComparison.OrdinalIgnoreCase))
                {
                    var content = await response.Content.ReadFromJsonAsync<SqlAgentPromptResponse>();
                    if (content == null || string.IsNullOrWhiteSpace(content.Sql))
                    {
                        return new AiProviderResponse(false, null, "AI provider returned an empty SQL response.", false, provider.Name);
                    }

                    return new AiProviderResponse(true, content.Sql, null, false, provider.Name);
                }

                var analysisContent = await response.Content.ReadFromJsonAsync<SqlAnalysisResponse>();
                if (analysisContent == null || string.IsNullOrWhiteSpace(analysisContent.Analysis))
                {
                    return new AiProviderResponse(false, null, "AI provider returned an empty analysis response.", false, provider.Name);
                }

                return new AiProviderResponse(true, analysisContent.Analysis, null, false, provider.Name);
            }

            var body = await response.Content.ReadAsStringAsync();
            var isQuota = response.StatusCode == HttpStatusCode.TooManyRequests ||
                          response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                          response.StatusCode == HttpStatusCode.BadGateway ||
                          response.StatusCode == HttpStatusCode.GatewayTimeout;

            return new AiProviderResponse(false, null, $"{response.StatusCode}: {body}", isQuota, provider.Name);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker open for provider {Provider}. Skipping provider.", provider.Name);
            return new AiProviderResponse(false, null, "Circuit breaker is open for provider.", true, provider.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while requesting AI provider {Provider}.", provider.Name);
            return new AiProviderResponse(false, null, ex.Message, true, provider.Name);
        }
    }

    private AsyncPolicy<HttpResponseMessage> CreatePolicy(string providerName)
    {
        var retry = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => IsTransientFailure(r))
            .WaitAndRetryAsync(new[]
            {
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000)
            }, (outcome, timespan, retryNumber, context) =>
            {
                _logger.LogDebug("Retry {RetryNumber} for provider {Provider} after {Delay} because {Reason}.", retryNumber, providerName, timespan, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
            });

        var breaker = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => IsTransientFailure(r))
            .CircuitBreakerAsync(3, TimeSpan.FromMinutes(1), (result, duration) =>
            {
                _logger.LogWarning("Circuit breaker opened for provider {Provider} for {Duration} due to {Reason}.", providerName, duration, result.Exception?.Message ?? result.Result?.StatusCode.ToString());
            }, () =>
            {
                _logger.LogInformation("Circuit breaker reset for provider {Provider}.", providerName);
            });

        return Policy.WrapAsync(retry, breaker);
    }

    private static bool IsTransientFailure(HttpResponseMessage response)
    {
        return response == null ||
               response.StatusCode == HttpStatusCode.RequestTimeout ||
               response.StatusCode == HttpStatusCode.TooManyRequests ||
               response.StatusCode == HttpStatusCode.BadGateway ||
               response.StatusCode == HttpStatusCode.ServiceUnavailable ||
               response.StatusCode == HttpStatusCode.GatewayTimeout;
    }

    private static IEnumerable<AiProviderConfig> BuildProviders(IConfiguration configuration)
    {
        var providers = new List<AiProviderConfig>
        {
            new("Gemini", configuration["AiProviders:GeminiEndpoint"]?.Trim() ?? "http://127.0.0.1:8000")
        };

        var claude = configuration["AiProviders:ClaudeEndpoint"]?.Trim();
        if (!string.IsNullOrWhiteSpace(claude)) providers.Add(new("Claude", claude));

        var gpt4o = configuration["AiProviders:Gpt4oEndpoint"]?.Trim();
        if (!string.IsNullOrWhiteSpace(gpt4o)) providers.Add(new("GPT-4o", gpt4o));

        return providers;
    }

    private sealed record AiProviderConfig(string Name, string BaseUrl);

    private sealed record SqlAgentPromptRequest(string Question, string Schema, string RolePolicy, IReadOnlyList<string> SelectedTables, string SelectionReason);

    private sealed record SqlAgentPromptResponse(string Sql);

    private sealed record SqlAnalysisRequest(string Question, string Schema, string RolePolicy, string Sql, string ResultJson);

    private sealed record SqlAnalysisResponse(string Analysis);
}
