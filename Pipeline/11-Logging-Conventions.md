# 11 — Logging Conventions

## Architecture

```
SerilogLoggerFactory (build pipeline)
    ├── Graylog sink (GELF 1.1) ✓
    ├── Console sink (StandardLogFormatter → JSON)
    └── File sink (rolling daily) ✓

Modulbank.Logger.Contracts.NetCore.ILogger (через DI)
    └── SerilogLogger (adapter)
```

## Как логировать

```csharp
// Через DI — везде используй этот интерфейс
public class MyService
{
    private readonly Modulbank.Logger.Contracts.NetCore.ILogger _log;

    public MyService(ILogger log) => _log = log;

    public async Task DoSomething()
    {
        await _log.Info("Сообщение", "MyService.DoSomething");

        await _log.Error("Ошибка: {reason}", "MyService", new Dictionary<string, object>
        {
            ["reason"] = "timeout"
        });
    }
}
```

## Поля контекста

Все логи автоматически содержат:
| Поле | Откуда | Пример |
|------|--------|--------|
| `service` | LoggerOptions.ServiceName | `api-gateway` |
| `version` | LoggerOptions.AppVersion | `1.0.0.0` |
| `host` | Environment.MachineName | `gateway-pod-xxx` |
| `env` | short env | `dev`, `rc`, `prod` |
| `event` | параметр вызова | `http.request_in`, `http.request_out` |
| `request_id` | X-Request-Id | `a1b2c3d4` |
| `source_context` | имя класса | `RequestIdAndLoggingMiddleware` |

## Structured logging — поля в Middleware

```csharp
var ctx = new Dictionary<string, object>
{
    ["http.method"] = req.Method,
    ["http.path"] = req.Path.Value,
    ["http.duration_ms"] = durationMs,
    ["route_id"] = routeId,
    ["cluster_id"] = clusterId,
    ["upstream"] = upstream,
    ["reject_reason"] = rejectReason  // только если отклонён
};
await _log.Log(level, msg, null, ctx);
```

## Правила

1. **Не используй `Console.WriteLine`** в production коде — только через `ILogger`
2. **Структурированные поля** — используй `Dictionary<string, object>` для контекста
3. **Sensitive данные маскируются** — автоматически через `SensitiveDataRegex` в `RequestIdAndLoggingMiddleware`
4. **Payload логирование** — только если `Logging:Payload:Mode != "none"`, макс. `MaxBytes` (262144)
5. **Sensitive log file** — только в dev среде, пишется в `Logging:SensitivePath`
6. **Уровни**: `Verbose/Debug` — для debug, `Info` — для обычных событий, `Warn` — для аномалий, `Error` — для ошибок
7. **Graylog** — включается через `GRAYLOG_ENABLED=true` или `Logging:Graylog:Enabled=true`
