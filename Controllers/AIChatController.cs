using EmployeeSystem.Models;
using EmployeeSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace EmployeeSystem.Controllers;

[Authorize(Roles = "Admin,HR,Manager,Employee")]
[Route("[controller]/[action]")]
public class AIChatController : Controller
{
    private readonly IAiSqlAgentService _aiSqlAgentService;
    private readonly EmployeeDbContext _context;

    public AIChatController(IAiSqlAgentService aiSqlAgentService, EmployeeDbContext context)
    {
        _aiSqlAgentService = aiSqlAgentService;
        _context = context;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AIChatQuestionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new AIChatAnswerResponse(
                "Асуултаа бичээд дахин илгээнэ үү.", null));
        }

        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        try
        {
            // Get or create session
            ChatSession? session = null;
            if (request.SessionId.HasValue && request.SessionId.Value > 0)
            {
                session = await _context.ChatSessions
                    .FirstOrDefaultAsync(s => s.SessionId == request.SessionId.Value && s.UserId == userId);
                
                if (session == null)
                    return BadRequest(new AIChatAnswerResponse("Session not found", null));
            }
            else
            {
                // Create new session
                session = new ChatSession
                {
                    UserId = userId,
                    Title = request.Question.Length > 80 ? request.Question[..80] : request.Question,
                    CreatedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                _context.ChatSessions.Add(session);
                await _context.SaveChangesAsync();
            }

            // Save user message
            var userMessage = new ChatMessage
            {
                SessionId = session.SessionId,
                Role = "user",
                Content = request.Question,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(userMessage);
            await _context.SaveChangesAsync();

            // Call AI with timing
            var stopwatch = Stopwatch.StartNew();
            var result = await _aiSqlAgentService.AnswerAsync(User, request.Question.Trim());
            stopwatch.Stop();

            // Save AI response
            var aiMessage = new ChatMessage
            {
                SessionId = session.SessionId,
                Role = "assistant",
                Content = result.Analysis,
                GeneratedSql = result.Sql,
                ExecutionMs = (int)stopwatch.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(aiMessage);

            // Update session's last message time
            session.LastMessageAt = DateTime.UtcNow;
            _context.ChatSessions.Update(session);
            await _context.SaveChangesAsync();

            return Ok(new AIChatAnswerResponse(result.Analysis, session.SessionId));
        }
        
        catch (AiSqlAgentService.AiSqlAgentQuotaException)
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new AIChatAnswerResponse(
                    "Gemini API quota/resource_exhausted хэтэрсэн байна. Түр хүлээгээд дахин оролдоно уу.", null));
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new AIChatAnswerResponse(
                    "FastAPI/Gemini үйлчилгээтэй холбогдож чадсангүй. `uvicorn main:app --reload` ажиллаж байгаа эсэхийг шалгана уу.", null));
        }
        catch (TaskCanceledException)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new AIChatAnswerResponse(
                    "AI үйлчилгээ хариу өгөхөд удаан байна. Түр хүлээгээд дахин оролдоно уу.", null));
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new AIChatAnswerResponse(
                    "AI Copilot ажиллуулах үед алдаа гарлаа. " +
                    $"Дэлгэрэнгүй: {TrimForDisplay(ex.Message)}", null));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Sessions()
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var sessions = await _context.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.LastMessageAt)
            .Take(20)
            .Select(s => new SessionSummary(
                s.SessionId,
                s.Title,
                s.CreatedAt,
                s.LastMessageAt,
                s.IsPinned
            ))
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Session(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var session = await _context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == id && s.UserId == userId);

        if (session == null)
            return NotFound();

        var messages = await _context.ChatMessages
            .Where(m => m.SessionId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageSummary(
                m.MessageId,
                m.Role,
                m.Content,
                m.GeneratedSql,
                m.ExecutionMs,
                m.CreatedAt
            ))
            .ToListAsync();

        return Ok(new SessionDetail(session.SessionId, session.Title, session.CreatedAt, session.IsPinned, messages));
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetSession(int id)
    {
        return Session(id);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var session = await _context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == id && s.UserId == userId);

        if (session == null)
            return NotFound();

        _context.ChatSessions.Remove(session);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}")]
    public async Task<IActionResult> PinSession(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var session = await _context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == id && s.UserId == userId);

        if (session == null)
            return NotFound();

        session.IsPinned = !session.IsPinned;
        await _context.SaveChangesAsync();

        return Ok(new { session.SessionId, session.IsPinned });
    }

    private int GetCurrentUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdStr, out var userId) ? userId : 0;
    }

    private static string TrimForDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Мэдээлэл байхгүй.";
        }

        const int maxLength = 700;

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    public record AIChatQuestionRequest(string Question, int? SessionId = null);

    public record AIChatAnswerResponse(string Answer, int? SessionId);

    public record SessionSummary(int SessionId, string Title, DateTime CreatedAt, DateTime LastMessageAt, bool IsPinned);

    public record MessageSummary(int MessageId, string Role, string Content, string? GeneratedSql, int ExecutionMs, DateTime CreatedAt);

    public record SessionDetail(int SessionId, string Title, DateTime CreatedAt, bool IsPinned, List<MessageSummary> Messages);
}
