using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

/// <summary>
/// The REST contract as Swagger describes it must match the committed snapshot: any change of a
/// route, parameter or model shows up in the diff. After a deliberate change, rerun with
/// <c>UPDATE_OPENAPI_SNAPSHOT=1</c> and commit the new file.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OpenApiSnapshotTests(PostgresFixture db)
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public async Task Contract_MatchesSnapshot()
    {
        await using var factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" });
        using var client = factory.CreateClient();

        var actual = Canonical(await client.GetStringAsync("/swagger/v1/swagger.json"));

        var path = SnapshotPath();
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
            await File.WriteAllTextAsync(path, actual + "\n");

        Assert.True(File.Exists(path), $"No snapshot at {path}: run with UPDATE_OPENAPI_SNAPSHOT=1");
        Assert.Equal(Canonical(await File.ReadAllTextAsync(path)), actual);
    }

    // Keys sorted: the order of paths follows controller discovery and is not part of the contract.
    // Line breaks of XML comments follow the checkout (CRLF on Windows, LF in CI).
    private static string Canonical(string json) => Sorted(JsonNode.Parse(json))!.ToJsonString(Indented);

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        JsonValue v when v.TryGetValue(out string? s) => JsonValue.Create(s.Replace("\r\n", "\n")),
        _ => node?.DeepClone()
    };

    private static string SnapshotPath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "Snapshots", "openapi.json"));
}
