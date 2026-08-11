# Project Analysis: BasicApi (BasicChatApi)

> Real-time чат-API на ASP.NET Core 10: JWT-аутентификация, SignalR, PostgreSQL + Dapper, курсорная пагинация и полнотекстовый поиск.

*Дата анализа: 2026-08-11. Ветка `master`, коммит `7400388`. Тесты: **103 passed / 0 failed** (`dotnet test BasicApi.sln`).*

---

## Overview

**BasicApi** — бэкенд мессенджера: регистрация/логин по JWT, приватные чаты, обмен сообщениями в реальном времени через SignalR, история сообщений с курсорной пагинацией, полнотекстовый поиск по сообщениям, поиск чатов и пользователей, статусы online/typing. В `wwwroot/app` лежит небольшой демо-фронтенд на ванильном ES-модульном JS, который потребляет и REST, и хаб.

Архитектура — классическая слоёная с feature-folders: `Controller` (тонкий, только атрибуты/Swagger/клэмп параметров) → `Handler` (оркестрация, возвращает `IActionResult`) → `Service` (бизнес-логика, авторизация, маппинг в DTO) → `Repository` (Dapper + сырой SQL) → PostgreSQL. Ошибки не ловятся по месту: домены бросают `DomainException`-наследников, а единственный `ExceptionHandlingMiddleware` превращает их в RFC 7807 ProblemDetails с машиночитаемым `errorCode`.

Решение состоит из трёх проектов: `BasicApi` (веб-приложение), `BasicApi.Storage` (сущности, репозитории, миграции), `BasicApi.Tests` (xUnit + Moq). Инфраструктура: Dapper 2.1.72, Npgsql 10, FluentMigrator 8 (миграции применяются на старте), BCrypt для паролей, Swashbuckle для Swagger, docker-compose с PostgreSQL и healthcheck.

**Общая оценка:** для учебно-пет-проекта уровень заметно выше среднего — продуманная курсорная пагинация, батчевые SQL без N+1, единый контракт ошибок, 103 юнит-теста. Основные риски лежат не в архитектуре, а в эксплуатации: **секреты закоммичены в репозиторий**, нет CI, нет интеграционных тестов на SQL, состояние присутствия хранится в памяти процесса, CORS открыт настежь.

---

## Project Structure

```
BasicChatApi/
├── BasicApi.sln
├── docker-compose.yml / docker-compose.dcproj
├── .env                               # ⚠️ закоммичен, содержит DB_USER/DB_PASSWORD
├── readme.md                          # запуск через docker-compose + описание хаба
├── Project-Analysis-Prompt.md         # шаблон, по которому сделан этот документ
├── CursorPaginationChanges.md         # история рефакторинга пагинации
├── test-endpoints.ps1                 # ручной прогон эндпоинтов
├── workflow/                          # внутренний TDD-регламент (6 md-файлов)
├── .continue/rules/                   # правила для AI-ассистента (паттерн поиска чатов)
├── .github/workflows/                 # ⚠️ пустая папка — CI отсутствует
│
├── BasicApi/                          # 🔵 Web API (net10.0)
│   ├── Program.cs                     # 59 строк — точка входа и пайплайн
│   ├── Dockerfile                     # multi-stage sdk:10.0 → aspnet:10.0
│   ├── appsettings.json               # ⚠️ содержит Jwt:Key и строку подключения
│   ├── Extensions/
│   │   ├── ServiceExtensions.cs       # 209 — DI, JWT, SignalR, CORS, FluentMigrator, коды валидации
│   │   ├── SwaggerExtensions.cs       # 70
│   │   └── ClaimsPrincipalExtensions.cs
│   ├── Middleware/
│   │   ├── ExceptionHandlingMiddleware.cs   # 115
│   │   └── Exceptions/                # DomainException + 5 наследников
│   ├── Features/
│   │   ├── Auth/    AuthController (78) + AuthHandler (100)
│   │   ├── Chats/   ChatsController (166) + ChatsHandler (128)
│   │   └── Users/   UsersController (74) + UsersHandler (103)
│   ├── Hubs/ChatHub.cs                # 250 — presence, join/leave, send, typing, ping
│   ├── Services/
│   │   ├── ChatService.cs (183) / IChatService.cs
│   │   ├── JwtService.cs (97) / IJwtService.cs
│   │   └── UserStatusService.cs (111) / IUserStatusService.cs
│   ├── Models/Dto/                    # Auth / Chat / Message / Users — 23 DTO
│   └── wwwroot/
│       ├── app/                       # демо-SPA: main.js, ui.js (552), api.js, signalr.js, state.js
│       ├── signalr-docs.html (400)    # живая документация по хабу
│       ├── test-client.html (255)
│       └── signalr.min.js             # вендоренный клиент
│
├── BasicApi.Storage/                  # 🟢 Data access (net10.0, class library)
│   ├── Entities/                      # User, Chat, ChatMember, Message
│   ├── Interfaces/                    # IDbConnectionFactory, I{User,Chat,Message}Repository
│   ├── Repositories/
│   │   ├── ChatRepository.cs          # 289 — LATERAL-батч списка чатов + поиск
│   │   ├── MessageRepository.cs       # 322 — курсорная пагинация + FTS
│   │   └── UserRepository.cs          # 99
│   ├── Dto/                           # CursorDto, CursorResult<T>, ChatListResult, MessageWithSender…
│   ├── Services/NpgsqlConnectionFactory.cs
│   └── Migrations/                    # InitialCreate(1), AddFullTextSearch(2), AddChatSearchIndex(3)
│
└── BasicApi.Tests/                    # 🟣 xUnit + Moq — 103 теста
    ├── CursorDtoTests.cs
    ├── Features/  Auth(7) AuthLogoutValidate(4) Chats(13) ChatsCursor(6) Users(8) UsersStatus(6)
    ├── Hubs/      ChatHubTests.cs (643 строки, 18 тестов)
    ├── Middleware/ExceptionHandlingMiddlewareTests.cs (16)
    └── Services/  ChatServiceCursor(10) ChatServiceUserChats(6) ChatServiceChatDetails(2) Jwt(5)
```

