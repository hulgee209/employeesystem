using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmployeeSystem.Models;
using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public interface IEntityResolver
{
    Task<EntityResolutionResult> ResolveDepartmentNameAsync(string question, DatabaseMetadata metadata);
    Task<EntityResolutionResult> ResolveLookupValueAsync(string value, string tableName, string columnName, DatabaseMetadata metadata);
}

public sealed class EntityResolutionResult
{
    public string? CanonicalValue { get; init; }
    public bool IsExactMatch { get; init; }
    public bool UseLikeFallback { get; init; }
    public string? LikePattern { get; init; }
}

public class EntityResolver : IEntityResolver
{
    private readonly ILogger<EntityResolver> _logger;

    private static readonly Dictionary<string, string> DepartmentAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Human Resources"] = "Human Resource",
        ["HR"] = "Human Resource",
        ["Хүний нөөц"] = "Human Resource",
        ["People Operations"] = "Human Resource",
        ["Human Resource"] = "Human Resource",
        ["Human Resources Department"] = "Human Resource"
    };

    public EntityResolver(ILogger<EntityResolver> logger)
    {
        _logger = logger;
    }

    public Task<EntityResolutionResult> ResolveDepartmentNameAsync(string question, DatabaseMetadata metadata)
    {
        var candidate = ExtractCandidateFromText(question);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Task.FromResult(new EntityResolutionResult());
        }

        var resolution = ResolveLookupValue(candidate, "Departments", "DepartmentName", metadata);
        if (resolution != null)
        {
            return Task.FromResult(resolution);
        }

        // If question contains a known alias, return the canonical form.
        foreach (var alias in DepartmentAliases)
        {
            if (question.Contains(alias.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new EntityResolutionResult
                {
                    CanonicalValue = alias.Value,
                    IsExactMatch = true
                });
            }
        }

        return Task.FromResult(new EntityResolutionResult());
    }

    public Task<EntityResolutionResult> ResolveLookupValueAsync(string value, string tableName, string columnName, DatabaseMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(new EntityResolutionResult());
        }

        var resolution = ResolveLookupValue(value, tableName, columnName, metadata);
        return Task.FromResult(resolution ?? new EntityResolutionResult());
    }

    private EntityResolutionResult? ResolveLookupValue(string value, string tableName, string columnName, DatabaseMetadata metadata)
    {
        var normalizedValue = Normalize(value);
        var aliasMatch = LookupAlias(tableName, columnName, normalizedValue);
        if (aliasMatch != null)
        {
            return new EntityResolutionResult
            {
                CanonicalValue = aliasMatch,
                IsExactMatch = true
            };
        }

        var candidates = GetLookupCandidates(metadata, tableName, columnName).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var exact = candidates.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return new EntityResolutionResult
            {
                CanonicalValue = exact,
                IsExactMatch = true
            };
        }

        exact = candidates.FirstOrDefault(c => Normalize(c) == normalizedValue);
        if (exact != null)
        {
            return new EntityResolutionResult
            {
                CanonicalValue = exact,
                IsExactMatch = true
            };
        }

        var closest = FindClosestCandidate(normalizedValue, candidates);
        if (closest != null)
        {
            return new EntityResolutionResult
            {
                CanonicalValue = closest,
                IsExactMatch = false
            };
        }

        return new EntityResolutionResult
        {
            UseLikeFallback = true,
            LikePattern = BuildLikePattern(value)
        };
    }

    private static string? LookupAlias(string tableName, string columnName, string normalizedValue)
    {
        if (!string.Equals(tableName, "Departments", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(columnName, "DepartmentName", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DepartmentAliases.TryGetValue(normalizedValue, out var canonical)
            ? canonical
            : null;
    }

    private static IEnumerable<string> GetLookupCandidates(DatabaseMetadata metadata, string tableName, string columnName)
    {
        var table = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (table == null)
        {
            return Array.Empty<string>();
        }

        return table.SampleRows
            .Select(row => row.TryGetValue(columnName, out var value) ? value as string : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindClosestCandidate(string normalizedValue, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = ComputeLevenshteinDistance(normalizedValue, Normalize(candidate));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best == null)
        {
            return null;
        }

        var threshold = Math.Max(1, normalizedValue.Length / 3);
        return bestDistance <= threshold ? best : null;
    }

    private static string Normalize(string text)
    {
        if (text == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Trim().ToLowerInvariant());
        return builder.ToString();
    }

    private static string ExtractCandidateFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Trim();
    }

    private static string BuildLikePattern(string text)
    {
        var normalized = Normalize(text);
        var cleaned = new string(normalized.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
        var keywords = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return keywords.Length == 0
            ? "%"
            : "%" + string.Join("%", keywords) + "%";
    }

    private static int ComputeLevenshteinDistance(string? source, string? target)
    {
        if (source == null) source = string.Empty;
        if (target == null) target = string.Empty;

        var sourceLength = source.Length;
        var targetLength = target.Length;

        if (sourceLength == 0) return targetLength;
        if (targetLength == 0) return sourceLength;

        var distance = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; distance[i, 0] = i++) { }
        for (var j = 0; j <= targetLength; distance[0, j] = j++) { }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }
}
