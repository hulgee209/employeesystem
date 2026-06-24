using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeSystem.Migrations
{
    /// <inheritdoc />
    public partial class PendingAuthEmployeeSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column (guarded)
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Employees', 'ManagerId') IS NULL
BEGIN
    ALTER TABLE dbo.Employees ADD [ManagerId] int NULL;
END
");

            // Index (guarded)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_ManagerId' AND object_id = OBJECT_ID('dbo.Employees'))
BEGIN
    CREATE INDEX [IX_Employees_ManagerId] ON dbo.Employees ([ManagerId]);
END
");

            // Self-FK (guarded) - avoid SQL Server multiple cascade path/cycle issues
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_Employees_Employees_ManagerId'
)
BEGIN
    ALTER TABLE dbo.Employees
    ADD CONSTRAINT [FK_Employees_Employees_ManagerId]
    FOREIGN KEY ([ManagerId]) REFERENCES dbo.Employees ([EmployeeId])
    ON DELETE NO ACTION;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Employees_Employees_ManagerId')
BEGIN
    ALTER TABLE dbo.Employees DROP CONSTRAINT [FK_Employees_Employees_ManagerId];
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_ManagerId' AND object_id = OBJECT_ID('dbo.Employees'))
BEGIN
    DROP INDEX [IX_Employees_ManagerId] ON dbo.Employees;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Employees', 'ManagerId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Employees DROP COLUMN [ManagerId];
END
");
        }
    }
}
