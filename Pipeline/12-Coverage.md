# 12 — Code Coverage

## Запуск

```bash
# Базовый замер
dotnet test --collect:"XPlat Code Coverage"

# С фильтром по проектам (если нужно)
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage-results

# Найти отчёт
# ./TestResults/{guid}/coverage.cobertura.xml
```

## Что меряем

Цель: **80%+ покрытие** для нового кода.

| Компонент | Цель | Примечание |
|-----------|------|-----------|
| Middleware (Invoke) | 90%+ | Основная логика |
| Bootstrap (MicroCachePolicy) | 90%+ | Кэширование |
| Configuration Extensions | 100% | Простые геттеры |
| ConsulConfigProvider | 80%+ | Валидация + обновление |
| SerilogLoggerFactory | 60%+ | Сложно тестировать (I/O) |

## Gap анализ (что не покрыто)

После замера просмотри `coverage.cobertura.xml` или используй report generator:

```bash
# Установка
dotnet tool install -g dotnet-reportgenerator-globaltool

# Генерация HTML отчёта
reportgenerator -reports:./TestResults/**/coverage.cobertura.xml -targetdir:./coverage-report -reporttypes:Html
```

### Типы непокрытого кода

| Тип | Действие |
|-----|----------|
| **Dead code** — условие, которое никогда не выполняется | Удали |
| **Error handling** — catch блоки без тестов | Добавь тест на ошибку |
| **Edge cases** — граничные значения (0, null, empty) | Добавь параметризованный тест |
| **Configuration fallback** — если не задан параметр | Добавь TestCase |

## CI интеграция

В `.gitlab-ci.yml` добавь coverage job:

```yaml
Test:DEV:API GATEWAY:
  extends: .unit_tests
  variables:
    <<: *default_dev_variables
  script:
    - dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
    - reportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage-report -reporttypes:Html
  artifacts:
    paths:
      - coverage-report/
  rules:
    - !reference [.rules, .dev]
```

## Правила

1. **Новый код** — покрытие > 80%
2. **Старый код** — не снижай покрытие ниже текущего уровня
3. **Dead code** — удаляй сразу
4. **Error paths** — всегда покрывай