---

## Architecture Breakdown

### 🔵 BasicApi — веб-приложение (точка входа)

- **Target:** `net10.0`, ASP.NET Core, `Nullable`+`ImplicitUsings` enabled, `GenerateDocumentationFile=true`, AnyCPU (специально, ради ARM64 Linux).
- **NuGet:** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.6, `System.IdentityModel.Tokens.Jwt` 8.17.0, `BCrypt.Net-Next` 4.1.0, `Swashbuckle.AspNetCore` 10.1.7, `FluentMigrator[.Runner][.Postgres]` 8.0.1, `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` 1.22.1.
- **Key Flow:**

```
Program.Main
  → AddApiServices(): Controllers (+кастомный InvalidModelStateResponseFactory)
                      → Swagger → JWT Bearer (+events для SignalR/401/403)
                      → SignalR (parallel=2, maxMessage=128KB)
                      → DI: repositories (Scoped), IChatService (Scoped),
                            IUserStatusService (Singleton), handlers (Scoped),
                            IDbConnectionFactory (Singleton)
                      → FluentMigrator → CORS "AllowAll"
  → app.Build()
  → UseMiddleware<ExceptionHandlingMiddleware>   // первым, до всего
  → IMigrationRunner.MigrateUp()                 // миграции на старте
  → UseDefaultFiles → UseStaticFiles → UseCors → Swagger UI
  → UseHttpsRedirection → UseAuthentication → UseAuthorization
  → MapHub<ChatHub>("/hubs/chat") → MapControllers
  → GET /signalr-docs, GET / → redirect /app/index.html
```

- **Key Files:**
  - `Program.cs` (59) — пайплайн, порядок корректный: обработчик ошибок первым, аутентификация до авторизации, хаб и контроллеры в конце.
  - `Extensions/ServiceExtensions.cs` (209) — вся регистрация DI. Интересное: `InvalidModelStateResponseFactory` отдаёт ProblemDetails с массивом `{code, message}` на каждое поле; `JwtBearerEvents.OnMessageReceived` достаёт токен из query string только для пути `/hubs/chat`; `OnChallenge`/`OnForbidden` бросают доменные исключения, чтобы 401/403 шли через тот же middleware и имели единый формат.
  - `Middleware/ExceptionHandlingMiddleware.cs` (115) — 5 типизированных `catch` + общий. В Development отдаёт `ex.Message` и `stackTrace`, в проде — «An unexpected error occurred.». Для 401 ставит `WWW-Authenticate`.
  - `Hubs/ChatHub.cs` (250) — primary constructor, `[Authorize]`. Presence с подсчётом соединений (мультивкладка), рассылка `UserOnlineChanged` только при реальной смене статуса, `SendMessage` шлёт полное сообщение в группу чата и укороченное превью (100 симв.) всем участникам для списка чатов, `Ping` для диагностики, статический `NotifyChatCreatedAsync` для вызова из REST-хендлера.
  - `Services/ChatService.cs` (183) — авторизация (`IsMemberAsync`) + маппинг + сборка курсора. `SearchChatsAsync` параллелит выборку и `COUNT(*)` через `Task.WhenAll`.
  - `Services/UserStatusService.cs` (111) — потокобезопасный in-memory трекер: `ConcurrentDictionary<Guid, ConcurrentDictionary<string,byte>>` как множество connectionId, аналогично для typing. Есть аккуратный `TryRemove(KeyValuePair)` для атомарного удаления пустого набора.
  - `Services/JwtService.cs` (97) — генерация (`sub`, `unique_name`, `email`, `jti`, `iat`), `ValidateToken` → `ClaimsPrincipal?`, `TryValidateToken` → `(bool, userId, username)` с фолбэком между `ClaimTypes.*` и `JwtRegisteredClaimNames.*`.

