# 07 — Code Review Checklist

Проверка кода перед коммитом.

## Middleware

- [ ] Middleware не превышает 200 строк
- [ ] Конструктор принимает только `RequestDelegate` + зависимости
- [ | `Invoke(HttpContext context)` — асинхронный
- [ ] Нет блокирующих вызовов (`.Result`, `.Wait()`, `Task.WaitAll`)
- [ ] Stream/Reader корректно диспозится (или используется `leaveOpen` с Position reset)
- [ ] Если middleware добавляет/меняет заголовки — проверь, что это не ломает downstream

## Статическое состояние

- [ ] Статические поля только если это read-only константы
- [ ] Для изменяемого состояния используй DI-сервисы (singleton)
- [ ] Если статика необходима — `static volatile` + `Interlocked` (как в `OrchestratorProtectionMiddleware`)
- [ ] Помечай статические поля атрибутом `[ThreadStatic]` если потоковое

## Конфигурация (IConfiguration)

- [ ] Все настройки читаются через `IConfiguration`, не хардкод
- [ ] Есть fallback/default значение
- [ ] Значение логируется при старте (особенно для protection/cache)
- [ ] Новые настройки добавлены в `consul-config.{env}.json` тестов и в `values.yaml`

## Логирование

- [ | Используется `Modulbank.Logger.Contracts.NetCore.ILogger` через DI
- [ ] Sensitive данные маскируются (пароли, токены)
- [ ] В response logging добавлена `reject_reason` если запрос отклонён
- [ ] Не логируются тела запросов > MaxBytes (по умолчанию 262144)

## Метрики

- [ | Counter-ы инкрементятся в GatewayMetrics
- [ ] Новые метрики зарегистрированы в `GatewayMetrics` конструкторе
- [ | Prometheus endpoint не кэшируется (`.CacheOutput(p => p.NoCache())`)

## Тесты

- [ ] Тесты в отдельном файле, не в `Tests.cs`
- [ | Используют `GatewayTestFixture` (или аналогичную инфраструктуру)
- [ ] Названия тестов по шаблону `{Feature}_{WhenCondition}_{ExpectsResult}`
- [ ] Нет хардкоженных задержек (`Thread.Sleep`)
- [ ] `[SetUp]`/`[TearDown]` корректно создают/удаляют `_factory`

## Общие

- [ ] Нет `#region` — если нужна группировка, раздели на классы
- [ ] Файл начинается с `using` statements
- [ ] Namespace соответствует папке
- [ ] Класс `public` если используется из другого проекта
- [ ] Нет закомментированного кода
- [ ] Нет `Console.WriteLine` в production коде (кроме `Program.cs` и `SerilogLoggerFactory`)
