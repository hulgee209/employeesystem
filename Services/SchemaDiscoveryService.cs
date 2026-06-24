using System.Data;
using System.Data.Common;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Services;

public class SchemaDiscoveryService : ISchemaDiscoveryService
{
    private readonly EmployeeDbContext _context;

    public SchemaDiscoveryService(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<DatabaseMetadata> DiscoverAsync()
    {
        var tables = new List<TableMetadata>();
        var connection = _context.Database.GetDbConnection();

        await using var _ = await OpenConnectionAsync();

        // Get all user tables and schemas
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.name AS SchemaName, t.name AS TableName
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name";

            var reader = await cmd.ExecuteReaderAsync();
            var tableNames = new List<(string Schema, string Name)>();

            while (await reader.ReadAsync())
            {
                tableNames.Add((reader.GetString(0), reader.GetString(1)));
            }

            await reader.CloseAsync();

            foreach (var (schema, name) in tableNames)
            {
                var columns = await LoadColumnsAsync(connection, schema, name);
                var fks = await LoadForeignKeysAsync(connection, schema, name);
                var samples = await LoadSampleRowsAsync(connection, schema, name, columns);

                tables.Add(new TableMetadata
                {
                    Schema = schema,
                    Name = name,
                    Columns = columns,
                    ForeignKeys = fks,
                    SampleRows = samples
                });
            }
        }

        return new DatabaseMetadata { Tables = tables };
    }

    private async Task<List<ColumnMetadata>> LoadColumnsAsync(DbConnection connection, string schema, string table)
    {
        var result = new List<ColumnMetadata>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = schema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new ColumnMetadata
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)
            });
        }

        return result;
    }

    private async Task<List<ForeignKeyMetadata>> LoadForeignKeysAsync(DbConnection connection, string schema, string table)
    {
        var result = new List<ForeignKeyMetadata>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT fk.name AS FKName, cp.name AS ParentColumn, tr.name AS RefTable, cr.name AS RefColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
            JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
            JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
            JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
            JOIN sys.schemas sp ON tp.schema_id = sp.schema_id
            WHERE sp.name = @schema AND tp.name = @table";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "@schema"; p1.Value = schema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@table"; p2.Value = table; cmd.Parameters.Add(p2);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new ForeignKeyMetadata
            {
                Name = reader.GetString(0),
                ParentColumn = reader.GetString(1),
                ReferencedTable = reader.GetString(2),
                ReferencedColumn = reader.GetString(3)
            });
        }

        return result;
    }

    private async Task<List<IReadOnlyDictionary<string, object?>>> LoadSampleRowsAsync(DbConnection connection, string schema, string table, List<ColumnMetadata> columns)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        if (columns.Count == 0) return rows;

        var columnList = string.Join(",", columns.Select(c => "[" + c.Name + "]"));

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT TOP 5 {columnList} FROM [{schema}].[{table}]";
        cmd.CommandTimeout = 30;

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            foreach (var col in columns)
            {
                var val = reader[col.Name];
                row[col.Name] = val == DBNull.Value ? null : val;
            }

            rows.Add(row);
        }

        return rows;
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
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
