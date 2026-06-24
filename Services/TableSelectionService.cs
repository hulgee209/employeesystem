using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EmployeeSystem.Services;

public interface ITableSelectionService
{
    Task<TableSelectionResult> SelectTablesAsync(string question);
    Task<string> BuildSchemaContextAsync(IReadOnlyList<string> selectedTables);
    Task<string> BuildSampleRowsContextAsync(IReadOnlyList<string> selectedTables, int rowsPerTable = 3);
}

public class TableSelectionService : ITableSelectionService
{
    private const int MaxSelectedTables = 6;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly EmployeeDbContext _context;
    private readonly ISchemaCacheService _schemaCacheService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TableSelectionService> _logger;

    public TableSelectionService(
        EmployeeDbContext context,
        ISchemaCacheService schemaCacheService,
        IMemoryCache cache,
        ILogger<TableSelectionService> logger)
    {
        _context = context;
        _schemaCacheService = schemaCacheService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TableSelectionResult> SelectTablesAsync(string question)
    {
        var normalizedQuestion = question.Trim();
        var cacheKey = "table-selection:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedQuestion.ToLowerInvariant())));

        if (_cache.TryGetValue(cacheKey, out TableSelectionResult? cached) && cached != null)
        {
            _logger.LogInformation(
                "AI table selection cache hit. Question: {Question}. SelectedTables: {SelectedTables}",
                normalizedQuestion,
                string.Join(",", cached.SelectedTables));

            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var metadata = await _schemaCacheService.GetSchemaAsync();
        var availableTables = metadata.Tables
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedTables = await SelectTablesFromMetadataAsync(normalizedQuestion, availableTables, metadata);

        if (selectedTables.Count == 0)
        {
            selectedTables = GetHeuristicTablesForQuestion(normalizedQuestion, availableTables);
        }

        selectedTables = EnsureEmployeesForDepartmentPeopleQuestions(normalizedQuestion, selectedTables, availableTables);

        if (selectedTables.Count == 0)
        {
            selectedTables = availableTables
                .Where(t => DefaultHrTables.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Take(MaxSelectedTables)
                .ToList();
        }

        if (selectedTables.Count == 0)
        {
            selectedTables = availableTables
                .OrderBy(t => t)
                .Take(MaxSelectedTables)
                .ToList();
        }

        var result = new TableSelectionResult
        {
            SelectedTables = selectedTables,
            Reason = "Metadata-driven table selection used."
        };

        _cache.Set(cacheKey, result, CacheDuration);

        stopwatch.Stop();
        _logger.LogInformation(
            "AI table selection completed. Question: {Question}. SelectedTables: {SelectedTables}. DurationMs: {DurationMs}",
            normalizedQuestion,
            string.Join(",", selectedTables),
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    public async Task<string> BuildSchemaContextAsync(IReadOnlyList<string> selectedTables)
    {
        var tableSet = selectedTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expandedTableSet = await ExpandTableSetWithRelatedTablesAsync(tableSet);

        var builder = new StringBuilder();
        builder.AppendLine("SELECTED DATABASE SCHEMA");
        builder.AppendLine("Use only these selected tables and columns.");

        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, NUMERIC_PRECISION, NUMERIC_SCALE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_NAME, ORDINAL_POSITION
            """;

        await using var reader = await command.ExecuteReaderAsync();
        string? currentTable = null;

        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);

            if (!expandedTableSet.Contains(table))
            {
                continue;
            }

            if (currentTable != table)
            {
                currentTable = table;
                builder.AppendLine();
                builder.AppendLine($"{table}:");
            }

            var column = reader.GetString(1);
            var dataType = reader.GetString(2);
            var nullable = reader.GetString(3);
            var precision = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
            var scale = reader.IsDBNull(5) ? null : reader.GetValue(5)?.ToString();
            var length = reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString();

            builder.AppendLine($"- {column} ({FormatSqlType(dataType, precision, scale, length)}, nullable: {nullable})");
        }

        // Ensure reader is closed before executing other commands on the same connection
        try
        {
            await reader.CloseAsync();
        }
        catch
        {
            // ignore any close errors; reader will be disposed by await using
        }

        var relationships = await GetForeignKeyContextAsync(expandedTableSet);
        if (!string.IsNullOrWhiteSpace(relationships))
        {
            builder.AppendLine();
            builder.Append(relationships);
        }

        return builder.ToString();
    }

    public async Task<string> BuildSampleRowsContextAsync(IReadOnlyList<string> selectedTables, int rowsPerTable = 3)
    {
        var availableTables = await GetAvailableTablesAsync();
        var safeTables = selectedTables
            .Where(t => availableTables.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Take(MaxSelectedTables)
            .ToList();

        var expandedTables = await ExpandTableSetWithRelatedTablesAsync(safeTables.ToHashSet(StringComparer.OrdinalIgnoreCase));

        var builder = new StringBuilder();
        builder.AppendLine("SAMPLE ROWS");
        builder.AppendLine("Samples are for understanding value shapes only. Do not infer facts beyond query results.");

        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();

        foreach (var table in expandedTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT TOP {Math.Clamp(rowsPerTable, 1, 5)} * FROM [{EscapeSqlIdentifier(table)}]";

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                builder.AppendLine();
                builder.AppendLine($"{table}:");

                var columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetName)
                    .ToList();

                var rowIndex = 0;
                while (await reader.ReadAsync())
                {
                    rowIndex++;
                    var values = columns.Select(column =>
                    {
                        var value = reader[column];
                        return $"{column}={FormatSampleValue(value)}";
                    });

                    builder.AppendLine($"- Row {rowIndex}: {string.Join("; ", values)}");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is DbException)
            {
                builder.AppendLine();
                builder.AppendLine($"{table}: sample unavailable ({ex.Message})");
            }
        }

        return builder.ToString();
    }

    private async Task<string> BuildMetadataContextAsync()
    {
        var builder = new StringBuilder();
        builder.AppendLine("DATABASE METADATA");
        builder.AppendLine("Select relevant tables from this metadata only.");

        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_NAME, ORDINAL_POSITION
            """;

        await using var reader = await command.ExecuteReaderAsync();
        string? currentTable = null;
        var tableColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            var column = reader.GetString(1);
            var dataType = reader.GetString(2);

            if (!tableColumns.TryGetValue(table, out var columns))
            {
                columns = new List<string>();
                tableColumns[table] = columns;
            }

            columns.Add(column);

            if (currentTable != table)
            {
                currentTable = table;
                builder.AppendLine();
                builder.AppendLine($"{table}:");
            }

            builder.AppendLine($"- {column} ({dataType})");
        }

        try
        {
            await reader.CloseAsync();
        }
        catch
        {
        }

        var relationships = await GetForeignKeyContextAsync(null);
        if (!string.IsNullOrWhiteSpace(relationships))
        {
            builder.AppendLine();
            builder.Append(relationships);
        }

        return builder.ToString();
    }

    private async Task<string> GetForeignKeyContextAsync(ISet<string>? tableFilter)
    {
        var builder = new StringBuilder();
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                fk.name AS ForeignKeyName,
                parentTable.name AS ParentTable,
                parentColumn.name AS ParentColumn,
                referencedTable.name AS ReferencedTable,
                referencedColumn.name AS ReferencedColumn
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.tables parentTable ON fkc.parent_object_id = parentTable.object_id
            INNER JOIN sys.columns parentColumn ON fkc.parent_object_id = parentColumn.object_id AND fkc.parent_column_id = parentColumn.column_id
            INNER JOIN sys.tables referencedTable ON fkc.referenced_object_id = referencedTable.object_id
            INNER JOIN sys.columns referencedColumn ON fkc.referenced_object_id = referencedColumn.object_id AND fkc.referenced_column_id = referencedColumn.column_id
            ORDER BY parentTable.name, referencedTable.name
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            var parentTable = reader.GetString(1);
            var parentColumn = reader.GetString(2);
            var referencedTable = reader.GetString(3);
            var referencedColumn = reader.GetString(4);

            if (tableFilter != null &&
                (!tableFilter.Contains(parentTable) || !tableFilter.Contains(referencedTable)))
            {
                continue;
            }

            rows.Add($"- {parentTable}.{parentColumn} -> {referencedTable}.{referencedColumn}");
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        builder.AppendLine("FOREIGN KEYS:");
        foreach (var row in rows)
        {
            builder.AppendLine(row);
        }

        return builder.ToString();
    }

    private static readonly string[] DefaultHrTables =
    {
        "Employees",
        "Departments",
        "Positions",
        "Payroll",
        "Attendance",
        "PerformanceReviews",
        "Trainings",
        "EmployeeAssets",
        "EmployeeProjects",
        "Candidates"
    };

    private async Task<HashSet<string>> ExpandTableSetWithRelatedTablesAsync(ISet<string> selectedTables)
    {
        var expandedTables = new HashSet<string>(selectedTables, StringComparer.OrdinalIgnoreCase);
        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT parentTable.name AS ParentTable, referencedTable.name AS ReferencedTable
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.tables parentTable ON fkc.parent_object_id = parentTable.object_id
            INNER JOIN sys.tables referencedTable ON fkc.referenced_object_id = referencedTable.object_id
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var parentTable = reader.GetString(0);
            var referencedTable = reader.GetString(1);

            if (selectedTables.Contains(parentTable) || selectedTables.Contains(referencedTable))
            {
                expandedTables.Add(parentTable);
                expandedTables.Add(referencedTable);
            }
        }

        return expandedTables;
    }

    private static List<string> GetHeuristicTablesForQuestion(string normalizedQuestion, HashSet<string> availableTables)
    {
        var terms = normalizedQuestion.ToLowerInvariant();

        var answer = new List<string>();

        if (terms.Contains("salary") || terms.Contains("цалин") || terms.Contains("payroll") || terms.Contains("хөлс"))
        {
            answer.AddRange(new[] { "Payroll", "Employees", "Departments" });
        }

        if (terms.Contains("attendance") || terms.Contains("hotsrolt") || terms.Contains("tardy") || terms.Contains("late") || terms.Contains("хоцрол"))
        {
            answer.AddRange(new[] { "Attendance", "Employees", "Departments" });
        }

        if (terms.Contains("performance") || terms.Contains("үнэлгээ") || terms.Contains("score") || terms.Contains("оноо"))
        {
            answer.AddRange(new[] { "PerformanceReviews", "Employees", "Departments" });
        }

        if (terms.Contains("department") || terms.Contains("heltes") || terms.Contains("хэлтэс") || terms.Contains("segment"))
        {
            answer.AddRange(new[] { "Departments", "Employees", "Positions" });
        }

        if (terms.Contains("user") || terms.Contains("login") || terms.Contains("auth") || terms.Contains("username") || terms.Contains("password"))
        {
            answer.AddRange(new[] { "Users", "Roles", "UserRoles" });
        }

        return answer
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(availableTables.Contains)
            .Take(MaxSelectedTables)
            .ToList();
    }

    private static List<string> EnsureEmployeesForDepartmentPeopleQuestions(string normalizedQuestion, IReadOnlyList<string> selectedTables, HashSet<string> availableTables)
    {
        if (!availableTables.Contains("Departments") || !availableTables.Contains("Employees"))
        {
            return selectedTables.ToList();
        }

        var lowerQuestion = normalizedQuestion.ToLowerInvariant();
        var needsEmployees = lowerQuestion.Contains("people") || lowerQuestion.Contains("staff") || lowerQuestion.Contains("person") ||
                             lowerQuestion.Contains("employee") || lowerQuestion.Contains("ajiltan") ||
                             lowerQuestion.Contains("hun") || lowerQuestion.Contains("хүн") || lowerQuestion.Contains("ажилтан");

        if (needsEmployees && selectedTables.Contains("Departments", StringComparer.OrdinalIgnoreCase) &&
            !selectedTables.Contains("Employees", StringComparer.OrdinalIgnoreCase))
        {
            var expanded = new List<string>(selectedTables) { "Employees" };
            return expanded
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSelectedTables)
                .ToList();
        }

        return selectedTables.ToList();
    }

    private async Task<HashSet<string>> GetAvailableTablesAsync()
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<Dictionary<string, List<string>>> GetTableColumnsAsync()
    {
        var tableColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var connection = _context.Database.GetDbConnection();
        await using var _ = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME, ORDINAL_POSITION";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            var column = reader.GetString(1);

            if (!tableColumns.TryGetValue(table, out var columns))
            {
                columns = new List<string>();
                tableColumns[table] = columns;
            }

            columns.Add(column);
        }

        return tableColumns;
    }

    private async Task<List<string>> SelectTablesFromMetadataAsync(string normalizedQuestion, HashSet<string> availableTables, DatabaseMetadata metadata)
    {
        var tokens = NormalizeTextForSearch(normalizedQuestion);
        if (tokens.Count == 0)
        {
            return new List<string>();
        }

        var tableColumns = await GetTableColumnsAsync();
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in availableTables)
        {
            scores[table] = 0;
            var tableName = table.ToLowerInvariant();
            var splitTable = SplitIdentifierToTokens(tableName);

            if (tokens.Overlaps(splitTable))
            {
                scores[table] += 10;
            }

            if (tableName.Contains("employee") && tokens.Contains("employee"))
            {
                scores[table] += 8;
            }

            if (tableName.Contains("payroll") && tokens.Contains("salary"))
            {
                scores[table] += 8;
            }

            if (tableName.Contains("attendance") && tokens.Contains("attendance"))
            {
                scores[table] += 8;
            }

            if (tableName.Contains("department") && tokens.Contains("department"))
            {
                scores[table] += 8;
            }

            if (tableColumns.TryGetValue(table, out var columns))
            {
                foreach (var column in columns)
                {
                    var columnTokens = SplitIdentifierToTokens(column.ToLowerInvariant());
                    if (tokens.Overlaps(columnTokens))
                    {
                        scores[table] += 5;
                    }
                }
            }

            if (AvailableDepartmentNameMatches(normalizedQuestion, metadata, table))
            {
                scores[table] += 6;
            }
        }

        foreach (var mapping in KeywordTableMappings)
        {
            if (tokens.Overlaps(mapping.KeywordTokens))
            {
                foreach (var table in mapping.TargetTables)
                {
                    if (availableTables.Contains(table))
                    {
                        scores[table] += 12;
                    }
                }
            }
        }

        return scores
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => kvp.Key)
            .Take(MaxSelectedTables)
            .ToList();
    }

    private static bool AvailableDepartmentNameMatches(string normalizedQuestion, DatabaseMetadata metadata, string table)
    {
        if (!string.Equals(table, "Departments", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tokens = NormalizeTextForSearch(normalizedQuestion);
        var departmentTable = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, "Departments", StringComparison.OrdinalIgnoreCase));
        if (departmentTable == null)
        {
            return false;
        }

        foreach (var row in departmentTable.SampleRows)
        {
            if (row.TryGetValue("DepartmentName", out var value) && value is string name && !string.IsNullOrWhiteSpace(name))
            {
                if (tokens.Contains(name.ToLowerInvariant()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly List<TableKeywordMapping> KeywordTableMappings = new()
    {
        new TableKeywordMapping(
            new HashSet<string>(new[] { "salary", "цалин", "payroll", "хөлс" }, StringComparer.OrdinalIgnoreCase),
            new[] { "Payroll", "Employees", "Departments" }),
        new TableKeywordMapping(
            new HashSet<string>(new[] { "attendance", "hotsrolt", "tardy", "late", "хоцрол" }, StringComparer.OrdinalIgnoreCase),
            new[] { "Attendance", "Employees", "Departments" }),
        new TableKeywordMapping(
            new HashSet<string>(new[] { "performance", "үнэлгээ", "score", "оноо" }, StringComparer.OrdinalIgnoreCase),
            new[] { "PerformanceReviews", "Employees", "Departments" }),
        new TableKeywordMapping(
            new HashSet<string>(new[] { "department", "heltes", "хэлтэс" }, StringComparer.OrdinalIgnoreCase),
            new[] { "Departments", "Employees", "Positions" }),
        new TableKeywordMapping(
            new HashSet<string>(new[] { "user", "login", "auth", "username", "password" }, StringComparer.OrdinalIgnoreCase),
            new[] { "Users", "Roles", "UserRoles" }),
    };

    private sealed record TableKeywordMapping(HashSet<string> KeywordTokens, string[] TargetTables);

    private static HashSet<string> NormalizeTextForSearch(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wordBuilder = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                wordBuilder.Append(char.ToLowerInvariant(ch));
            }
            else if (wordBuilder.Length > 0)
            {
                tokens.Add(wordBuilder.ToString());
                wordBuilder.Clear();
            }
        }

        if (wordBuilder.Length > 0)
        {
            tokens.Add(wordBuilder.ToString());
        }

        return tokens;
    }

    private static HashSet<string> SplitIdentifierToTokens(string identifier)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();

        foreach (var ch in identifier)
        {
            if (char.IsLetterOrDigit(ch))
            {
                token.Append(char.ToLowerInvariant(ch));
            }
            else if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            tokens.Add(token.ToString());
        }

        return tokens;
    }

    private async Task<IAsyncDisposable> OpenConnectionAsync()
    {
        var connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            return new ConnectionCloser(connection);
        }

        return new NoopAsyncDisposable();
    }

    private static string FormatSqlType(string dataType, string? precision, string? scale, string? length)
    {
        return dataType switch
        {
            "decimal" or "numeric" => $"{dataType}({precision},{scale})",
            "nvarchar" or "varchar" or "char" or "nchar" => $"{dataType}({length})",
            _ => dataType
        };
    }

    private static string EscapeSqlIdentifier(string value)
    {
        return value.Replace("]", "]]", StringComparison.Ordinal);
    }

    private static string FormatSampleValue(object value)
    {
        if (value == DBNull.Value)
        {
            return "NULL";
        }

        var text = Convert.ToString(value) ?? string.Empty;
        return text.Length <= 80 ? text : text[..80] + "...";
    }

    private sealed class ConnectionCloser : IAsyncDisposable
    {
        private readonly IDbConnection _connection;

        public ConnectionCloser(IDbConnection connection)
        {
            _connection = connection;
        }

        public ValueTask DisposeAsync()
        {
            _connection.Close();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

public class TableSelectionResult
{
    public IReadOnlyList<string> SelectedTables { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
}
