namespace EmployeeSystem.Models;

public class AuditLog
{
    public int AuditLogId { get; set; }
    
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    
    public string Action { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, READ
    public string TableName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    
    public string? OldValues { get; set; } // JSON of old state
    public string? NewValues { get; set; } // JSON of new state
    
    public string IpAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
