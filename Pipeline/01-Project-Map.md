# 01 — Project Map

## Gateway API: Reverse Proxy для Цифрового рубля

```
./
├── Gateway.API/              ← ASP.NET Core Web API (точка входа)
│   ├── Program.cs            ← Pipeline: Consul → middleware → reverse proxy
│   ├── Gateway.API.csproj    ← net10.0, Yarp.ReverseProxy 2.3.0
│   ├── Gateway.slnx          ← Solution file
│   ├── Bootstrap/
│   │   ├── GatewayMetrics.cs       ← OpenTelemetry/Prometheus метрики
│   │   ├── MicroCachePolicy.cs     ← Output caching для polling endpoints
│   │   ├── Models/Constants/
│   │   │   └── PayloadMode.cs      ← "full" / "safe"
│   │   └── Startup/
│   │       ├── ConfigurationBuilderExtensions.cs  ← Consul bootstrap
│   │       ├── LoggingBuilderExtensions.cs        ← Serilog setup
│   │       └── WebApplicationBuilderExtensions.cs ← DI регистрации
│   ├── Middleware/
│   │   ├── RequestIdAndLoggingMiddleware.cs ← (~230 строк) логирование + X-Request-Id
│   │   └── OrchestratorProtectionMiddleware.cs ← Throttling /dc/ и /dcu/
│   └── Extensions/Configuration/
│       ├── ConfigurationExtensions.Base.cs         ← ServiceName, Version, Env
│       ├── ConfigurationExtensions.Orchestrator.cs ← Protection настройки
│       ├── ConfigurationExtensions.Logging.cs      ← Graylog, Payload настройки
│       └── ConfigurationExtensions.MicroCache.cs   ← Cache настройки
│
├── Gateway.Consul/           ← Consul dynamic configuration
│   ├── Gateway.Consul.csproj
│   ├── YarpConstants.cs           ← RequiredClusters, RequiredRoutes
│   ├── Contracts/
│   │   └── IConsulConfigProvider.cs ← IProxyConfigProvider + IsReady + InitializeAsync
│   └── Implementation/
│       ├── ConsulConfigProvider.cs    ← Читает YARP config из Consul KV
│       └── ProxyConfig.cs            ← Immutable snapshot с CancellationTokenSource
│
├── Gateway.Logger/           ← Serilog logging infrastructure
│   ├── Gateway.Logger.csproj
│   ├── LoggerOptions.cs           ← ServiceName, AppVersion, GraylogHost, etc.
│   └── Core/
│       ├── SerilogLoggerFactory.cs    ← Строит Serilog pipeline
│       ├── SerilogLogger.cs           ← Adapter для Modulbank.Logger.Contracts
│       ├── StandardLogFormatter.cs    ← JSON formatter для console/file
│       └── GelfLogFormatter.cs        ← GELF 1.1 formatter для Graylog
│
├── Gateway.Tests/            ← Integration tests (NUnit + Moq)
│   ├── Gateway.Tests.csproj  ← net10.0, coverlet, WebApplicationFactory
│   ├── Tests.cs              ← 8 integration tests (Orchestrator, MicroCache, Routing, Config)
│   ├── Infrastructure/
│   │   ├── GatewayTestFixture.cs           ← WebApplicationFactory<Program>
│   │   ├── MockConsulProvider.cs           ← Mock IConsulConfigProvider
│   │   └── TestForwarderHttpClientFactory.cs ← Mock upstream responses
│   └── config/
│       ├── consul-config.development.json
│       ├── consul-config.staging.json
│       └── consul-config.prod.json
│
├── helm/chart/               ← Kubernetes deployment
│   ├── Chart.yaml
│   ├── values.yaml           ← Env-specific (Dev/Staging/Production)
│   └── templates/
│       ├── deployment.yaml, gateway.yaml, ingress.yaml
│       ├── service.yaml, service-monitor.yaml, service-nodeport.yaml
│       └── _helpers.tpl, image-pull-secret.yml
│
├── .gitlab-ci.yml            ← CI/CD: Build → Test → Deploy (Dev/RC/Prod)
├── Dockerfile                ← Multi-stage: sdk 10.0 → aspnet 10.0
├── docker-compose.yml        ← Локальный запуск
├── NuGet.Config              ← nexus.moduldev.ru private feed
└── Readme.md                 ← Runbook (деградация, отключение защиты, диагностика)
```

## Архитектура потока запроса

```
Client → [RequestIdAndLoggingMiddleware] → [OrchestratorProtectionMiddleware*] → [YARP Reverse Proxy] → Upstream
                                                ↑                                    ↑
                                           Только /dc/, /dcu/                ConsulConfigProvider
                                                                             (dynamic routes/clusters)
```

## Ключевые решения

| Решение | Где | Почему |
|---------|-----|--------|
| Конфигурация из Consul KV | `ConsulConfigProvider` | Без рестарта, env-префиксы (dev/rc/prod) |
| Статические поля для состояния | `OrchestratorProtectionMiddleware` | `_activeRequests`, `_waitingInQueue` |
| Composite cache key | `MicroCachePolicy` | SHA256(Authorization + X-Token + body) |
| Semaphore throttling | `OrchestratorProtectionMiddleware` | SemaphoreSlim + queue + timeout |
| Partial class для конфигов | `ConfigurationExtensions*.cs` | 4 файла с extension методами |
| WebApplicationFactory | `GatewayTestFixture` | Интеграционные тесты с mock upstream |

## Env-конфигурация

| Environment | Consul KV Prefix | Helm values |
|-------------|------------------|-------------|
| Development | `cbdc-api-gateway/dev/config` | `values.yaml → Development` |
| Staging | `cbdc-api-gateway/rc/config` | `values.yaml → Staging` |
| Production | `cbdc-api-gateway/prod/config` | `values.yaml → Production` |
