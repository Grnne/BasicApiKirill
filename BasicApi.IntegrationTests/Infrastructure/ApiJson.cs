using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>Reading JSON answers of the API in tests.</summary>
public static class ApiJson
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>GET that must answer 200.</summary>
    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    /// <summary>POST of a JSON body that must succeed; returns the answer body.</summary>
    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object? body = null)
    {
        var response = body is null ? await client.PostAsync(url, null) : await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.ReadJsonAsync();
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).GetProperty("errorCode").GetString();

    /// <summary>The user's journal entries of one type, oldest first.</summary>
    public static async Task<List<JsonElement>> JournalAsync(this HttpClient client, string type) =>
        [.. (await client.GetJsonAsync("/api/sync?since=0&limit=500")).GetProperty("updates").EnumerateArray()
            .Where(u => u.GetProperty("type").GetString() == type)
            .Select(u => u.GetProperty("payload"))];

    public static async Task<long> PtsAsync(this HttpClient client) =>
        (await client.GetJsonAsync("/api/sync/state")).GetProperty("pts").GetInt64();

    /// <summary>The chat's history as the user sees it, oldest first.</summary>
    public static async Task<List<JsonElement>> HistoryAsync(this HttpClient client, Guid chatId) =>
        [.. (await client.GetJsonAsync($"/api/chats/{chatId}/messages/cursor?limit=100")).GetProperty("items").EnumerateArray()];

    public static Task<JsonElement> ChatItemAsync(this HttpClient client, Guid chatId) =>
        client.GetJsonAsync($"/api/chats/{chatId}/item");

    public static Guid Id(this JsonElement element, string property = "id") => element.GetProperty(property).GetGuid();
}