### 🟢 BasicApi.Storage — доступ к данным

- **Target:** `net10.0`, class library.
- **NuGet:** `Dapper` 2.1.72, `Npgsql` 10.0.2, `FluentMigrator[.Runner][.Postgres]` 8.0.1.
- **Purpose:** сущности, интерфейсы репозиториев, SQL-запросы на Dapper, фабрика подключений, версионированные миграции.
- **Key Files:**
  - `Repositories/ChatRepository.cs` (289) — ядро — константа `ChatListBaseSql`: один запрос со `LEFT JOIN LATERAL` для последнего сообщения, `LATERAL` для собеседника приватного чата и коррелированным подзапросом для `unread_count`. `GetUserChatsBatchedAsync` — это просто `SearchChatsBatchedAsync(userId, null, null, null)`, то есть список и поиск используют один и тот же план. `BuildSearchWhereClause` собирает WHERE по фильтру типа; значения передаются параметром `@query`, конкатенируется только структура.
  - `Repositories/MessageRepository.cs` (322) — курсор `(created_at, id)`, сравнение `created_at < @t OR (created_at = @t AND id < @id)` — устойчиво к одинаковым таймстампам. Выборка `limit+1` для `HasMore`. FTS: `to_tsvector('english', text) @@ plainto_tsquery('english', @query)` — выражение точь-в-точь совпадает с GIN-индексом из миграции 2, значит индекс реально используется. `COUNT(*)` считается только на первой странице.
  - `Dto/CursorDto.cs` (60) — `readonly struct`, реализует `IComparable`, кодирует `(ticks, 0L, guid)` в 32 байта → URL-safe Base64 без паддинга.
  - `Migrations/` — `InitialCreate` (4 таблицы, FK, составной PK `chat_members`, индекс `messages(chat_id, created_at DESC)`), `AddFullTextSearch` (GIN по `to_tsvector`), `AddChatSearchIndex` (расширение `pg_trgm` + частичный GIN по `chats.title WHERE type='group'` и GIN по `users.display_name/username`). У всех трёх реализован `Down()`.

### 🟣 BasicApi.Tests — юнит-тесты

- **Framework:** xUnit 2.9.3, Moq 4.20.72, coverlet 6.0.4, Microsoft.NET.Test.Sdk 17.14.1.
- **Стиль:** AAA, моки всех зависимостей через `Mock<I…>`, конструктор фикстуры создаёт SUT. Для `ChatHub` тесты подменяют `Clients`/`Groups`/`Context` моками SignalR-абстракций — это самый объёмный файл (643 строки, 18 тестов).
- **Покрытие по факту (103 теста):**

| Область | Тестов | Что проверяется |
|---|---|---|
| `ChatHubTests` | 18 | connect/disconnect, счётчик соединений, рассылка `UserOnlineChanged` только при смене статуса, join/leave с проверкой членства, send с обрезкой превью, typing, ping |
| `ExceptionHandlingMiddlewareTests` | 16 | все 6 типов исключений → статусы и `errorCode`, скрытие деталей вне Development, `traceId`, `WWW-Authenticate` |
| `ChatsHandlerTests` | 13 | создание/получение приватного чата, self-chat, mark-read, jump-to-date |
| `ChatServiceCursorTests` | 10 | авторизация, маппинг, `HasMore`, проброс курсора, валидация запроса поиска |
| `UsersHandlerTests` / `UsersHandlerStatusTests` | 8 / 6 | lookup по username/email, поиск, online-статус, фильтрация typing по своим чатам |
| `AuthHandlerTests` / `…LogoutValidateTests` | 7 / 4 | логин, неверный пароль, дубликаты username/email, фолбэк displayName, logout, validate |
| `ChatServiceUserChatsTests` / `ChatDetails` | 6 / 2 | маппинг списка чатов, отсутствие последнего сообщения, участники, 404 |
| `JwtServiceTests` | 5 | генерация, валидация, `TryValidateToken`, срок жизни |
| `CursorDtoTests` | 1 | roundtrip encode/decode |

**Чего нет:** ни одного интеграционного теста. Весь SQL (а это самая нетривиальная часть проекта — LATERAL-джойны, FTS, курсорные сравнения) не покрыт вообще: репозитории всегда замоканы.

---

## Key Design Patterns Observed

