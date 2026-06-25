using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Models;

public partial class EmployeeDbContext : DbContext
{
    public EmployeeDbContext()
    {
    }

    public EmployeeDbContext(DbContextOptions<EmployeeDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<Position> Positions { get; set; }

    public virtual DbSet<Attendance> Attendance { get; set; }

    public virtual DbSet<Payroll> Payroll { get; set; }

    public virtual DbSet<PerformanceReview> PerformanceReviews { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<ChatSession> ChatSessions { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var databaseProvider = Environment.GetEnvironmentVariable("DatabaseProvider");
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

            if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                optionsBuilder.UseNpgsql(connectionString);
            }
            else
            {
                optionsBuilder.UseSqlServer(connectionString ??
                    "Server=localhost\\SQLEXPRESS;Database=EmployeeDB;Trusted_Connection=True;TrustServerCertificate=True;");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var moneyColumnType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
        var netSalaryColumnType = isPostgres ? "numeric(20,2)" : "decimal(20,2)";
        var longTextColumnType = isPostgres ? "text" : "nvarchar(max)";

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("PK__Departme__B2079BEDDF3646AE");

            entity.Property(e => e.DepartmentName).HasMaxLength(100);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId).HasName("PK__Employee__7AD04F11BD5876B6");

            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.LastName).HasMaxLength(50);
            entity.Property(e => e.Phone).HasMaxLength(20);

            entity.HasOne(d => d.Department).WithMany(p => p.Employees)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Employees_Departments");

            entity.HasOne(d => d.Position).WithMany(p => p.Employees)
                .HasForeignKey(d => d.PositionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Employees_Positions");
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasKey(e => e.PositionId).HasName("PK__Position__60BB9A791A0014C9");

            entity.Property(e => e.PositionName).HasMaxLength(100);
        });

        modelBuilder.Entity<Attendance>(entity =>
        {
            entity.ToTable("Attendance");
            entity.HasKey(e => e.AttendanceId);

            entity.Property(e => e.Status).HasMaxLength(20);

            entity.HasIndex(e => new { e.EmployeeId, e.AttendanceDate });
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payroll>(entity =>
        {
            entity.ToTable("Payroll");
            entity.HasKey(e => e.PayrollId);

            entity.Property(e => e.PayrollMonth)
                .HasColumnName("PayMonth");
            entity.Property(e => e.BaseSalary)
                .HasColumnName("Salary")
                .HasColumnType(moneyColumnType);
            entity.Property(e => e.Bonus)
                .HasColumnType(moneyColumnType);
            entity.Property(e => e.Deductions)
                .HasColumnName("Deduction")
                .HasColumnType(moneyColumnType);
            entity.Property(e => e.NetSalary)
                .HasColumnType(netSalaryColumnType);

            entity.HasIndex(e => new { e.EmployeeId, e.PayrollMonth });

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PerformanceReview>(entity =>
        {
            entity.HasKey(e => e.ReviewId);

            entity.Property(e => e.Score);
            entity.Property(e => e.Comments).HasMaxLength(2000);

            entity.HasIndex(e => new { e.EmployeeId, e.ReviewDate });

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.EmployeeId).IsUnique()
                .HasFilter(isPostgres ? "\"EmployeeId\" IS NOT NULL" : "[EmployeeId] IS NOT NULL");

            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId);

            entity.HasIndex(e => e.RoleName).IsUnique();
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.UserRoleId);

            entity.HasIndex(e => new { e.UserId, e.RoleId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(e => e.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany(e => e.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.TableName, e.RecordId });
            entity.HasIndex(e => e.CreatedAt).IsDescending();
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.Action).HasMaxLength(20);
            entity.Property(e => e.TableName).HasMaxLength(100);
            entity.Property(e => e.OldValues).HasColumnType(longTextColumnType);
            entity.Property(e => e.NewValues).HasColumnType(longTextColumnType);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.IsPinned).HasDefaultValue(false);
            entity.HasIndex(e => new { e.UserId, e.LastMessageAt }).IsDescending();
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.Content).HasColumnType(longTextColumnType);
            entity.Property(e => e.GeneratedSql).HasColumnType(longTextColumnType);
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt });
            entity.HasOne<ChatSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
