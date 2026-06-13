using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Sends notifications through the Telegram Bot API. Disabled (no-op) until a
/// bot token and chat id are configured. Never throws — failures are logged
/// and reported via the return value.
/// </summary>
public class TelegramNotificationSender : INotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<TelegramNotificationSender> _logger;

    public TelegramNotificationSender(
        IHttpClientFactory httpClientFactory,
        IOptions<NotificationOptions> options,
        ILogger<TelegramNotificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Telegram;
        _logger = logger;
    }

    public string Channel => "Telegram";

    public bool IsEnabled => _options.Enabled;

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("notifications");
            var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
            var text = $"*{Escape(message.Title)}*\n{Escape(message.Body)}";

            var payload = new
            {
                chat_id = _options.ChatId,
                text,
                parse_mode = "Markdown",
                disable_web_page_preview = true
            };

            using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
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
