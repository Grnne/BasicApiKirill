# 14 — Metrics (Prometheus + OpenTelemetry)

## Architecture

```
GatewayMetrics (singleton)
    ├── Counter: gateway.requests.total   ← {route, cluster, status}
    ├── Counter: gateway.rejects.total    ← {reason, route}
    ├── Counter: gateway.cache.hits       ← hits
    ├── Counter: gateway.cache.misses     ← misses
    ├── ObservableGauge: gateway.orchestrator.inflight   ← ActiveRequests
    └── ObservableGauge: gateway.orchestrator.queue_depth ← WaitingInQueue

Prometheus scrape: GET /metrics (OpenTelemetry PrometheusExporter)
```

## Existing Metrics

| Metric | Type | Labels | Description |
|--------|------|--------|-------------|
| `gateway.requests.total` | Counter | route, cluster, status | Общее число запросов |
| `gateway.rejects.total` | Counter | reason, route | Отклоненные запросы |
| `gateway.cache.hits` | Counter | — | Запросы из кэша |
| `gateway.cache.misses` | Counter | — | Промахи кэша |
| `gateway.orchestrator.inflight` | Gauge | — | Текущие запросы к оркестратору |
| `gateway.orchestrator.queue_depth` | Gauge | — | Размер очереди |

## Как добавить новую метрику

1. Открой `Gateway.API/Bootstrap/GatewayMetrics.cs`
2. Добавь новый Counter/Gauge:

```csharp
public class GatewayMetrics
{
    // Existing counters...

    public Counter<long> MyNewMetric { get; }

    public GatewayMetrics()
    {
        // Existing counters...

        MyNewMetric = _meter.CreateCounter<long>(
            "gateway.my_new_metric",
            description: "Описание метрики");
    }
}
```

3. Инкременть в нужном месте:

```csharp
_metrics.MyNewMetric.Add(1, new("key", "value"));
```

## Где инкрементятся метрики сейчас

| Метрика | Где инкрементится |
|---------|-------------------|
| `requests.total` | `RequestIdAndLoggingMiddleware.LogResponse()` |
| `rejects.total` | `OrchestratorProtectionMiddleware.RejectRequest()` |
| `cache.hits` / `cache.misses` | Вручную пока не инкрементятся (можно добавить из `MicroCachePolicy`) |

## Как не кэшировать метрики

```csharp
// Program.cs — metrics endpoint не кэшируется
app.MapPrometheusScrapingEndpoint().CacheOutput(p => p.NoCache());
```

## Alerts (Prometheus)

Примеры алертов для Runbook:

```yaml
# gateway_rejects_high: много отказов за последние 5 минут
# gateway_inflight_max: inflight == MaxConcurrent более 5 минут
# gateway_queue_growing: queue_depth стабильно > 0
```
