using BasicApi.Storage.Interfaces;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BasicApi.Services;

/// <summary>
/// Готовность к работе: без базы приложение бесполезно, поэтому /health/ready
/// падает вместе с ней. Детали ошибки в ответ не попадают — только в лог.
/// </summary>
public sealed class PostgresHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = connectionFactory.CreateConnection();
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Postgres is unreachable", ex);
        }
    }
}
