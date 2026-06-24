using System;
using System.Collections.Generic;

namespace EmployeeSystem.Models;

public partial class Employee
{
    public int EmployeeId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public int DepartmentId { get; set; }

    public int PositionId { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateOnly? HireDate { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ManagerId { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual Position Position { get; set; } = null!;

    public virtual Employee? Manager { get; set; }

    public virtual ICollection<Employee> DirectReports { get; set; } = new List<Employee>();
}