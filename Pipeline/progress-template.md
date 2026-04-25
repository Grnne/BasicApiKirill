# Progress: {Feature Name}

> **Story:** {link to Jira/issue if any}
> **Owner:** @{name}
> **Started:** {date}
> **Target:** {date}

## TDD Phases

- [ ] **RED** — тест написан и падает с ожидаемой ошибкой
- [ ] **GREEN** — минимальный код, все тесты проходят
- [ ] **REFACTOR** — код улучшен без изменения поведения

## Checklist

### Анализ
- [ ] Определены affected компоненты (API/Consul/Logger/Tests)
- [ ] Нужны ли новые роуты в YARP?
- [ ] Нужны ли новые настройки в Consul?
- [ ] Нужны ли новые метрики?

### Реализация
- [ ] Тест написан первым (RED)
- [ ] Минимальная реализация (GREEN)
- [ ] Рефакторинг (REFACTOR)
- [ ] Middleware ≤ 200 строк
- [ ] Тест-класс выделен отдельно (не в Tests.cs)
- [ ] Нет хардкода — конфиги через `IConfiguration`

### Проверка
- [ ] `dotnet test` — все тесты проходят
- [ ] `dotnet test --collect:"XPlat Code Coverage"` — покрытие > 80%
- [ ] Code Review по `07-Code-Review.md`
- [ ] Test Review по `06-Test-Review.md`

### CI/CD
- [ ] Consul config для всех env (dev/rc/prod) актуален
- [ ] Если новый роут — обновлены `consul-config.*.json` в тестах
- [ ] Если новый параметр — добавлен в `values.yaml` (все env)
- [ ] MR создан

## Notes

```
{any notes, decisions, or questions}
```
