# 05 — TDD: REFACTOR Phase

Улучшаем код без изменения поведения.

## Правила REFACTOR

1. **Тесты не трогаем** — если нужно изменить тест, значит изменилось поведение — это новая RED фаза
2. **Один рефакторинг за раз** — сделал изменение → `dotnet test` → всё зелёное? Идём дальше
3. **После каждого изменения — `dotnet test`**
4. **Измеряем покрытие** ← `dotnet test --collect:"XPlat Code Coverage"

## Что рефакторить (приоритет)

### 🔴 Высокий — обязательно
| Симптом | Действие |
|---------|----------|
| Middleware > 200 строк | Разделить на несколько классов/методов |
| Тест-класс > 200 строк | Разделить на несколько тест-классов |
| Статическое состояние | Выделить в DI-сервис |

### 🟡 Средний — желательно
| Симптом | Действие |
|---------|----------|
| Дублирование кода | Выделить общий метод |
| Магические строки | Вынести в `YarpConstants` или отдельный класс |
| Смешанные ответственности | Разделить на два класса |

### 🟢 Низкий — можно оставить
| Симптом | Действие |
|---------|----------|
| Небольшие методы без дублирования | Оставить |
| Конфиги через `IConfiguration.GetValue()` | OK для текущей архитектуры |

## Примеры рефакторинга для Gateway

### Статические поля → DI сервис

**До:**
```csharp
private static int _activeRequests = 0;
private static int _waitingInQueue = 0;
```

**После:**
```csharp
// Gateway.API/Bootstrap/ConcurrencyTracker.cs
public class ConcurrencyTracker
{
    private int _activeRequests = 0;
    public int ActiveRequests => _activeRequests;
    // ...
}

// В WebApplicationBuilderExtensions.cs
builder.Services.AddSingleton<ConcurrencyTracker>();
```

### Тяжёлый middleware → несколько middleware

**До:** `RequestIdAndLoggingMiddleware.cs` (230 строк, mixed concerns)

**После:**
```
Middleware/
├── RequestIdMiddleware.cs          ← только X-Request-Id
├── LoggingMiddleware.cs            ← только логирование
└── SensitiveDataMiddleware.cs      ← только sanitization
```

### Config → типизированные Options (если нужно)

**До:**
```csharp
config.GetValue("Protection:Orchestrator:MaxConcurrent", 5)
```

**После:**
```csharp
public class OrchestratorProtectionOptions
{
    public bool Enabled { get; init; }
    public int MaxConcurrent { get; init; } = 5;
    // ...
}

services.Configure<OrchestratorProtectionOptions>(config.GetSection("Protection:Orchestrator"));
```

## Чекай перед коммитом

- [ ] Все тесты проходят после всех изменений?
- [ ] Middleware ≤ 200 строк?
- [ ] Тест-файлы ≤ 200 строк?
- [ ] Нет дублирования кода?
- [ ] Имена классов/методов отражают их ответственность?
