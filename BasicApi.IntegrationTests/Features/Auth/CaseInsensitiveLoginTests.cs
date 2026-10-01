using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Auth;

public class CaseInsensitiveLoginTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task Register_SameUsernameInOtherCase_Returns409()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        await factory.RegisterAsync("Alice");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { username = "alice", email = "other@test.local", password = ApiClient.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("USERNAME_TAKEN", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_SameEmailInOtherCase_Returns409()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        await factory.RegisterAsync("alice", "Alice@Test.Local");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { username = "bob", email = "alice@test.local", password = ApiClient.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("EMAIL_TAKEN", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_IgnoresCaseAndSurroundingSpaces_OfUsernameAndEmail()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("Alice", "Alice@Test.Local");

        Assert.Equal(alice.UserId, (await factory.LoginAsync("ALICE")).UserId);
        Assert.Equal(alice.UserId, (await factory.LoginAsync(" alice@test.local ")).UserId);
    }

    [Fact]
    public async Task ParallelRegistrations_DifferingOnlyInCase_OnlyOneSucceeds()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(new[] { "Carol", "carol", "CAROL" }.Select(name =>
            client.PostAsJsonAsync("/api/auth/register",
                new { username = name, email = $"{name}-{Guid.NewGuid():N}@test.local", password = ApiClient.Password })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }
}

[Collection(PostgresCollection.Name)]
public class NormalizedUserKeysMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_WithCaseConflicts_FailsWithReport_AndChangesNothing()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("normalized_keys_conflict");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(6));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(@"
            INSERT INTO users (id, username, email, password_hash, display_name) VALUES
                (gen_random_uuid(), 'Bob', 'bob1@test.local', 'x', 'Bob'),
                (gen_random_uuid(), 'bob', 'bob2@test.local', 'x', 'bob'),
                (gen_random_uuid(), 'dave', 'Same@Test.Local', 'x', 'dave'),
                (gen_random_uuid(), 'erin', 'same@test.local', 'x', 'erin')");

        var ex = Assert.ThrowsAny<Exception>(() => PostgresFixture.MigrateUp(connectionString));

        var message = ex.ToString();
        Assert.Contains("bob", message);
        Assert.Contains("same@test.local", message);
        var version = await connection.ExecuteScalarAsync<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"");
        Assert.Equal(6, version);
    }
}