| Паттерн | Где | Описание |
|---|---|---|
| **Feature folders** | `Features/{Auth,Chats,Users}` | Контроллер и хендлер лежат рядом по фиче, а не по типу слоя |
| **Handler (тонкий контроллер)** | `*Handler.cs` | Контроллер только описывает контракт и клэмпит `limit`; логика в хендлере |
| **Domain exception hierarchy** | `Middleware/Exceptions/` | `DomainException` + `ErrorCode`, 5 наследников → HTTP-статус в одном месте |
| **Global error middleware → RFC 7807** | `ExceptionHandlingMiddleware` | Единый формат ошибок, `traceId`, dev/prod-разделение |
| **Cursor pagination** | `CursorDto`, `CursorResult<T>`, репозитории | Составной курсор `(created_at, id)`, выборка `limit+1` для `HasMore` |
| **Repository + Connection Factory** | `Storage/Repositories`, `NpgsqlConnectionFactory` | Dapper поверх `IDbConnection`, фабрика подменяется в тестах |
| **Batched query вместо N+1** | `ChatRepository.ChatListBaseSql` | `LATERAL` для последнего сообщения и собеседника, unread одним подзапросом |
| **Один SQL для списка и поиска** | `GetUserChatsBatchedAsync` → `SearchChatsBatchedAsync` | Список чатов — это поиск с пустым фильтром |
| **Validation error codes** | `ServiceExtensions.GetValidationErrorCode` | Стандартные сообщения ASP.NET → `REQUIRED`/`MAX_LENGTH`/… |
| **Auth events → доменные исключения** | `JwtBearerEvents` | 401/403 от фреймворка проходят через тот же формат ответа |
| **Токен в query string для WebSocket** | `OnMessageReceived` | Только для `/hubs/chat`, как рекомендует документация SignalR |
| **Presence с подсчётом соединений** | `UserStatusService` | Мультивкладка: offline рассылается только при закрытии последнего соединения |
| **Migrations on startup** | `Program.cs` | `MigrateUp()` в scope при старте |

---

## API Surface

| Метод | Маршрут | Auth | Описание |
|---|---|---|---|
| POST | `/api/auth/register` | anon | Регистрация, 201 + JWT |
| POST | `/api/auth/login` | anon | Логин, 200 + JWT |
| POST | `/api/auth/logout` | JWT | No-op (stateless JWT) |
| GET | `/api/auth/validate` | anon | Проверка токена из заголовка → `{userId, username, isValid}` |
| GET | `/api/users/GetUserId/{usernameOrEmail}` | JWT | Поиск id по логину/почте |
| GET | `/api/users/search?q&limit` | JWT | ILIKE-поиск по `display_name`/`username` |
| GET | `/api/users/status` | JWT | Кто из собеседников сейчас онлайн |
| GET | `/api/users/typing` | JWT | Кто печатает, с фильтром по своим чатам |
| GET | `/api/chats` | JWT | Список чатов: последнее сообщение + unread |
| GET | `/api/chats/search?q&type&limit` | JWT | Поиск по чатам (group → title, private → собеседник) |
| POST | `/api/chats/private/{userId}` | JWT | 200 если чат уже есть, 201 если создан |
| GET | `/api/chats/{chatId}` | JWT | Детали чата + участники |
| GET | `/api/chats/{chatId}/messages/cursor?cursor&limit` | JWT | История, курсорная пагинация |
| GET | `/api/chats/{chatId}/messages/at?date&limit` | JWT | «Перейти к дате» |
| GET | `/api/chats/{chatId}/messages/search?q&cursor&limit` | JWT | Полнотекстовый поиск в чате |
| POST | `/api/chats/{chatId}/read` | JWT | Отметить прочитанным до сообщения |
| WS | `/hubs/chat?access_token=…` | JWT | SignalR: `JoinChat`, `LeaveChat`, `SendMessage`, `Typing`, `Ping` → `MessageCreated`, `ChatListUpdated`, `ChatCreated`, `UserOnlineChanged`, `TypingChanged`, `Pong` |

---

## Strengths

