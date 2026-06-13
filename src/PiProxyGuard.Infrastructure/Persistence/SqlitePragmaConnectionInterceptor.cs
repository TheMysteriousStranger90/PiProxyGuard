using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PiProxyGuard.Infrastructure.Persistence;

/// <summary>
/// Applies the SQLite PRAGMAs that make a single file safe to share between the
/// Worker (writer) and the API (reader) processes:
/// <list type="bullet">
///   <item><c>journal_mode=WAL</c> — Write-Ahead Logging lets readers keep
///   working against the last committed snapshot while a writer (e.g. the
///   ~85k-row blocklist refresh) holds the write lock, instead of failing with
///   "database is locked".</item>
///   <item><c>busy_timeout</c> — writers wait for each other up to N ms rather
///   than throwing immediately.</item>
///   <item><c>synchronous=NORMAL</c> — the recommended durability/throughput
///   trade-off under WAL.</item>
/// </list>
/// PRAGMAs are connection-scoped, so they are re-applied on every open. WAL is
/// a no-op on in-memory databases, so this is safe for the test fixtures too.
/// </summary>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>Shared, stateless instance.</summary>
    public static readonly SqlitePragmaConnectionInterceptor Instance = new();

    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
