using System;

namespace EmployeeSystem.Models;

public partial class PerformanceReview
{
    public int ReviewId { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly? ReviewDate { get; set; }

    public int? Score { get; set; }

    public string? Comments { get; set; }

    public virtual Employee Employee { get; set; } = null!;
}
