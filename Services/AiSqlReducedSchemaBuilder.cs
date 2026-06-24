using System.Text;
using EmployeeSystem.Models;

namespace EmployeeSystem.Services;

public static class AiSqlReducedSchemaBuilder
{
    public static string BuildReducedSchemaContext(DatabaseMetadata dbSchema, IReadOnlyList<string> selectedTables)
    {
        // selectedTables are expected in format "Schema.Table".
        var selectedSet = selectedTables
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("DATABASE SCHEMA (REDUCED)");
        sb.AppendLine("Use only these tables and columns. Generate SQL Server SELECT queries only.");

        foreach (var table in dbSchema.Tables
                     .Where(t => selectedSet.Contains($"{t.Schema}.{t.Name}"))
                     .OrderBy(t => t.Schema).ThenBy(t => t.Name))
        {
            sb.AppendLine();
            sb.AppendLine($"{table.Schema}.{table.Name}:");

            foreach (var col in table.Columns)
            {
                sb.AppendLine($"- {col.Name} ({col.DataType}) Nullable: {col.IsNullable}");
            }

            if (table.ForeignKeys?.Any() == true)
            {
                sb.AppendLine("Foreign keys:");
                foreach (var fk in table.ForeignKeys)
                {
                    sb.AppendLine($"- {fk.Name}: {fk.ParentColumn} -> {fk.ReferencedTable}.{fk.ReferencedColumn}");
                }
            }

            if (table.SampleRows?.Any() == true)
            {
                sb.AppendLine("Sample values (from TOP 5):");

                // Collect sample values per column (avoid dumping whole rows).
                foreach (var col in table.Columns)
                {
                    var samples = table.SampleRows
                        .Select(r => r.TryGetValue(col.Name, out var v) ? v?.ToString() : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .Take(5)
                        .ToList();

                    if (samples.Count == 0) continue;

                    sb.AppendLine($"- {col.Name}:");
                    foreach (var s in samples)
                    {
                        sb.AppendLine($"  - {s}");
                    }
                }
            }
        }

        return sb.ToString();
    }
}

