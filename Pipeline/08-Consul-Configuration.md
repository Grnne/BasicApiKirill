# 08 — Consul Configuration

## Как хранятся конфиги

```
Consul KV: cbdc-api-gateway/{env}/config
                    ↑                    ↑
               dev/rc/prod          JSON-объект
```

Секции в JSON:
```json
{
  "Logging": { ... },
  "Protection": {
    "Orchestrator": { ... },
    "MicroCache": { ... }
  },
  "ReverseProxy": {
    "Routes": { ... },
    "Clusters": { ... }
  }
}
```

## Добавление нового параметра

1. Добавь extension метод в соответствующий файл:

```csharp
// Gateway.API/Extensions/Configuration/ConfigurationExtensions.Orchestrator.cs
public static int GetMyNewSetting(this IConfiguration config) =>
    config.GetValue<int>("Protection:Orchestrator:MyNewSetting", 42);
```

2. Добавь во все `consul-config.*.json` в тестах:

```json
{
  "Protection": {
    "Orchestrator": {
      "MyNewSetting": 42
    }
  }
}
```

3. Если настройка специфична для env — добавь в `helm/chart/values.yaml`:

```yaml
Development:
  MY_NEW_SETTING: "42"
```

## Тестирование Consul конфигов

В проекте есть тест `RealConsulJson_ShouldBeParsedSuccessfully_ForCurrentEnvironment`, 
который парсит `consul-config.{env}.json` и проверяет валидность.

При добавлении новых секций — убедись что этот тест проходит:

```bash
ASPNETCORE_ENVIRONMENT=development dotnet test --filter "RealConsulJson"
```

## Как добавить новый роут

→ Открой `09-New-Route.md`

## Локальное тестирование без Consul

```bash
# В тестах — автоматически через DISABLE_CONSUL=true
# В docker-compose — через CONSUL_PATH и CONSUL_KV_PREFIX

docker-compose up
```
