# 06 — Test Review Checklist

Проверка тестов перед коммитом.

## Содержание теста

- [ ] Название теста описывает сценарий: `{Feature}_{WhenCondition}_{ExpectsResult}`
  - ✅ `OrchestratorProtection_WhenQueueExceeded_OnPost_Returns429`
  - ❌ `Test1`, `ProtectionTest`
- [ ] Один тест = одна логическая проверка (используй `Assert.Multiple` если нужно проверить несколько связанных фактов)
- [ ] Нет `Thread.Sleep()` — используй `Task.Delay()` с `await`
- [ ] Нет try/catch в тесте — тест должен падать сам

## Использование GatewayTestFixture

- [ ] Параметры теста настраиваются через `_factory.MaxConcurrent`, `_factory.QueueLimit` и т.д.
- [ ] Если тест требует специфичной конфигурации — добавь InMemory-коллекцию в `GatewayTestFixture.ConfigureAppConfiguration`
- [ ] Если тест проверяет роутинг — используй `TestForwarderHttpClientFactory.LastForwardedUri`

## Assert-ы

- [ ] Используй `Assert.That(actual, Is.EqualTo(expected))` — консистентно
- [ ] Для коллекций: `Assert.That(list, Has.Count.EqualTo(3))`
- [ ] Для булевых: `Assert.That(result, Is.True)` / `Assert.That(result, Is.False)`
- [ ] Для null: `Assert.That(value, Is.Not.Null)` / `Assert.That(value, Is.Null)`
- [ ] Для HTTP status: `Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK))`
- [ ] Для заголовков: `Assert.That(response.Headers.Contains("X-Request-Id"), Is.True)`

## Интеграционные тесты

- [ ] Тест не обращается к внешним сервисам (DISABLE_CONSUL=true)
- [ ] `MockConsulProvider` используется вместо реального Consul
- [ ] `TestForwarderHttpClientFactory` имитирует upstream

## Безопасность

- [ ] Нет хардкоженных паролей/токенов в тестовых данных
- [ ] Нет логирования sensitive данных в тестовом выводе
- [ ] Тестовые конфиги (`consul-config.*.json`) не содержат реальных секретов

## Покрытие

- [ ] Тест проверяет сценарий, который не покрыт другими тестами
- [ ] Добавлены тесты для:
  - [ ] Happy path
  - [ ] Error path (если релевантно)
  - [ ] Edge cases (граничные значения)
- [ ] Подумай про параметризованные тесты (TestCase)

```csharp
[TestCase("/api/dc/status", true)]
[TestCase("/api/dc/submit", false)]
public async Task MicroCache_ShouldWork_OnlyForPollingEndpoints(string url, bool shouldCache)
{
    // ...
}
```
