using System.Net;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public class StartupConfigurationTests(PostgresFixture db)
{
    [Fact]
    public void Production_WithPlaceholderJwtKey_RefusesToStart()
    {
        using var factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "your-super-secret-key-with-at-least-32-characters-long"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public async Task Production_SwaggerDisabledByDefault()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Production_SwaggerEnabledByFlag()
    {
        await using var factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?>
        {
            ["Swagger:Enabled"] = "true"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Production_DoesNotRedirectToHttps()
    {
        // TLS снимает прокси; редирект внутри приложения давал бы петлю за ним.
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
