using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Eudoria.Core.Api.Health;

/// <summary>Checks that PostgreSQL answers a trivial query within a short delay.</summary>
internal sealed partial class DatabaseHealthCheck(NpgsqlDataSource dataSource, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MaxDuration);

        try
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(timeout.Token);
            await using NpgsqlCommand command = new("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Details stay in the server logs; the HTTP response never exposes them.
            LogDatabaseUnavailable(logger, exception);
            return HealthCheckResult.Unhealthy();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database health check failed")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception);
}
