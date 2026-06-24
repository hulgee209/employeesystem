using System;

namespace EmployeeSystem.Models;

public partial class Payroll
{
    public int PayrollId { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly? PayrollMonth { get; set; }

    public decimal? BaseSalary { get; set; }

    public decimal? Bonus { get; set; }

    public decimal? Deductions { get; set; }

    public decimal? NetSalary { get; set; }

    public virtual Employee Employee { get; set; } = null!;
}
