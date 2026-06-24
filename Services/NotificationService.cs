using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public interface INotificationService
{
    Task NotifyLeaveApprovedAsync(string email, string name, DateTime startDate, DateTime endDate);
    Task NotifyLeaveRejectedAsync(string email, string name, DateTime startDate, string reason);
    Task NotifyReviewDueAsync(string managerEmail, string employeeName, DateTime dueDate);
    Task NotifyInterviewScheduledAsync(string candidateEmail, string candidateName, DateTime scheduledAt, string position);
}

public class NotificationService : INotificationService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IBackgroundTaskQueue taskQueue, IEmailSender emailSender, ILogger<NotificationService> logger)
    {
        _taskQueue = taskQueue;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task NotifyLeaveApprovedAsync(string email, string name, DateTime startDate, DateTime endDate)
    {
        var days = (endDate - startDate).Days;
        var subject = "Чөлөө баталагдлаа";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; direction: ltr;'>
            <h3 style='color: #2ecc71;'>Чөлөө баталагдлаа</h3>
            <p>Сайн байна уу {name},</p>
            <p>Таны чөлөө авах хүсэлт баталагдлаа:</p>
            <ul>
                <li><strong>Эхлэх өдөр:</strong> {startDate:yyyy-MM-dd}</li>
                <li><strong>Дуусах өдөр:</strong> {endDate:yyyy-MM-dd}</li>
                <li><strong>Хугацаа:</strong> {days} өдөр</li>
            </ul>
            <p>Баяр</p>
            <p><strong>HR Систем</strong></p>
        </body>
        </html>";

        await QueueEmailAsync(email, subject, body);
    }

    public async Task NotifyLeaveRejectedAsync(string email, string name, DateTime startDate, string reason)
    {
        var subject = "Чөлөө авах хүсэлт сэргээгдлээ";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; direction: ltr;'>
            <h3 style='color: #e74c3c;'>Чөлөө авах хүсэлт сэргээгдлээ</h3>
            <p>Сайн байна уу {name},</p>
            <p>{startDate:yyyy-MM-dd} өнөөс эхлэх таны чөлөө авах хүсэлт сэргээгдлээ.</p>
            <p><strong>Сэргээх шалтгаан:</strong> {reason}</p>
            <p>Дэлгэрэнгүй мэдээллийн хувьд HR хэлтэгтэй холбогдоно уу.</p>
            <p><strong>HR Систем</strong></p>
        </body>
        </html>";

        await QueueEmailAsync(email, subject, body);
    }

    public async Task NotifyReviewDueAsync(string managerEmail, string employeeName, DateTime dueDate)
    {
        var subject = "Гүйцэтгэл үнэлгээ хийх шаардлага";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; direction: ltr;'>
            <h3 style='color: #f39c12;'>Гүйцэтгэл үнэлгээ хийх шаардлага</h3>
            <p>Сайн байна уу,</p>
            <p><strong>{employeeName}</strong>-ийн гүйцэтгэл үнэлгээг хийх шаардлагатай байна.</p>
            <p><strong>Хэлэцэх хугацаа:</strong> {dueDate:yyyy-MM-dd}</p>
            <p>Түрүүлэн гүйцэтгүүлэх хүсүүлийг дүүргэнэ үү.</p>
            <p><strong>HR Систем</strong></p>
        </body>
        </html>";

        await QueueEmailAsync(managerEmail, subject, body);
    }

    public async Task NotifyInterviewScheduledAsync(string candidateEmail, string candidateName, DateTime scheduledAt, string position)
    {
        var subject = "Ярилцлагын сургалт";
        var body = $@"
        <html>
        <body style='font-family: Arial, sans-serif; direction: ltr;'>
            <h3 style='color: #3498db;'>Ярилцлага товлогдлоо</h3>
            <p>Сайн байна уу {candidateName},</p>
            <p><strong>{position}</strong> албан тушаалын үзүүлэлтийн ярилцлагад та таньсаа урих болж байна.</p>
            <p><strong>Цаг:</strong> {scheduledAt:yyyy-MM-dd HH:mm}</p>
            <p>Цаг нь нцэлэх нь чухал. Хэрэв та ирч чадахгүй бол HR хэлтэгтэй холбогдоно уу.</p>
            <p>Баярлалаа,<br/>
            <strong>Нэгдсэн компани</strong></p>
        </body>
        </html>";

        await QueueEmailAsync(candidateEmail, subject, body);
    }

    private async Task QueueEmailAsync(string to, string subject, string body)
    {
        try
        {
            await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                try
                {
                    await _emailSender.SendAsync(to, subject, body, isHtml: true, cancellationToken: token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending email notification to {Email}", to);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing email notification to {Email}", to);
        }
    }
}
