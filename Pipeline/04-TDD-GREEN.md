# 04 — TDD: GREEN Phase

Пишем минимальный код, чтобы тест прошёл.

## Правила GREEN

1. **Минимальная реализация** — только то, что нужно для прохождения теста. Ничего лишнего.
2. **Тесты READ-ONLY** — **НЕЛЬЗЯ** менять тест, чтобы он прошёл. Если тест не проходит, меняй имплементацию.
3. **Можно хардкодить** — на этапе GREEN разрешён хардкод и "грязные" решения. Рефакторинг будет на REFACTOR.
4. **Все тесты должны проходить** — не только новый, но и все старые:

```bash
dotnet test
```

## Типовые сценарии GREEN для Gateway

### Новый middleware

```csharp
// Gateway.API/Middleware/MyNewMiddleware.cs
public class MyNewMiddleware
{
    private readonly RequestDelegate _next;

    public MyNewMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        // Минимальная логика для прохождения теста
        await _context.Response.WriteAsync("some value");
        // ИЛИ
        await _next(context);
    }
}
```

### Новый extension для конфигурации

```csharp
// Gateway.API/Extensions/Configuration/ConfigurationExtensions.MyFeature.cs
public static partial class ConfigurationExtensions
{
    public static string GetMyNewSetting(this IConfiguration config) =>
        config.GetValue<string>("MyFeature:Setting", "default");
}
```

### Новый роут в тестовом конфиге

```csharp
// MockConsulProvider.SetValidConfig() — добавить новый RouteConfig и ClusterConfig
```

## Чекай перед переходом к REFACTOR

- [ ] `dotnet test` — все тесты (старые + новый) проходят?
- [ ] Реализация минимальна?
- [ ] Нет dead code?
- [ ] Тесты не менялись с RED фазы?
