using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Sends notifications by e-mail over SMTP. Disabled (no-op) until at least a
/// host, sender and recipient are configured. Never throws.
/// </summary>
public class EmailNotificationSender : INotificationSender
{
    private readonly EmailNotificationOptions _options;
    private readonly ILogger<EmailNotificationSender> _logger;

    public EmailNotificationSender(
        IOptions<NotificationOptions> options,
        ILogger<EmailNotificationSender> logger)
    {
        _options = options.Value.Email;
        _logger = logger;
    }

    public string Channel => "Email";

    public bool IsEnabled => _options.Enabled;

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(_options.From!),
                Subject = $"[PiProxyGuard] {message.Title}",
                Body = message.Body
            };

            foreach (var recipient in _options.To!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                mail.To.Add(recipient);
            }

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrEmpty(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "E-mail notification failed");
            return false;
        }
    }
}
