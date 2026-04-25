# 03 — TDD: RED Phase

Пишем тест, который должен упасть, потому что код ещё не написан.

## Правила RED

1. **Тест определяет ожидаемое поведение** — не пиши тест под уже существующий код
2. **Тест должен падать с осмысленной ошибкой** — не `NullReferenceException`, а `Assert.That(...)` с понятным сообщением
3. **Один тест = одна проверка** — не пиши 10 asserts в одном тесте (используй `Assert.Multiple` если нужно)
4. **Тест НЕ трогает:**
   - Базу данных
   - Файловую систему (кроме `sensitiveLogPath` если явно тестируешь)
   - Реальные внешние сервисы
   - Consul (через `DISABLE_CONSUL=true` и `MockConsulProvider`)

## Шаблон теста для Gateway

```csharp
namespace Gateway.Tests;

[TestFixture]
public class MyNewFeatureTests
{
    private GatewayTestFixture _factory;

    [SetUp]
    public void Setup()
    {
        _factory = new GatewayTestFixture();
        // Настройка специфичных параметров для теста
        _factory.MaxConcurrent = 1;
        _factory.QueueLimit = 1;
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task MyFeature_WhenCondition_ExpectsResult()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/dc/some-endpoint");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

## Структура тестов в проекте

| Что тестируешь | Где писать тест | Инфраструктура |
|----------------|-----------------|----------------|
| Middleware | Новый `.cs` файл | `GatewayTestFixture` + настройка `_factory` |
| Routing/прокси | Тот же файл | `TestForwarderHttpClientFactory.LastForwardedUri` |
| Consul config | Тот же файл | `config/consul-config.{env}.json` |
| Reject/Protection | Тот же файл | `_factory.UpstreamDelay`, `_factory.MaxConcurrent` |
| Caching | Тот же файл | `_factory.UpstreamCallCount` |

## Доступные настройки GatewayTestFixture

```csharp
_factory.UpstreamCallCount = 0;   // Счетчик вызовов upstream
_factory.UpstreamDelay = TimeSpan.Zero;  // Симуляция задержки upstream
_factory.MaxConcurrent = 5;       // Protection:MaxConcurrent
_factory.QueueLimit = 10;         // Protection:QueueLimit
```

## Чекай перед коммитом RED

- [ ] Тест падает с ожидаемой ошибкой?
- [ ] Сообщение об ошибке понятно?
- [ ] Тест не требует внешних зависимостей?
- [ ] Тест в отдельном файле (не в `Tests.cs`)?
- [ ] `[SetUp]`/`[TearDown]` правильно настроены?
