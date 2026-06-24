using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmployeeSystem.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var smtpHost = _configuration["Smtp:Host"];
            var smtpPort = int.Parse(_configuration["Smtp:Port"] ?? "587");
            var smtpUser = _configuration["Smtp:User"] ?? "no-reply@company.com";
            var smtpPass = _configuration["Smtp:Pass"] ?? string.Empty;
            var fromEmail = _configuration["Smtp:From"] ?? smtpUser;
            var enableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "true");

            if (string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogWarning("SMTP configuration is missing. Email not sent to {To}", to);
                return;
            }

            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.EnableSsl = enableSsl;
                client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                client.Timeout = 10000;

                using (var message = new MailMessage(fromEmail, to, subject, body))
                {
                    message.IsBodyHtml = isHtml;
                    
                    await client.SendMailAsync(message, cancellationToken);
                    
                    _logger.LogInformation("Email sent successfully to {To}", to);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}. Subject: {Subject}", to, subject);
            throw;
        }
    }
}
