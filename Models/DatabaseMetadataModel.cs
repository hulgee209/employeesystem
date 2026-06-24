using System.Collections.Generic;

namespace EmployeeSystem.Models;

public class DatabaseMetadata
{
    public IReadOnlyList<TableMetadata> Tables { get; init; } = Array.Empty<TableMetadata>();
}

public class TableMetadata
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ColumnMetadata> Columns { get; init; } = Array.Empty<ColumnMetadata>();
    public IReadOnlyList<ForeignKeyMetadata> ForeignKeys { get; init; } = Array.Empty<ForeignKeyMetadata>();
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows { get; init; } = Array.Empty<IReadOnlyDictionary<string, object?>>();
}

public class ColumnMetadata
{
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
}

public class ForeignKeyMetadata
{
    public string Name { get; init; } = string.Empty;
    public string ParentColumn { get; init; } = string.Empty;
    public string ReferencedTable { get; init; } = string.Empty;
    public string ReferencedColumn { get; init; } = string.Empty;
}
