# 13 — Test Patterns

## Pattern 1: Protection/Throttling

Тестируем, что middleware отклоняет запросы при превышении лимита.

```csharp
[Test]
public async Task Protection_WhenQueueExceeded_Returns429()
{
    // Arrange
    _factory.UpstreamDelay = TimeSpan.FromSeconds(2); // upstream тормозит
    _factory.MaxConcurrent = 1;
    _factory.QueueLimit = 1;

    var client = _factory.CreateClient();

    // Act
    var task1 = client.PostAsync("/api/dc/submit", new StringContent("{}")); // активный слот
    await Task.Delay(50);
    var task2 = client.PostAsync("/api/dc/submit", new StringContent("{}")); // очередь
    await Task.Delay(50);
    var task3 = client.PostAsync("/api/dc/submit", new StringContent("{}")); // 429

    var responses = await Task.WhenAll(task1, task2, task3);

    // Assert
    Assert.That(responses[0].StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(responses[1].StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(responses[2].StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
}
```

## Pattern 2: Caching

Проверяем, что повторные запросы кэшируются.

```csharp
[Test]
public async Task Cache_WhenMultipleIdenticalRequests_CoalescesIntoOne()
{
    // Arrange
    _factory.UpstreamCallCount = 0;
    _factory.UpstreamDelay = TimeSpan.FromMilliseconds(500);
    var client = _factory.CreateClient();

    // Act
    var tasks = Enumerable.Range(0, 10).Select(_ =>
        client.GetAsync("/api/dc/status")).ToList();
    var responses = await Task.WhenAll(tasks);

    // Assert
    Assert.That(responses.All(r => r.StatusCode == HttpStatusCode.OK), Is.True);
    Assert.That(_factory.UpstreamCallCount, Is.EqualTo(1));
}

[Test]
public async Task Cache_WhenTtlExpired_SecondRequestHitsUpstream()
{
    // Arrange
    _factory.UpstreamCallCount = 0;
    var client = _factory.CreateClient();

    // Act
    await client.GetAsync("/api/dc/status");
    Assert.That(_factory.UpstreamCallCount, Is.EqualTo(1));

    await Task.Delay(3000); // Ждём TTL (2000ms)

    await client.GetAsync("/api/dc/status");

    // Assert
    Assert.That(_factory.UpstreamCallCount, Is.EqualTo(2));
}
```

## Pattern 3: Routing

Проверяем, что URL трансформируется правильно.

```csharp
[Test]
public async Task Route_WhenPortalEndpoint_TransformsCorrectly()
{
    // Arrange
    var client = _factory.CreateClient();
    var factory = _factory.Services
        .GetRequiredService<IForwarderHttpClientFactory>() as TestForwarderHttpClientFactory;

    // Act
    await client.GetAsync("/api/portal/account");

    // Assert
    Assert.That(factory.LastForwardedUri,
        Is.EqualTo("https://dev.moduldev.ru/api/account"));
}
```

## Pattern 4: Request ID

Проверяем propagation X-Request-Id.

```csharp
[Test]
public async Task RequestId_WhenClientSends_ShouldBePreservedInResponse()
{
    // Arrange
    var client = _factory.CreateClient();
    var customId = "my-custom-id-123";

    var request = new HttpRequestMessage(HttpMethod.Get, "/api/portal/info");
    request.Headers.Add("X-Request-Id", customId);

    // Act
    var response = await client.SendAsync(request);

    // Assert
    Assert.That(response.Headers.GetValues("X-Request-Id").First(),
        Is.EqualTo(customId));
}
```

## Pattern 5: Consul Config Validation

Проверяем парсинг Consul JSON.

```csharp
[Test]
public void ConsulConfig_ShouldBeValid_ForCurrentEnv()
{
    // Arrange
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").ToLower();
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile($"config/consul-config.{env}.json", optional: false)
        .Build();

    // Act
    var provider = new ConsulConfigProvider(config, Mock.Of<ILogger>(), Mock.Of<IOutputCacheStore>());
    provider.InitializeAsync().Wait();

    // Assert
    Assert.That(provider.IsReady, Is.True);
}
```

## Pattern 6: TestCase (параметризованный)

```csharp
[TestCase("/api/dc/status", ExpectedResult = true)]
[TestCase("/api/dc/submit", ExpectedResult = false)]
public async Task<bool> MicroCache_ShouldOnlyCachePollingEndpoints(string url)
{
    // Arrange
    _factory.UpstreamCallCount = 0;
    var client = _factory.CreateClient();

    // Act
    await client.GetAsync(url);

    // Потом проверяем кэширование
    await client.GetAsync(url);

    // Assert
    return _factory.UpstreamCallCount == 1; // true если кэшируется
}
```

## Pattern 7: Reject reason in response

```csharp
[Test]
public async Task Protection_WhenRejected_ResponseContainsRetryAfter()
{
    // Arrange
    _factory.MaxConcurrent = 1;
    _factory.QueueLimit = 0;
    var client = _factory.CreateClient();

    // Act
    var task1 = client.PostAsync("/api/dc/submit", new StringContent("{}"));
    await Task.Delay(50);
    var task2 = client.PostAsync("/api/dc/submit", new StringContent("{}"));

    var responses = await Task.WhenAll(task1, task2);

    // Assert
    Assert.That(responses[1].Headers.RetryAfter, Is.Not.Null);
    Assert.That(responses[1].Headers.RetryAfter.ToString(), Is.EqualTo("5"));
}
```
