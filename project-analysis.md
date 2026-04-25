# Project Analysis: BasicChatApi

> Real-time chat Web API with JWT authentication, SignalR, PostgreSQL, and cursor-based pagination

## Overview

BasicChatApi is a .NET 10 ASP.NET Core Web API that provides real-time private messaging functionality. The architecture follows a **Feature-based** layout within the API project, with a separate **Storage** layer handling data access via Dapper over PostgreSQL, and a **Tests** project using xUnit + Moq.

The solution comprises 3 projects: `BasicApi` (API entry point with controllers, handlers, services, SignalR hub), `BasicApi.Storage` (database entities, Dapper repositories, FluentMigrator migrations, DTOs), and `BasicApi.Tests` (xUnit unit tests). The API uses JWT Bearer authentication, supports cursor-based pagination for messages, and provides SignalR endpoints for real-time communication.

Key technologies: .NET 10, ASP.NET Core, SignalR, PostgreSQL, Dapper (micro-ORM), FluentMigrator, Swagger/OpenAPI, xUnit, Moq.

## Project Structure

```
BasicChatApi/
├── BasicApi/                         # Web API entry point
│   ├── Extensions/                   # DI registration, Swagger, JWT config
│   ├── Features/
│   │   ├── Auth/                     # Login/Register endpoints
│   │   ├── Chats/                    # Chat CRUD, messages with cursor pagination
│   │   └── Users/                    # User lookup by username/email
│   ├── Hubs/
│   │   └── ChatHub.cs               # SignalR real-time hub
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs
│   ├── Models/Dto/                   # API-facing DTOs (Auth, Chat, Message)
│   ├── Services/                     # Business logic (JwtService, ChatService)
│   ├── Program.cs                    # App bootstrap
│   └── Dockerfile
├── BasicApi.Storage/                 # Data access layer
│   ├── Dto/                          # Storage DTOs, CursorDto, CursorResult
│   ├── Entities/                     # DB entities (User, Chat, ChatMember, Message)
│   ├── Interfaces/                   # Repository interfaces + IDbConnectionFactory
│   ├── Migrations/                   # FluentMigrator migrations
│   ├── Repositories/                 # Dapper implementations
│   └── Services/                     # NpgsqlConnectionFactory
├── BasicApi.Tests/                   # xUnit unit tests
│   ├── CursorDtoTests.cs
│   ├── ChatsHandlerCursorTests.cs
│   └── ChatServiceCursorTests.cs
├── docker-compose.yml                # PostgreSQL + API containers
├── BasicApi.sln
└── workflow/                         # TDD workflow documentation
```

## Architecture Breakdown

