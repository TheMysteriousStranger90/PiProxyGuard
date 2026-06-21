using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Database-backed <see cref="INotificationSettingsStore"/>. Keeps the single
/// settings row cached so the (singleton) notification channels can read it
/// cheaply, refreshes it on a short TTL so changes made in the dashboard reach
/// the separate Worker process, and falls back to the <c>Notifications</c>
/// configuration section when no row has been saved yet.
/// </summary>
public sealed class NotificationSettingsStore : INotificationSettingsStore, IDisposable
{
    /// <summary>Primary key of the single settings row.</summary>
    public const long SingletonId = 1;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(8);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationSettingsSnapshot _fallback;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NotificationSettingsSnapshot? _cached;
    private DateTime _cachedAtUtc;

    public NotificationSettingsStore(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> fallbackOptions)
    {
        _scopeFactory = scopeFactory;
        _fallback = BuildFallback(fallbackOptions.Value);
    }

    public NotificationSettingsSnapshot Current => _cached ?? _fallback;

    public async Task<NotificationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cached;
        if (cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return _cached;
            }

            var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _cached = snapshot;
            _cachedAtUtc = DateTime.UtcNow;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(NotificationSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await dbContext.NotificationSettings
                .FirstOrDefaultAsync(s => s.Id == SingletonId, cancellationToken)
                .ConfigureAwait(false);

            if (row is null)
            {
                row = new NotificationSetting { Id = SingletonId };
                dbContext.NotificationSettings.Add(row);
            }

            row.MinimumSeverity = snapshot.MinimumSeverity;

            row.TelegramEnabled = snapshot.TelegramEnabled;
            row.TelegramBotToken = NullIfBlank(snapshot.TelegramBotToken);
            row.TelegramChatId = NullIfBlank(snapshot.TelegramChatId);

            row.EmailEnabled = snapshot.EmailEnabled;
            row.EmailHost = NullIfBlank(snapshot.EmailHost);
            row.EmailPort = snapshot.EmailPort > 0 ? snapshot.EmailPort : 587;
            row.EmailUseSsl = snapshot.EmailUseSsl;
            row.EmailUsername = NullIfBlank(snapshot.EmailUsername);
            row.EmailPassword = NullIfBlank(snapshot.EmailPassword);
            row.EmailFrom = NullIfBlank(snapshot.EmailFrom);
            row.EmailTo = NullIfBlank(snapshot.EmailTo);

            row.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _cached = ToSnapshot(row);
            _cachedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<NotificationSettingsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await dbContext.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SingletonId, cancellationToken)
            .ConfigureAwait(false);

        return row is not null ? ToSnapshot(row) : _fallback;
    }

    private static NotificationSettingsSnapshot ToSnapshot(NotificationSetting row) => new(
        row.MinimumSeverity,
        row.TelegramEnabled,
        NullIfBlank(row.TelegramBotToken),
        NullIfBlank(row.TelegramChatId),
        row.EmailEnabled,
        NullIfBlank(row.EmailHost),
        row.EmailPort > 0 ? row.EmailPort : 587,
        row.EmailUseSsl,
        NullIfBlank(row.EmailUsername),
        NullIfBlank(row.EmailPassword),
        NullIfBlank(row.EmailFrom),
        NullIfBlank(row.EmailTo));

    private static NotificationSettingsSnapshot BuildFallback(NotificationOptions options)
    {
        var telegram = options.Telegram;
        var telegramConfigured = !string.IsNullOrWhiteSpace(telegram.BotToken)
                                 && !string.IsNullOrWhiteSpace(telegram.ChatId);

        var email = options.Email;
        var emailConfigured = !string.IsNullOrWhiteSpace(email.Host)
                              && !string.IsNullOrWhiteSpace(email.From)
                              && !string.IsNullOrWhiteSpace(email.To);

        return new NotificationSettingsSnapshot(
            (NotificationSeverity)(int)options.MinimumSeverity,
            telegramConfigured,
            NullIfBlank(telegram.BotToken),
            NullIfBlank(telegram.ChatId),
            emailConfigured,
            NullIfBlank(email.Host),
            email.Port > 0 ? email.Port : 587,
            email.UseSsl,
            NullIfBlank(email.Username),
            NullIfBlank(email.Password),
            NullIfBlank(email.From),
            NullIfBlank(email.To));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _gate.Dispose();
}
