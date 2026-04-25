# Gateway API Pipeline — Simplified Workflow

Рабочий процесс для команды **Gateway API (YARP Reverse Proxy)**.

Этот набор инструкций адаптирован под существующую архитектуру проекта: 
4 проекта, Consul-конфигурация, интеграционные тесты с `WebApplicationFactory`, 
YARP-маршрутизация, Serilog/Graylog-логирование, OpenTelemetry-метрики.

**Не требует перехода на Clean Architecture/DDD** — инструкции работают 
с текущей структурой "как есть".

---

## Быстрый старт

| Если нужно | Открой |
|------------|--------|
| Понять структуру проекта | `01-Project-Map.md` |
| Начать новую фичу | `02-New-Feature-Checklist.md` |
| Написать тест (RED) | `03-TDD-RED.md` |
| Написать код (GREEN) | `04-TDD-GREEN.md` |
| Отрефакторить (REFACTOR) | `05-TDD-REFACTOR.md` |
| Проверить тесты перед коммитом | `06-Test-Review.md` |
| Проверить код перед коммитом | `07-Code-Review.md` |
| Разобраться с конфигами | `08-Consul-Configuration.md` |
| Добавить новый эндпоинт/роут | `09-New-Route.md` |
| Запустить тесты / CI | `10-Commands.md` |
| Написать логирование | `11-Logging-Conventions.md` |
| Проверить покрытие | `12-Coverage.md` |
| Примеры тестов | `13-Test-Patterns.md` |
| Метрики и мониторинг | `14-Metrics.md` |

---

## Основные правила

1. **Тесты пишутся до кода** — каждая фича начинается с RED-фазы
2. **Тесты READ-ONLY во время GREEN** — не меняй тесты, чтобы они проходили
3. **Одна фича = один прогресс-файл** — `progress-{feature-name}.md`
4. **Middleware ≤ 200 строк** — если больше, декомпозируй
5. **Тесты в отдельных классах** — не добавляй в `Tests.cs`, создавай новый файл
6. **Конфиги через Consul + `IConfiguration`** — не хардкодь значения
7. **Статические поля только через сервис** — для состояний используй DI-сервисы

---

## Структура Pipeline/

```
Pipeline/
├── README.md                   ← этот файл
├── 01-Project-Map.md           ← карта проекта
├── 02-New-Feature-Checklist.md ← чеклист новой фичи
├── 03-TDD-RED.md               ← RED: пишем тест
├── 04-TDD-GREEN.md             ← GREEN: пишем код
├── 05-TDD-REFACTOR.md          ← REFACTOR: улучшаем
├── 06-Test-Review.md           ← проверка тестов
├── 07-Code-Review.md           ← проверка кода
├── 08-Consul-Configuration.md  ← работа с Consul
├── 09-New-Route.md             ← новый роут/эндпоинт
├── 10-Commands.md              ← команды терминала
├── 11-Logging-Conventions.md   ← правила логирования
├── 12-Coverage.md              ← анализ покрытия
├── 13-Test-Patterns.md         ← примеры тестов
├── 14-Metrics.md               ← метрики (Prometheus)
└── progress-template.md        ← шаблон прогресса
```


Pipeline/ — упрощённый workflow для Gateway API
14 инструкций + 1 шаблон прогресса, адаптированные под текущую архитектуру проекта (без навязывания Clean Architecture/DDD).

Что внутри
#	Файл	О чём
1	Project-Map.md	Полная карта всех 4 проектов, поток запроса, ключевые решения, env-конфигурация
2	New-Feature-Checklist.md	Что сделать до начала работы: создать progress, определить scope, где изменения
3	TDD-RED.md	Как писать тест первым: шаблон, структура, доступные настройки GatewayTestFixture
4	TDD-GREEN.md	Минимальная реализация: примеры для middleware, extensions, config
5	TDD-REFACTOR.md	Что и как рефакторить: priority-таблица (статическое состояние → DI, middleware >200 строк → split)
6	Test-Review.md	28 проверок: названия тестов, assert-ы, безопасность, покрытие, параметризация
7	Code-Review.md	30+ проверок: middleware, статика, конфиги, логирование, метрики
8	Consul-Configuration.md	Как добавлять новые параметры в Consul, обновлять consul-config.*.json и тестировать
9	New-Route.md	Пошаговая инструкция по добавлению YARP маршрута (6 шагов)
10	Commands.md	Все команды: dotnet test, docker, consul, CI pipeline
11	Logging-Conventions.md	Как логировать: интерфейс, поля контекста, structured logging, маскирование
12	Coverage.md	Запуск coverlet, gap-анализ, цели покрытия по компонентам, CI интеграция
13	Test-Patterns.md	7 готовых шаблонов тестов: protection, caching, routing, request ID, config validation, TestCase, rejects
14	Metrics.md	Все метрики, как добавить новую, где инкрементятся, Prometheus scrape
—	progress-template.md	Шаблон для отслеживания фичи (TDD phases + checklist + notes)
Ключевые отличия от предыдущей версии (ManualWorkflow/)
Было (ManualWorkflow/)	Стало (Pipeline/)
Clean Architecture + DDD фокус	Адаптировано под текущую структуру 4 проектов
Абстрактные шаблоны	Конкретные примеры из кода Gateway (_factory.MaxConcurrent, TestForwarderHttpClientFactory.LastForwardedUri)
14 документов на все случаи	14 документов под конкретные задачи Gateway
coverage через abstract filter	Прямые ссылки на coverlet.collector и consul-config.*.json
Routes — общая теория	Routes — пошаговая инструкция с Reference к существующим тестам и YarpConstants
Progress template generic	Progress template с Gateway-специфичными пунктами (Consul config, Helm values, YARP routes)