- **Слои разделены честно.** Контроллер не знает про репозитории, сервис — про HTTP (кроме возврата `IActionResult` из хендлеров), хранилище — про DTO API. Зависимости идут в одну сторону.
- **Единый контракт ошибок.** Ни одного `try/catch` ради формирования ответа в бизнес-коде: доменное исключение + middleware. 401/403 от JWT-мидлвары специально заворачиваются в тот же формат — редко кто это доводит до конца.
- **Курсорная пагинация сделана правильно.** Составной ключ `(created_at, id)` вместо `OFFSET`, сравнение с тай-брейком по id, `limit+1` для `HasMore`, курсор непрозрачен для клиента (Base64, URL-safe, без паддинга).
- **SQL написан осознанно.** `LATERAL`-джойны вместо N+1, выражение FTS совпадает с индексным выражением, частичный trigram-индекс `WHERE type='group'`, `COUNT(*)` только на первой странице поиска.
- **Presence корректен для мультивкладки.** Считаются соединения, а не «булев онлайн»; событие рассылается только при реальном переходе 0↔1. Это распространённая ошибка, здесь её нет.
- **103 теста, все зелёные, быстрые (2 c).** Хаб покрыт особенно плотно, включая моки `IHubCallerClients`/`IGroupManager`.
- **Демо-фронтенд безопасен по построению:** DOM собирается хелпером `el()` через `createTextNode`, текст сообщений никогда не попадает в `innerHTML` — XSS через сообщение не проходит.
- **Инфраструктура воспроизводима:** docker-compose с healthcheck на `pg_isready` и `depends_on: service_healthy`, multi-stage Dockerfile с раздельным `restore`, порты настраиваются через env.
- **Документация живая:** `/signalr-docs`, Swagger с XML-комментариями и remarks-примерами, `readme.md`, `workflow/` с TDD-регламентом.

---

## Code Review: найденные проблемы

Отсортировано по серьёзности. Указано место и что именно предлагается.

### 🔴 Критично

| # | Проблема | Где | Что делать |
|---|---|---|---|
| 1 | **Секреты закоммичены в git.** `.env` (`DB_USER`, `DB_PASSWORD`) отслеживается git и отсутствует в `.gitignore`; `BasicApi/appsettings.json` содержит реальный `Jwt:Key` и строку подключения с паролем. Ключ подписи JWT в истории репозитория = любой, у кого есть доступ к репо, выпускает валидные токены за любого пользователя. | `.env`, `BasicApi/appsettings.json` | Добавить `.env` и `appsettings.*.json` (кроме шаблона) в `.gitignore`, удалить из индекса (`git rm --cached`), **сменить ключ JWT и пароль БД**, положить `.env.example`/`appsettings.Example.json` с плейсхолдерами. Для локальной разработки уже есть `UserSecretsId` — использовать его. |
| 2 | **Известная уязвимость в зависимости.** При сборке: `NU1903: Microsoft.OpenApi 2.4.1 — известная уязвимость высокого уровня (GHSA-v5pm-xwqc-g5wc)`. Приходит транзитивно через Swashbuckle 10.1.7. | `BasicApi.csproj` | Обновить Swashbuckle или добавить прямой `PackageReference` на исправленную версию `Microsoft.OpenApi`. Заодно включить `<NuGetAudit>` как ошибку сборки. |
| 3 | **CORS: `SetIsOriginAllowed(_ => true)` + `AllowCredentials()`.** Отражается любой Origin с разрешением на credentials — это ровно то, что спецификация CORS запрещает делать через `*`, обойдённое вручную. Сейчас ущерб ограничен тем, что токен лежит не в куках, но политика применяется и к хабу. | `ServiceExtensions.cs:102-111` | Читать список разрешённых origin из конфигурации; `AllowCredentials` оставить только для них. |

### 🟠 Важно

