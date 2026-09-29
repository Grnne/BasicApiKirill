using BasicApi.Storage.Interfaces;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BasicApi.Services;

/// <summary>
/// Readiness: without the database the application is useless, so /health/ready
/// fails together with it. Error details do not go into the response — only into the log.
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
