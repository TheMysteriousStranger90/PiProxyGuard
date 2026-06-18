using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Reads and updates the runtime notification settings (Telegram and e-mail)
/// and sends test messages — the REST equivalent of the dashboard Settings page.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationSettingsStore _store;
    private readonly IReadOnlyList<INotificationSender> _senders;

    public NotificationsController(
        INotificationSettingsStore store,
        IEnumerable<INotificationSender> senders)
    {
        _store = store;
        _senders = senders.ToList();
    }

    /// <summary>Returns the current notification settings (secrets masked).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var snapshot = await _store.GetAsync(cancellationToken);
        return ToDto(snapshot);
    }

    /// <summary>Replaces the notification settings. Blank secrets keep the stored value.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateSettings(
        [FromBody] UpdateNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await _store.GetAsync(cancellationToken);

        if (!Enum.TryParse<NotificationSeverity>(request.MinimumSeverity, true, out var severity))
        {
            return BadRequest(new
                { error = $"'{request.MinimumSeverity}' is not a valid severity (Info, Warning, Critical)." });
        }

        var token = string.IsNullOrWhiteSpace(request.TelegramBotToken)
            ? current.TelegramBotToken
            : request.TelegramBotToken.Trim();
        var password = string.IsNullOrWhiteSpace(request.EmailPassword)
            ? current.EmailPassword
            : request.EmailPassword.Trim();

        if (request.TelegramEnabled &&
            (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(request.TelegramChatId)))
        {
            return BadRequest(new { error = "Telegram needs a bot token and a chat id." });
        }

        if (request.EmailEnabled &&
            (string.IsNullOrWhiteSpace(request.EmailHost) ||
             string.IsNullOrWhiteSpace(request.EmailFrom) ||
             string.IsNullOrWhiteSpace(request.EmailTo)))
        {
            return BadRequest(new { error = "E-mail needs a host, a from address and at least one recipient." });
        }

        var snapshot = new NotificationSettingsSnapshot(
            severity,
            request.TelegramEnabled,
            token,
            request.TelegramChatId,
            request.EmailEnabled,
            request.EmailHost,
            request.EmailPort > 0 ? request.EmailPort : 587,
            request.EmailUseSsl,
            request.EmailUsername,
            password,
            request.EmailFrom,
            request.EmailTo);

        await _store.SaveAsync(snapshot, cancellationToken);

        var saved = await _store.GetAsync(cancellationToken);
        return ToDto(saved);
    }

    /// <summary>Sends a test message through one channel using the saved settings.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(
        [FromBody] SendTestNotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sender = _senders.FirstOrDefault(s =>
            string.Equals(s.Channel, request.Channel, StringComparison.OrdinalIgnoreCase));

        if (sender is null)
        {
            return BadRequest(new { error = $"Unknown channel '{request.Channel}'." });
        }

        if (!sender.IsEnabled)
        {
            return BadRequest(new { error = $"{sender.Channel} is not enabled and configured." });
        }

        var message = new NotificationMessage(
            "Test notification",
            $"This is a PiProxyGuard test message sent at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.",
            NotificationSeverity.Critical);

        var delivered = await sender.SendAsync(message, cancellationToken);
        return delivered
            ? Ok(new { status = "sent", channel = sender.Channel })
            : StatusCode(StatusCodes.Status502BadGateway,
                new { error = $"{sender.Channel} test failed — check the credentials and the server logs." });
    }

    private static NotificationSettingsDto ToDto(NotificationSettingsSnapshot s) => new(
        s.MinimumSeverity.ToString(),
        s.TelegramEnabled,
        !string.IsNullOrWhiteSpace(s.TelegramBotToken),
        Mask(s.TelegramBotToken),
        s.TelegramChatId,
        s.EmailEnabled,
        s.EmailHost,
        s.EmailPort,
        s.EmailUseSsl,
        s.EmailUsername,
        !string.IsNullOrWhiteSpace(s.EmailPassword),
        s.EmailFrom,
        s.EmailTo);

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var trimmed = secret.Trim();
        var tail = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        return $"\u2022\u2022\u2022{tail}";
    }
}
