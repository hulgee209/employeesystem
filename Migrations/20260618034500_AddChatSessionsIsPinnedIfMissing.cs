using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeSystem.Migrations
{
    [DbContext(typeof(EmployeeDbContext))]
    [Migration("20260618034500_AddChatSessionsIsPinnedIfMissing")]
    public partial class AddChatSessionsIsPinnedIfMissing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.ChatSessions', 'IsPinned') IS NULL
BEGIN
    ALTER TABLE dbo.ChatSessions
    ADD [IsPinned] bit NOT NULL
        CONSTRAINT [DF_ChatSessions_IsPinned] DEFAULT CAST(0 AS bit);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.ChatSessions', 'IsPinned') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.default_constraints
        WHERE name = 'DF_ChatSessions_IsPinned'
            AND parent_object_id = OBJECT_ID('dbo.ChatSessions')
    )
    BEGIN
        ALTER TABLE dbo.ChatSessions DROP CONSTRAINT [DF_ChatSessions_IsPinned];
    END

    ALTER TABLE dbo.ChatSessions DROP COLUMN [IsPinned];
END
");
        }
    }
}
