using Microsoft.AspNetCore.Components;
using PiProxyGuard.Web.Realtime;
using PiProxyGuard.Web.Services;

namespace PiProxyGuard.Web.Components;

/// <summary>
/// Base for dashboard pages that show live data. Subscribes to the
/// <see cref="LiveUpdateNotifier"/> heartbeat and re-runs <see cref="LoadAsync"/>
/// on every tick (skipping overlapping reloads), so pages stay current without
/// their own timers.
/// </summary>
public abstract class LivePageComponent : ComponentBase, IDisposable
{
    private bool _refreshing;
    private bool _disposed;

    [Inject] protected LiveUpdateNotifier Notifier { get; set; } = default!;

    [Inject] protected DashboardService Dashboard { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Notifier.DataChanged += OnDataChanged;
        await LoadAsync();
    }

    /// <summary>Loads the data the page renders. Called on init and every heartbeat.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Reloads immediately (e.g. after a user action) and re-renders.</summary>
    protected async Task ReloadAsync()
    {
        await LoadAsync();
        StateHasChanged();
    }

    private void OnDataChanged()
    {
        _ = InvokeAsync(async () =>
        {
            if (_refreshing)
            {
                return;
            }

            _refreshing = true;
            try
            {
                await LoadAsync();
                StateHasChanged();
            }
            finally
            {
                _refreshing = false;
            }
        });
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Notifier.DataChanged -= OnDataChanged;
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