| # | Проблема | Где | Что делать |
|---|---|---|---|
| 4 | **Нет CI.** Папка `.github/workflows/` пустая. Ничто не мешает влить код, который не собирается. | — | Workflow: `dotnet build` + `dotnet test` + `dotnet list package --vulnerable` на PR; сборка образа на master. |
| 5 | **Нет интеграционных тестов на БД.** Самая сложная часть (LATERAL-джойны, FTS, курсорные сравнения, миграции) не выполняется ни разу в тестах. Опечатка в SQL обнаружится только в рантайме. | `BasicApi.Tests` | Testcontainers для PostgreSQL: прогон миграций + тесты репозиториев (курсор через страницу с одинаковыми `created_at`, `unread_count`, поиск). |
| 6 | **`timestamp without time zone` + `DateTime.UtcNow`.** Колонки `created_at` созданы как `AsDateTime()` (= `timestamp`), а в параметрах уходит `DateTime` с `Kind=Utc` (и `CursorDto.Decode` тоже возвращает `Kind=Utc`). Npgsql выводит для такого параметра тип `timestamptz`, и Postgres неявно приводит его по таймзоне сессии. Пока `TimeZone=UTC` — всё сходится; на сервере с другой TZ курсоры и «переход к дате» поедут на несколько часов. | `InitialCreate.cs`, все репозитории | Перевести временные колонки на `timestamptz` (миграция `ALTER TABLE … TYPE timestamptz USING … AT TIME ZONE 'UTC'`) либо явно задавать `Kind=Unspecified`. Первый вариант правильнее. |
| 7 | **Presence живёт в памяти процесса.** `UserStatusService` — синглтон с `ConcurrentDictionary`. При двух инстансах пользователь виден онлайн только «своему»; `Clients.User(...)`/`Clients.Group(...)` тоже не доходят между инстансами. | `UserStatusService.cs`, `ChatHub` | Redis backplane для SignalR + Redis для presence (SETEX по connectionId). До этого момента честно фиксировать в README, что деплой однонодовый. |
| 8 | **Гонка при создании приватного чата.** Между `GetPrivateChatAsync` (null) и `CreateAsync` два параллельных запроса создадут два чата между теми же людьми — уникального ограничения нет. | `ChatsHandler.cs:31-45` | Уникальный индекс на нормализованную пару участников (например, отдельная колонка `private_key = least(u1,u2)||greatest(u1,u2)`) + обработка нарушения как «чат уже есть». Или advisory-lock на пару id. |
| 9 | **Дубликаты при регистрации дают 500, а не 409.** Проверки `GetByUsernameOrEmailAsync` не атомарны; при гонке сработает уникальный индекс БД и `PostgresException` уйдёт в общий `catch` → 500 INTERNAL_ERROR. | `AuthHandler.RegisterAsync` | Ловить `PostgresException` с `SqlState == 23505` и бросать `ConflictException` с нужным кодом. |
| 10 | **`CancellationToken` объявлен, но не используется.** `IUserRepository` принимает `ct` во всех методах — ни один не передаёт его в Dapper (`new CommandDefinition(sql, param, cancellationToken: ct)`). В `IChatRepository`/`IMessageRepository` токена нет вовсе, и по цепочке контроллер→сервис он не пробрасывается. Отменённые запросы продолжают занимать соединение. | `UserRepository.cs`, интерфейсы | Добавить `CancellationToken` во все репозитории и пробросить `HttpContext.RequestAborted` от контроллера. |
| 11 | **`IsRead` всегда `false`.** Захардкожено в четырёх местах (`ChatService` ×2, `ChatHub` ×2) с `// TODO`. Функциональность «прочитано» на чтении не работает, хотя `last_read_message_id` пишется. | `ChatService.cs:79,120`, `ChatHub.cs:162,175` | Джойн с `chat_members.last_read_message_id` (сравнение по `created_at` прочитанного сообщения) — данные для этого уже есть. |
| 12 | **Полнотекстовый поиск жёстко на `'english'`.** Приложение русскоязычное, но `to_tsvector('english', text)` не выполняет стемминг русского и режет стоп-слова не те. Поиск «сообщения»/«сообщение» будет считать это разными словами. | `MessageRepository.SearchMessagesCursorAsync`, `AddFullTextSearch.cs` | Вынести конфигурацию словаря в константу и использовать `'russian'` (или `'simple'` как компромисс); индекс и запрос обязаны использовать **одно и то же** выражение, иначе индекс перестанет применяться. |
| 13 | **Нет rate limiting.** `/api/auth/login` не ограничен — брутфорс пароля ничем не сдерживается; BCrypt лишь делает его дорогим для сервера. | `Program.cs` | `builder.Services.AddRateLimiter(...)` + жёсткая политика на `/api/auth/*`. |

### 🟡 Средне

