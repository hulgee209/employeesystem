using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeSystem.Migrations
{
    [DbContext(typeof(EmployeeDbContext))]
    [Migration("20260618033000_AddEmployeesIsActiveIfMissing")]
    public partial class AddEmployeesIsActiveIfMissing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Employees', 'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.Employees
    ADD [IsActive] bit NOT NULL
        CONSTRAINT [DF_Employees_IsActive] DEFAULT CAST(1 AS bit);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Employees', 'IsActive') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.default_constraints
        WHERE name = 'DF_Employees_IsActive'
            AND parent_object_id = OBJECT_ID('dbo.Employees')
    )
    BEGIN
        ALTER TABLE dbo.Employees DROP CONSTRAINT [DF_Employees_IsActive];
    END

    ALTER TABLE dbo.Employees DROP COLUMN [IsActive];
END
");
        }
    }
}