### 🔵 BasicApi — entry point, main app
- **Target:** .NET 10, ASP.NET Core
- **NuGet:** 
  - `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.6
  - `BCrypt.Net-Next` 4.1.0
  - `System.IdentityModel.Tokens.Jwt` 8.17.0
  - `Swashbuckle.AspNetCore` 10.1.7
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1
  - `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` 1.22.1
- **Key Flow:**
  ```
  Program.cs → AddApiServices (DI, JWT, Swagger, SignalR, FluentMigrator)
    → ExceptionHandlingMiddleware
    → MigrateUp (dev only)
    → StaticFiles → CORS → SwaggerUI → HttpsRedirection → Auth → Authorization
    → MapHub<ChatHub>("/hubs/chat") → MapControllers → Run
  ```
- **Key Files:**
  - `Program.cs` — Entry point, ~35 lines. Configures middleware pipeline and service registrations.
  - `Extensions/ServiceExtensions.cs` — All DI registrations, JWT config with SignalR token query string support, CORS "AllowAll" policy. ~80 lines.
  - `Extensions/SwaggerExtensions.cs` — SwaggerGen + SwaggerUI with JWT security definition. ~75 lines.
  - `Middleware/ExceptionHandlingMiddleware.cs` — Global error handling returning ProblemDetails (RFC 9110), maps custom exceptions (NotFoundException, BadRequestException, ConflictException, UnauthorizedAccessException) to HTTP status codes. ~120 lines.
  - `Features/Auth/AuthHandler.cs` — Login/Register with BCrypt password hashing. ~55 lines.
  - `Features/Chats/ChatsHandler.cs` — Chat operations + cursor-based pagination + jump-to-date. ~90 lines.
  - `Features/Chats/ChatsController.cs` — REST endpoints with XML documentation. ~100 lines.
  - `Hubs/ChatHub.cs` — SignalR hub: JoinChat, LeaveChat, SendMessage, Typing, online status tracking. ~100 lines.
  - `Services/JwtService.cs` — JWT generation/validation with configurable expiry. ~65 lines.
  - `Services/ChatService.cs` — Business logic for chat listing (batched query), chat details, cursor-based message pagination. ~80 lines.

### 🟢 BasicApi.Storage — data access layer
- **Target:** .NET 10
- **NuGet:**
  - `Dapper` 2.1.72
  - `Npgsql` 10.0.2
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1
- **Purpose:** Database entities, Dapper-based repositories, FluentMigrator migrations, storage DTOs
- **Key Files:**
  - `Repositories/MessageRepository.cs` — Cursor-based pagination with composite key (created_at, id), JOIN for sender names, fetch N+1 technique for HasMore detection. ~160 lines.
  - `Repositories/ChatRepository.cs` — Batched chat list query with LATERAL JOINs for last message and companion name, unread count via subquery. ~160 lines.
  - `Repositories/UserRepository.cs` — User lookup by username/email, user creation. ~55 lines.
  - `Dto/CursorDto.cs` — URL-safe Base64 encoding of (DateTime, Guid) composite cursor. ~55 lines.
  - `Dto/CursorResult.cs` — Generic wrapper with Items + Extra for HasMore detection.
  - `Entities/*.cs` — 4 entity classes (User, Chat, ChatMember, Message).
  - `Migrations/InitialCreate.cs` — Initial schema: users, chats, chat_members, messages tables with indexes and foreign keys.
  - `Services/NpgsqlConnectionFactory.cs` — Opens Npgsql connection eagerly.

### 🟣 BasicApi.Tests — unit tests
- **Framework:** xUnit 2.9.3 + Moq 4.20.72 + Microsoft.NET.Test.Sdk 17.14.1 + coverlet.collector 6.0.4
- **Infrastructure:**
  - `CursorDtoTests.cs` — 8 tests for encode/decode roundtrip, URL safety, edge cases (null, empty, whitespace, invalid base64), CompareTo logic.
  - `ChatServiceCursorTests.cs` — 7 tests for ChatService pagination: authorization check, message mapping, HasMore/cursor generation, chronological ordering, empty chat, cursor passthrough.
  - `ChatsHandlerCursorTests.cs` — 4 tests for handler layer: OK result wrapping, unauthorized exception propagation, HasMore/nocursor scenarios, empty chat.
- **Test coverage:**
  - CursorDto encode/decode roundtrip — tests URL safety, null/empty/whitespace edge cases, comparison logic
  - ChatService pagination — member check throws, message mapping, HasMore detection, chronological reordering (ASC), empty chat, cursor forwarding
  - Handler integration — OkObjectResult wrapping, exception propagation to middleware, paginated response structure

## Key Design Patterns Observed

| Pattern | Where | Description |
|---------|-------|-------------|
| **Feature-based layout** | `BasicApi/Features/{Feature}/` | Per-feature folders with Controller + Handler, keeping related endpoints cohesive |
| **Handler pattern** | `BasicApi/Features/*/Handler.cs` | Thin controllers delegate to Handler classes that orchestrate services/repos and return `IActionResult` |
| **Cursor-based pagination** | `BasicApi.Storage/Dto/CursorDto.cs`, `MessageRepository.cs` | Composite key (created_at, id) pagination with URL-safe Base64 encoding, fetch N+1 for HasMore detection |
| **Repository pattern** | `BasicApi.Storage/Interfaces/*.cs`, `Repositories/*.cs` | Interface-based data access with Dapper implementation |
| **Global error handling middleware** | `Middleware/ExceptionHandlingMiddleware.cs` | Centralized ProblemDetails responses for known exception types, stack trace stripped in non-dev |
| **Batched query (anti-N+1)** | `ChatRepository.GetUserChatsBatchedAsync` | Single SQL query with LATERAL JOINs replaces previous N+1 pattern for chat listing |
| **Static in-memory state** | `ChatHub._onlineUsers` | Static Dictionary tracks online users by userId → connectionId |
| **Primary constructor DI** | All Handlers, Services, Repositories | .NET 10 primary constructors for clean DI declaration |
| **Custom exception types** | `Middleware/ExceptionHandlingMiddleware.cs` | NotFoundException, BadRequestException, ConflictException for semantic exception-to-status mapping |

## Strengths

1. **Clean feature-based architecture** with clear separation: Controllers → Handlers → Services → Repositories (via interfaces).
2. **Production-grade cursor pagination** with composite key (created_at, id), URL-safe encoding, and N+1 detection using fetch `limit+1` — avoids duplicates and missed records.
3. **Batched SQL queries** eliminate N+1 problems in chat listing (single query with LATERAL JOIN for companion name and last message).
4. **Global exception handling middleware** provides consistent ProblemDetails responses across all endpoints, with environment-aware detail stripping.
5. **SignalR integration** with JWT token passthrough via query string, real-time message delivery, typing indicators, and online status tracking.
6. **FluentMigrator** for versioned, repeatable database migrations with proper rollback support.
7. **Comprehensive cursor pagination tests** covering edge cases: empty chat, HasMore detection, chronological ordering, URL safety, composite key comparison.

## Areas for Improvement

| Concern | Current State | Suggested Improvement |
|---------|--------------|----------------------|
| **Connection lifetime management** | `IDbConnection` is registered as `Scoped` but `NpgsqlConnectionFactory` eagerly opens the connection in the constructor. The connection is injected directly into repositories (not a factory), leading to potential connection leaks if a repository method throws mid-operation. | Use `DbConnection` injection properly with Dapper's built-in connection management, or inject `IDbConnectionFactory` into repositories so connections are created and disposed per query. Also, Npgsql connection pooling makes eager opening unnecessary. |
| **Missing CI/CD pipeline** | No `.github/workflows/` files exist. The `workflow/` folder contains TDD process documentation only. | Add GitHub Actions CI pipeline for build → test → docker build/push. Even a minimal pipeline would catch compilation errors early. |
| **In-memory state in SignalR Hub** | `_onlineUsers` is a static `Dictionary<Guid, string>` in `ChatHub`. This breaks in multi-instance deployments (horizontal scaling) and is not thread-safe for concurrent access. | Use a distributed cache (Redis) or a SignalR backplane for cross-instance state. At minimum, use `ConcurrentDictionary` and document the single-instance limitation. |
| **No refresh token mechanism** | JWT tokens expire based on a configurable `ExpiryMinutes` (default 60 min). There is no refresh token flow, so users must re-authenticate after token expiry. | Implement refresh tokens (e.g., HTTP-only cookie with long-lived refresh token, short-lived access token). Alternatively, extend expiry or document the trade-off. |
| **"No auth" on UsersController.GetUserId** | `GetUserId` endpoint requires `[Authorize]` but returns a user ID from any username/email — this leaks user identity enumeration. | Add rate limiting, or require the caller to be the user themselves (compare with `User.GetUserId()`), or at least document the privacy implication. |
| **Missing appsettings schema documentation** | Configuration schema (Jwt, Swagger, ConnectionStrings sections) is defined in code but not documented separately. | Create a `appsettings.schema.json` or a configuration documentation section in README showing all config keys with descriptions and defaults. |
| **IsMember check duplication** | `ChatsHandler.GetMessagesAtAsync` and `ChatService.GetChatMessagesCursorAsync` both check `IsMemberAsync`. The handler calls it, then also calls the service which calls it again — double database roundtrip. | Remove the redundant check in `ChatsHandler.GetMessagesAtAsync` since `ChatService.GetChatMessagesCursorAsync` already performs it. |
| **No observability/logging beyond console** | Only FluentMigrator logging is explicitly added (`lb.AddConsole`). No structured logging (Serilog), metrics (Prometheus/OpenTelemetry), or distributed tracing is configured. | Add Serilog for structured logging, OpenTelemetry for metrics/tracing (minimal setup: OTel SDK + Prometheus exporter + Jaeger/Zipkin for traces). |
| **CursorPaginatedResponse<T> uses `before` terminology in XML docs** | `CursorPaginatedResponse.NextCursor` XML comment says "pass as `before` to fetch the next (older) page" but the query parameter is named `cursor`. | Fix XML doc to reflect actual parameter name `cursor` instead of `before` to avoid confusion. |
| **No health check endpoint** | The only root endpoint redirects to Swagger. No `/health` or `/ready` endpoint for container orchestration health probes. | Add ASP.NET Core Health Checks (`AddHealthChecks`) with PostgreSQL ping probe and map `/health` and `/ready` endpoints. Docker healthcheck for API is also missing (only PostgreSQL has healthcheck). |
| **Password validation mismatch** | `LoginRequestDto` requires `[MinLength(6)]` for password, but `RegisterRequestDto` also requires `[MinLength(6)]`. No upper bound or complexity requirement on registration, yet no validation at all on login side (only required). | Add consistent password validation between registration and login. Consider adding `[MaxLength]` to prevent DoS via extremely long passwords. |
| **IsRead always false** | `ChatService.GetChatMessagesCursorAsync` sets `IsRead = false` with a `// TODO: resolve actual read status` comment. Read status is never actually resolved in the response. | Resolve `IsRead` by joining with `chat_members.last_read_message_id` in the cursor query, so the client knows which messages the current user has read. |
| **Tests only cover cursor pagination** | All 19 tests are focused on cursor pagination. Auth, chat creation, SignalR hub, and User lookup are untested. | Add tests for AuthHandler (login, register, password validation), ChatHandler (create chat, get chat, mark read), UserHandler, and ChatHub SignalR methods. |
| **No integration tests** | All tests use Moq mocks — no database integration tests verify that SQL queries actually work against a real PostgreSQL schema. | Add integration tests using `Testcontainers.PostgreSQL` (or a real test DB) to verify actual SQL query correctness and migration compatibility. |
| **Transactions in ChatRepository.CreateAsync** | Manual transaction management with try/catch/rollback. Dapper's `transaction: connection.BeginTransaction()` is used but if the connection is disposed mid-operation the transaction might not roll back. | Consider using `connection.BeginTransaction()` with a `using` statement (returns `DbTransaction` that auto-rollbacks on dispose), or use the newer `NpgsqlTransaction` async API. |

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET / ASP.NET Core | 10.0 | Application framework |
| Npgsql (PostgreSQL provider) | 10.0.2 | Database driver |
| PostgreSQL | latest (Docker) | Relational database |
| Dapper | 2.1.72 | Micro-ORM for SQL queries |
| FluentMigrator | 8.0.1 | Database migrations |
| SignalR | (built-in) | Real-time web communication |
| JWT Bearer Auth | 10.0.6 | Authentication (Microsoft.AspNetCore.Authentication.JwtBearer) |
| BCrypt.Net-Next | 4.1.0 | Password hashing |
| Swashbuckle (Swagger) | 10.1.7 | API documentation UI |
| xUnit | 2.9.3 | Unit testing framework |
| Moq | 4.20.72 | Mocking framework |
| Docker Compose | latest | Local development container orchestration |
| System.IdentityModel.Tokens.Jwt | 8.17.0 | JWT token generation/validation |
