using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Sends notifications through the Telegram Bot API. The bot token and chat id
/// come from the runtime <see cref="INotificationSettingsStore"/> (editable in
/// the dashboard), so the channel can be turned on, off or re-pointed without a
/// restart. Disabled (no-op) until enabled with a token and chat id. Never
/// throws — failures are logged and reported via the return value.
/// </summary>
public class TelegramNotificationSender : INotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationSettingsStore _settings;
    private readonly ILogger<TelegramNotificationSender> _logger;

    public TelegramNotificationSender(
        IHttpClientFactory httpClientFactory,
        INotificationSettingsStore settings,
        ILogger<TelegramNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public string Channel => "Telegram";

    public bool IsEnabled => _settings.Current.TelegramConfigured;

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var snapshot = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.TelegramConfigured)
        {
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("notifications");
            var url = $"https://api.telegram.org/bot{snapshot.TelegramBotToken}/sendMessage";
            var text = $"*{Escape(message.Title)}*\n{Escape(message.Body)}";

            var payload = new
            {
                chat_id = snapshot.TelegramChatId,
                text,
                parse_mode = "Markdown",
                disable_web_page_preview = true
            };

            using var response = await client.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning("Telegram notification failed with status {Status}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram notification failed");
            return false;
        }
    }

    // Telegram Markdown treats these as control characters.
    private static string Escape(string value) =>
        value.Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
}