| # | Проблема | Где | Что делать |
|---|---|---|---|
| 14 | **Нет индекса `chat_members(user_id)`.** PK — `(chat_id, user_id)`, поэтому запросы, фильтрующие только по `user_id` (список чатов, `GetAllChatMembersAsync`, typing-фильтр), не могут им воспользоваться и идут seq scan. | `InitialCreate.cs` | `CREATE INDEX ix_chat_members_user_id ON chat_members(user_id)` отдельной миграцией. |
| 15 | **`IsMemberAsync` через `COUNT(1)`.** Считает все совпадения ради булева ответа. | `ChatRepository.cs:103-108` | `SELECT EXISTS(SELECT 1 FROM chat_members WHERE …)`. |
| 16 | **N+1 вставка участников.** `CreateAsync` делает отдельный `INSERT` на каждого участника в цикле (в транзакции). | `ChatRepository.cs:83-91` | Dapper умеет `ExecuteAsync(sql, listOfParams)`, либо `unnest(@ids)` одним запросом. |
| 17 | **`GetChatDetailsAsync` делает лишний запрос.** Сначала `GetByIdAsync`, потом `IsMemberAsync`, потом `GetChatParticipantsAsync` — три обращения к БД там, где хватает одного-двух. | `ChatService.cs:34-58` | Объединить чат и участников одним запросом; членство проверяется наличием себя среди участников. |
| 18 | **Мёртвый код.** Не вызывается нигде: `ChatRepository.GetUnreadCountAsync`, `ChatRepository.GetCompanionNameAsync`, `MessageRepository.GetUnreadCountAsync` (дубль первого), `MessageRepository.GetMessagesCursorAsync` (осталась от версии без JOIN на sender), DTO `SendMessageDto`. Они же тянутся в интерфейсы и заставляют моки в тестах. | `Storage/Repositories/*`, `Models/Dto/Message/SendMessageDto.cs` | Удалить вместе с объявлениями в интерфейсах. |
| 19 | **`TotalCount` только на первой странице поиска.** На последующих страницах возвращается `0`, а не реальное число — клиент, показывающий «найдено N», увидит ноль при пролистывании. | `MessageRepository.cs:291-302`, `SearchMessagesResponseDto` | Либо документировать явно (`totalCount` валиден только при `cursor == null`), либо считать всегда, либо отдавать `null` вместо `0`, чтобы отличать «не считали» от «ничего не нашли». |
| 20 | **`GetTypingStatusAsync(userId)` игнорирует свой аргумент.** Сервис отдаёт снимок **всех** чатов системы, а приватность обеспечивается фильтром в `UsersHandler`. Контракт вводит в заблуждение, а на больших объёмах это копирование всей карты на каждый вызов. | `UserStatusService.cs:31-45`, `IUserStatusService` | Передавать в сервис набор `chatIds` пользователя и фильтровать внутри, либо честно назвать метод `GetAllTypingAsync()` без параметра. |
| 21 | **Опрос статусов вместо push.** `/api/users/status` и `/api/users/typing` предполагают polling, хотя те же события уже рассылаются через хаб (`UserOnlineChanged`, `TypingChanged`). Двойной источник правды. | `UsersController` | Оставить REST только как «снимок при загрузке страницы» и зафиксировать это в описании эндпоинта. |
| 22 | **Typing не истекает.** `SetTypingAsync(chatId, userId, true)` снимается только явным `false`. Если клиент отвалился во время печати, пользователь останется «печатает» навсегда — `OnDisconnectedAsync` не чистит typing. | `UserStatusService`, `ChatHub.OnDisconnectedAsync` | Чистить typing при отключении и/или хранить с TTL (метка времени + фоновая чистка). |
| 23 | **Ошибки хаба уходят клиенту как есть.** `EnableDetailedErrors = true` захардкожено — в проде клиенту поедут тексты внутренних исключений. | `ServiceExtensions.cs:68` | `options.EnableDetailedErrors = env.IsDevelopment()`. |
| 24 | **`UseHttpsRedirection` при HTTP-only контейнере.** В compose открыт только `http://+:8080`, HTTPS-порт не сконфигурирован — редирект либо не сработает, либо будет писать warning на каждый запрос. | `Program.cs:37`, `docker-compose.yml` | Не включать редирект, когда HTTPS-порт не задан (или ставить его за reverse-proxy и убрать из приложения). |
| 25 | **Миграции применяются каждым инстансом на старте.** При параллельном запуске двух реплик возможен конфликт на `VersionInfo`. | `Program.cs:22-27` | Вынести в отдельный шаг деплоя/init-контейнер, либо обернуть в advisory lock. |
| 26 | **`/signalr-docs` читает файл с диска на каждый запрос** и обращается к `app.Environment.WebRootPath` без проверки на null. | `Program.cs:44-50` | Файл уже лежит в `wwwroot` и раздаётся `UseStaticFiles` — достаточно редиректа на `/signalr-docs.html`. |

### 🟢 Мелочи и стиль

