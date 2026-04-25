# 09 — Adding a New Route

Инструкция по добавлению нового YARP маршрута.

## 1. Определи route config

Параметры:
- `RouteId` — уникальный ID (пример: `dc-{action}-route`)
- `ClusterId` — к какому кластеру направлять
- `Match.Path` — паттерн URL (YARP route pattern)
- `Transforms` — трансформация пути/заголовков
- `Timeout` — таймаут для данного роута
- `Metadata` — доп. параметры (AllowRetry)

## 2. Добавь в Consul конфиг (все 3 env)

Обнови `Gateway.Tests/config/consul-config.{development,staging,prod}.json`:

```json
{
  "ReverseProxy": {
    "Routes": {
      "my-new-route": {
        "Order": 100,
        "ClusterId": "orchestrator-cluster",
        "Match": { "Path": "/api/dc/my-action/{**catchall}" },
        "Transforms": [
          { "PathPattern": "dc/cddc/my-action/{**catchall}" }
        ],
        "Timeout": "00:00:30"
      }
    }
  }
}
```

## 3. Если новый кластер — добавь ClusterConfig

```json
{
  "ReverseProxy": {
    "Clusters": {
      "my-new-cluster": {
        "Destinations": {
          "d1": { "Address": "https://my-upstream.modulbank.ru/" }
        }
      }
    }
  }
}
```

## 4. Обнови YarpConstants (если mandatory)

Если роут или кластер обязателен — добавь в `Gateway.Consul/YarpConstants.cs`:

```csharp
public static readonly string[] RequiredRoutes = { 
    "portal-route", "esia-route", "...", "my-new-route" 
};
```

## 5. Обнови MockConsulProvider

Добавь новый `RouteConfig` и/или `ClusterConfig` в `SetValidConfig()`:

```csharp
new RouteConfig
{
    RouteId = "my-new-route",
    ClusterId = "orchestrator-cluster",
    Match = new RouteMatch { Path = "/api/dc/my-action/{**catchall}" },
    Transforms = new[] { new Dictionary<string, string> { { "PathPattern", "dc/cddc/my-action/{**catchall}" } } }
}
```

## 6. Напиши тест

Проверь:
- Роутинг — запрос уходит на правильный upstream
- Трансформация пути — корректный `LastForwardedUri`
- Таймаут (если отличается от дефолтного)

## 7. Пример

Смотри существующие тесты в `Gateway.Tests/Tests.cs`:
- `Routing_ShouldTransformAndForwardToCorrectCluster` — проверка маршрутов portal, esia, orchestrator
- `Config_RegexRouting_ShouldMatchCorrectRoutes` — проверка regex routing
