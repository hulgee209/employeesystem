using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public class ResultInterpreterService : IResultInterpreterService
{
    private readonly ILogger<ResultInterpreterService> _logger;

    public ResultInterpreterService(ILogger<ResultInterpreterService> logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateFallbackAnalysisAsync(
        string question,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        try
        {
            if (rows == null || rows.Count == 0)
            {
                return Task.FromResult("Өгөгдөл олдсонгүй.");
            }

            var firstRow = rows[0];
            var firstColumn = columns.FirstOrDefault();

            if (string.IsNullOrEmpty(firstColumn))
            {
                return Task.FromResult("Үр дүнг унших боломжгүй байна.");
            }

            // Pattern 1: Single COUNT column
            if (columns.Count == 1 && firstColumn.Contains("Count", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractNumericValue(firstRow, firstColumn);
                if (value.HasValue)
                {
                    _logger.LogInformation("ResultInterpreter: Detected COUNT pattern, value={Value}", value);
                    return Task.FromResult($"Өгөгдлийн сангийн үр дүнгээр {value:N0} ажилтан байна.");
                }
            }

            // Pattern 2: Single COUNT column with generic name
            if (columns.Count == 1 && firstColumn.ToLowerInvariant().Contains("count"))
            {
                var value = ExtractNumericValue(firstRow, firstColumn);
                if (value.HasValue)
                {
                    _logger.LogInformation("ResultInterpreter: Detected generic count pattern, value={Value}", value);
                    return Task.FromResult($"Үр дүнгээр {value:N0} ажилтан байна.");
                }
            }

            // Pattern 3: SUM pattern
            if (columns.Count == 1 && firstColumn.Contains("Sum", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractNumericValue(firstRow, firstColumn);
                if (value.HasValue)
                {
                    _logger.LogInformation("ResultInterpreter: Detected SUM pattern, value={Value}", value);
                    return Task.FromResult($"Нийт дүн: {value:N0}");
                }
            }

            // Pattern 4: AVG pattern
            if (columns.Count == 1 && firstColumn.Contains("Avg", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractNumericValue(firstRow, firstColumn);
                if (value.HasValue)
                {
                    _logger.LogInformation("ResultInterpreter: Detected AVG pattern, value={Value}", value);
                    return Task.FromResult($"Дундаж үзүүлэлт: {value:N2}");
                }
            }

            // Pattern 5: Multiple rows - show count and first few values
            if (rows.Count > 1)
            {
                _logger.LogInformation("ResultInterpreter: Detected multiple rows pattern, count={Count}", rows.Count);
                var description = GenerateMultiRowDescription(columns, rows);
                return Task.FromResult(description);
            }

            // Pattern 6: Single row with multiple columns - describe the record
            if (columns.Count > 1)
            {
                _logger.LogInformation("ResultInterpreter: Detected multi-column pattern");
                var description = GenerateSingleRecordDescription(columns, firstRow);
                return Task.FromResult(description);
            }

            // Pattern 7: Generic single cell response
            var cellValue = firstRow.FirstOrDefault().Value?.ToString() ?? "хоосон";
            _logger.LogInformation("ResultInterpreter: Falling back to generic cell value: {Value}", cellValue);
            return Task.FromResult($"Үр дүн: {cellValue}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResultInterpreter: Error generating fallback analysis");
            return Task.FromResult("Үр дүнг унших үед алдаа гарлаа. Л данных сангийн үр дүнг хүснэгтээр харна уу.");
        }
    }

    private decimal? ExtractNumericValue(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => l,
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            string s when decimal.TryParse(s, out var d) => d,
            _ => null
        };
    }

    private string GenerateMultiRowDescription(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var rowCount = rows.Count;
        
        if (columns.Count == 1)
        {
            var firstVal = rows[0].Values.First()?.ToString() ?? "";
            var lastVal = rows[rowCount - 1].Values.First()?.ToString() ?? "";
            return $"Нийтэм {rowCount} үр дүн олдлоо. Эхний утга: {firstVal}, сүүлийн утга: {lastVal}";
        }

        return $"Нийтэм {rowCount} үр дүн олдлоо.";
    }

    private string GenerateSingleRecordDescription(IReadOnlyList<string> columns, IReadOnlyDictionary<string, object?> row)
    {
        var nonNullColumns = columns
            .Where(c => row.TryGetValue(c, out var v) && v != null)
            .Take(3)
            .ToList();

        if (!nonNullColumns.Any())
        {
            return "Үр дүн хүлээлээ.";
        }

        var values = string.Join(", ", nonNullColumns.Select(c =>
        {
            row.TryGetValue(c, out var v);
            return $"{c}: {v}";
        }));

        return $"Үр дүн: {values}";
    }
}