| # | Наблюдение | Где |
|---|---|---|
| 27 | **Битая кодировка в комментариях.** В `ServiceExtensions.cs` часть русских комментариев сохранена не в UTF-8: `//�������`, `// SignalR ������� ����� ����� query string`. Файл смешивает читаемые и нечитаемые комментарии. | `ServiceExtensions.cs:101,136,154,162` |
| 28 | **Рваные отступы.** В нескольких местах сохранились артефакты правок: `Program.cs:22` и `41`, `ChatsController.cs:30`, `ChatService.cs:34,89,131,141,167`, `ChatsHandler.cs:28,33,78,99,109`, `ChatRepository.cs:11,110,178`. Форматирование не единообразно. | много файлов |
| 29 | **Смешение языков.** Комментарии и XML-doc частью на английском, частью на русском — иногда в одном файле. Стоит зафиксировать один язык в `workflow/`. | `ChatHub.cs`, `AuthHandler.cs` |
| 30 | **Warning при сборке:** `CS1570` — некорректный XML в `UsersController.cs:33` (`&limit` не экранирован → должно быть `&amp;limit`). | `UsersController.cs:33` |
| 31 | **Warning `MSB3270`** про архитектуру `IBM.Data.Db2.dll` — приходит из глобального NuGet-кэша/окружения, к самому решению отношения не имеет, но шумит в логе сборки. | сборка |
| 32 | **`ClaimsPrincipalExtensions.GetUserId` бросает `ArgumentNullException`** на `Guid.Parse(null!)`, если клейма нет. На `[Authorize]`-эндпоинтах это недостижимо, но исключение получится 500-е, а не 401. | `ClaimsPrincipalExtensions.cs:12` |
| 33 | **`WHERE` для поиска без фильтра типа** полагается на приоритет `AND` над `OR`: `(type='group' AND … OR type='private' AND (…))`. Семантически верно, но читается плохо и ломается от любой правки. | `ChatRepository.cs:252` |
| 34 | **`last_login_at` никогда не обновляется** — колонка есть в схеме и в сущности, но при логине не пишется. | `AuthHandler.LoginAsync` |
| 35 | **`GetUserId/{usernameOrEmail}` позволяет перебирать пользователей** (проверять существование логина/почты по 200 vs 404). Под `[Authorize]`, так что риск невысок, но `/api/users/search` покрывает тот же сценарий лучше. | `UsersController.cs:19` |
| 36 | **`RegisterAsync` проверяет username методом «username OR email»** — регистрация под именем, совпадающим с чужим email, вернёт `USERNAME_TAKEN`, что вводит в заблуждение. | `AuthHandler.cs:38` |
| 37 | **Нет ограничения длины текста сообщения** ни на хабе, ни в схеме (`text` = `varchar(max)`); защищает только `MaximumReceiveMessageSize = 128KB`. | `ChatHub.SendMessage` |
| 38 | **`nextCursor` возвращается даже при `hasMore == false`** — клиент, ориентирующийся на непустой курсор, сделает лишний запрос. | `ChatService.cs:82-94` |
| 39 | **`docker-compose` монтирует `${APPDATA}`** — на Linux/macOS переменная пуста, том смонтируется некорректно. Прод-профиль стоит отделить от dev. | `docker-compose.yml:33-37` |
| 40 | **`postgres:latest`** — незакреплённая версия образа, обновление мажора сломает данные/поведение без предупреждения. | `docker-compose.yml:3` |
| 41 | Прежняя версия этого документа советовала «убрать лишний `OrderBy(m => m.CreatedAt)`» в `ChatService` — совет неверный: SQL отдаёт `DESC`, а API обязан вернуть по возрастанию, `OrderBy` здесь необходим. Курсор при этом берётся из `messages[^1]` **до** переупорядочивания — то есть от самого старого сообщения. Это корректно и стоит закрепить комментарием, чтобы не «оптимизировали» повторно. | `ChatService.cs:82-94` |

---

## Приоритетный план

1. **Секреты** — убрать `.env`/`appsettings.json` из git, ротировать `Jwt:Key` и пароль БД, перейти на user-secrets/переменные окружения (#1).
2. **Обновить `Microsoft.OpenApi`** и включить NuGet-аудит в сборке (#2).
3. **CI на GitHub Actions**: build + test + audit на каждый PR (#4).
4. **Закрыть CORS** до списка доверенных origin (#3), выключить `EnableDetailedErrors` вне dev (#23), добавить rate limiting на `/api/auth/*` (#13).
5. **Интеграционные тесты на Testcontainers** для репозиториев и миграций (#5) — без них любая правка SQL слепая.
6. **`timestamptz`** миграцией + индекс `chat_members(user_id)` (#6, #14).
7. **Уникальность приватного чата** и корректный 409 при дубликатах регистрации (#8, #9).
8. **Доделать `IsRead`** и русский словарь FTS (#11, #12) — это уже про функциональность, а не про инфраструктуру.
9. Уборка: мёртвый код, битая кодировка, отступы, `CancellationToken` (#18, #27, #28, #10).

---

## Technology Stack

| Технология | Версия | Назначение |
|---|---|---|
| .NET / ASP.NET Core | 10.0 | Платформа приложения |
| PostgreSQL | `latest` (docker) | Основная БД |
| Dapper | 2.1.72 | Микро-ORM |
| Npgsql | 10.0.2 | ADO.NET-провайдер PostgreSQL |
| FluentMigrator (+Runner, +Postgres) | 8.0.1 | Версионированные миграции |
| BCrypt.Net-Next | 4.1.0 | Хеширование паролей |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.6 | JWT-аутентификация |
| System.IdentityModel.Tokens.Jwt | 8.17.0 | Выпуск/валидация токенов |
| SignalR | встроенный | WebSocket-транспорт реального времени |
| Swashbuckle.AspNetCore | 10.1.7 | Swagger/OpenAPI (⚠️ тянет уязвимый Microsoft.OpenApi 2.4.1) |
| xUnit | 2.9.3 | Тестовый фреймворк |
| Moq | 4.20.72 | Моки |
| coverlet.collector | 6.0.4 | Покрытие |
| Microsoft.NET.Test.Sdk | 17.14.1 | Тестовый SDK |
| Docker / docker-compose | — | Контейнеризация и локальный запуск |
| Ванильный JS (ES-модули) | — | Демо-клиент в `wwwroot/app` |
