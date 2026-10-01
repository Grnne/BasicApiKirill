using System.Net;
using BasicApi.IntegrationTests.Infrastructure;
using Npgsql;

namespace BasicApi.IntegrationTests.Platform;

[Collection(PostgresCollection.Name)]
public class HealthCheckTests(PostgresFixture db)
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithDatabase_ReturnsOk()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_WhenDatabaseUnreachable_Returns503_AndLiveStillOk()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient(); // app startup - with a live database (migrations)

        // Take the database "down" by swapping the password: the connection pool will not help, new ones will not open.
        NpgsqlConnection.ClearAllPools();
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "ALTER ROLE CURRENT_USER WITH PASSWORD 'temporarily-wrong'", connection);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnection.ClearAllPools();

            var ready = await client.GetAsync("/health/ready");
            var live = await client.GetAsync("/health/live");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            var builder = new NpgsqlConnectionStringBuilder(db.ConnectionString);
            var wrong = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Password = "temporarily-wrong" };
            await using var connection = new NpgsqlConnection(wrong.ToString());
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"ALTER ROLE CURRENT_USER WITH PASSWORD '{builder.Password}'", connection);
            await cmd.ExecuteNonQueryAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task HealthEndpoints_AreNotRateLimited()
    {
        // Compose/load-balancer checks must not eat the limit and get 429.
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        for (var i = 0; i < 80; i++)
        {
            var response = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
