namespace EmployeeSystem.Models;

public class ChatSession
{
    public int SessionId { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public bool IsPinned { get; set; }
}

public class ChatMessage
{
    public int MessageId { get; set; }
    public int SessionId { get; set; }
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public string? GeneratedSql { get; set; }
    public int ExecutionMs { get; set; } // Query execution time in milliseconds
    public DateTime CreatedAt { get; set; }
}
