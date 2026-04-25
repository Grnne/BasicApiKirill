# 10 — Commands Reference

## Running Tests

```bash
# Все тесты
dotnet test

# Конкретный тест-класс
dotnet test --filter "GatewayIntegrationTests"

# Конкретный тест
dotnet test --filter "OrchestratorProtection_WhenQueueExceeded_OnPost_Returns429"

# Тесты по категории
dotnet test --filter "FullyQualifiedName~Orchestrator"

# С покрытием
dotnet test --collect:"XPlat Code Coverage"

# С покрытием + отчёт
dotnet test --collect:"XPlat Code Coverage"
# Затем посмотреть в ./TestResults/{guid}/coverage.cobertura.xml
```

## Building

```bash
# Сборка
dotnet build

# Сборка конкретного проекта
dotnet build Gateway.API/Gateway.API.csproj

# Публикация
dotnet publish Gateway.API/Gateway.API.csproj -c Release -o ./publish
```

## Solution

```bash
# Решение в корне проекта
dotnet build Gateway.API/Gateway.slnx

# Добавить проект в решение
dotnet sln Gateway.API/Gateway.slnx add Gateway.NewProject/Gateway.NewProject.csproj
```

## Docker

```bash
# Сборка образа
docker build -t gateway-api .

# Запуск локально
docker compose up
```

## Consul (для отладки)

```bash
# Прочитать конфиг из Consul
curl -X GET http://172.21.13.84:8500/v1/kv/cbdc-api-gateway/dev/config?raw

# Записать конфиг в Consul
curl -X PUT -d @config.json http://172.21.13.84:8500/v1/kv/cbdc-api-gateway/dev/config
```

## Окружение для тестов

```bash
# Development (по умолчанию)
ASPNETCORE_ENVIRONMENT=development dotnet test

# Staging
ASPNETCORE_ENVIRONMENT=staging dotnet test --filter "RealConsulJson"

# Production
ASPNETCORE_ENVIRONMENT=production dotnet test --filter "RealConsulJson"
```

## CI Pipeline (GitLab)

Pipeline стадии:
1. **Build**: Сборка Docker образа
2. **Test**: `dotnet test` в контейнере `mcr.microsoft.com/dotnet/sdk:10.0`
3. **Deploy**: Выкладка в Kubernetes через Helm
