using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Sends notifications by e-mail over SMTP. Host, credentials and addresses come
/// from the runtime <see cref="INotificationSettingsStore"/> (editable in the
/// dashboard). Disabled (no-op) until enabled with at least a host, sender and
/// recipient. Never throws.
/// </summary>
public class EmailNotificationSender : INotificationSender
{
    private readonly INotificationSettingsStore _settings;
    private readonly ILogger<EmailNotificationSender> _logger;

    public EmailNotificationSender(
        INotificationSettingsStore settings,
        ILogger<EmailNotificationSender> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string Channel => "Email";

    public bool IsEnabled => _settings.Current.EmailConfigured;

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var snapshot = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.EmailConfigured)
        {
            return false;
        }

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(snapshot.EmailFrom!),
                Subject = $"[PiProxyGuard] {message.Title}",
                Body = message.Body
            };

            foreach (var recipient in snapshot.EmailTo!.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                mail.To.Add(recipient);
            }

            using var client = new SmtpClient(snapshot.EmailHost, snapshot.EmailPort)
            {
                EnableSsl = snapshot.EmailUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrEmpty(snapshot.EmailUsername))
            {
                client.Credentials = new NetworkCredential(snapshot.EmailUsername, snapshot.EmailPassword);
            }

            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "E-mail notification failed");
            return false;
        }
    }
}
