using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EmployeeSystem.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class UserListItemViewModel
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = "-";

    public bool IsActive { get; set; }

    public string Roles { get; set; } = "-";
}

public class UserEditViewModel
{
    public int? UserId { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public int? EmployeeId { get; set; }

    public bool IsActive { get; set; } = true;

    public List<int> SelectedRoleIds { get; set; } = new();

    public List<SelectListItem> Employees { get; set; } = new();

    public List<SelectListItem> Roles { get; set; } = new();
}

public class RoleListItemViewModel
{
    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public int UserCount { get; set; }
}
