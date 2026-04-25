# Project Analysis: BasicApi

> Real-time chat API with JWT authentication, cursor-based pagination, and PostgreSQL storage

## Overview

BasicApi is a .NET 10 ASP.NET Core Web API that implements a real-time private messaging system with SignalR hubs for live communication. The project follows a **feature-based vertical slice architecture** within the main API project, with a separate storage layer handling all data access via Dapper and PostgreSQL.

The solution consists of three projects: `BasicApi` (ASP.NET Core host with controllers, SignalR hubs, and business logic), `BasicApi.Storage` (data access layer with Dapper repositories, FluentMigrator migrations, and Npgsql), and `BasicApi.Tests` (xUnit test suite with Moq). The API uses JWT bearer tokens for authentication, supports cursor-based pagination for message history, and is fully containerized with Docker Compose for local development with PostgreSQL.

## Project Structure

```
BasicApi/
├── BasicApi.sln
├── .env (configuration)
├── .gitignore
├── docker-compose.yml
├── readme.md
├── test-client.html
├── CursorPaginationChanges.md
├── launchSettings.json
├── BasicApi/
│   ├── BasicApi.csproj
│   ├── Program.cs
│   ├── Dockerfile
│   ├── Controllers/
│   │   └── ProductsController.cs
│   ├── Extensions/
│   │   ├── ServiceExtensions.cs
│   │   ├── SwaggerExtensions.cs
│   │   └── ClaimsPrincipalExtensions.cs
│   ├── Features/
│   │   ├── Auth/
│   │   │   ├── AuthController.cs
│   │   │   └── AuthHandler.cs
│   │   ├── Chats/
│   │   │   ├── ChatsController.cs
│   │   │   └── ChatsHandler.cs
│   │   └── Users/
│   │       ├── UsersController.cs
│   │       └── UsersHandler.cs
│   ├── Hubs/
│   │   └── ChatHub.cs
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs
│   ├── Models/
│   │   ├── Product.cs
│   │   ├── User.cs
│   │   └── Dto/
│   │       ├── Auth/
│   │       │   ├── AuthResponseDto.cs
│   │       │   ├── LoginRequestDto.cs
│   │       │   ├── RegisterRequestDto.cs
│   │       │   └── ErrorResponseDto.cs
│   │       ├── Chat/
│   │       │   ├── ChatDetailDto.cs
│   │       │   ├── ChatListItemDto.cs
│   │       │   ├── ChatParticipantDto.cs
│   │       │   └── CreatePrivateChatDto.cs
│   │       └── Message/
│   │           ├── MessageDto.cs
│   │           ├── SendMessageDto.cs
│   │           ├── MarkMessageReadDto.cs
│   │           └── CursorPaginatedResponse.cs
│   └── Services/
│       ├── IChatService.cs
│       ├── ChatService.cs
│       ├── IJwtService.cs
│       └── JwtService.cs
├── BasicApi.Storage/
│   ├── BasicApi.Storage.csproj
│   ├── Dto/
│   │   ├── ChatParticipantDto.cs
│   │   ├── CursorDto.cs
│   │   └── CursorResult.cs
│   ├── Entities/
│   │   ├── Chat.cs
│   │   ├── ChatMember.cs
│   │   ├── Message.cs
│   │   └── User.cs
│   ├── Interfaces/
│   │   ├── IChatRepository.cs
│   │   ├── IDbConnectionFactory.cs
│   │   ├── IMessageRepository.cs
│   │   └── IUserRepository.cs
│   ├── Migrations/
│   │   └── InitialCreate.cs
│   ├── Repositories/
│   │   ├── ChatRepository.cs
│   │   ├── MessageRepository.cs
│   │   └── UserRepository.cs
│   └── Services/
│       └── NpgsqlConnectionFactory.cs
└── BasicApi.Tests/
    ├── BasicApi.Tests.csproj
    ├── ChatServiceCursorTests.cs
    ├── ChatsHandlerCursorTests.cs
    └── CursorDtoTests.cs
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
- **Key Flow:**
  ```
  Program.Main → AddApiServices() → builder.Build()
  → ExceptionHandlingMiddleware → FluentMigrateUp (dev) → StaticFiles
  → CORS → Swagger → HttpsRedirection → Auth → Authorization
  → MapHub<ChatHub>("/hubs/chat") → MapControllers()
  ```
- **Key Files:**
  - `Program.cs` (49 lines) — Application bootstrap, middleware pipeline ordering, migration execution
  - `Extensions/ServiceExtensions.cs` (99 lines) — DI registration for all services, JWT config, CORS, FluentMigrator setup
  - `Extensions/SwaggerExtensions.cs` (86 lines) — Swagger/OpenAPI configuration with JWT security definitions
  - `Middleware/ExceptionHandlingMiddleware.cs` (105 lines) — Global exception handler mapping exceptions to ProblemDetails RFC 9110 responses

### 🟢 BasicApi.Storage — data access / infrastructure
- **Target:** .NET 10 class library
- **NuGet:** `Dapper` 2.1.72, `Npgsql` 10.0.2, `FluentMigrator` 8.0.1
- **Purpose:** Data access layer with repository pattern, PostgreSQL via Dapper, automatic migrations
- **Key Files:**
  - `Repositories/MessageRepository.cs` — Cursor-based pagination with composite key `(created_at, id)` for deterministic ordering (120 lines)
  - `Repositories/ChatRepository.cs` — Chat CRUD, member management, unread counts with SQL subqueries (160 lines)
  - `Repositories/UserRepository.cs` — User lookup by username/email, user creation (48 lines)
  - `Migrations/InitialCreate.cs` — Database schema: users, chats, chat_members, messages tables with foreign keys and indexes (98 lines)
  - `Dto/CursorDto.cs` — URL-safe Base64 encoding of composite `(DateTime, Guid)` cursor for pagination (63 lines)
  - `Dto/CursorResult.cs` — Generic wrapper fetching `limit+1` rows to detect `HasMore` without a second query (20 lines)

### 🟣 BasicApi.Tests — unit tests
- **Framework:** xUnit 2.9.3
- **Infrastructure:**
  - `Moq` 4.20.72 for mocking dependencies
  - `Microsoft.NET.Test.Sdk` 17.14.1
  - `coverlet.collector` 6.0.4 for code coverage
- **Test coverage:**
  - `ChatServiceCursorTests.cs` (174 lines) — 7 tests covering:
    - Authorization check (throws `UnauthorizedAccessException` for non-members)
    - Message mapping from entities to DTOs with sender names
    - `HasMore` flag and cursor generation when extra records exist
    - `HasMore=false` with cursor present for last page
    - Chronological (ASC) ordering of returned messages
    - Empty chat returns empty page with no cursor
    - Valid cursor passthrough to repository layer
  - `ChatsHandlerCursorTests.cs` (123 lines) — 4 tests covering:
    - Handler returns `OkObjectResult` with `CursorPaginatedResponse`
    - UnauthorizedAccessException bubbles to global middleware
    - No-more-pages scenario with `HasMore=false`
    - Empty chat returns empty page
  - `CursorDtoTests.cs` (113 lines) — 8 tests covering:
    - Encode/decode round-trip preserves values
    - URL-safe encoding (no `+`, `/`, `=` characters)
    - Invalid/empty/null string decoding error handling
    - `CompareTo` for same values, different times, and same-time-different-IDs

## Key Design Patterns Observed

| Pattern | Where | Description |
|---------|-------|-------------|
| Feature-based vertical slices | `BasicApi/Features/{Feature}/` | Each feature (Auth, Chats, Users) has its own Controller + Handler, keeping related logic together |
| Repository pattern | `BasicApi.Storage/Repositories/*` | Data access abstracted behind interfaces (`IChatRepository`, `IMessageRepository`, `IUserRepository`) |
| Handler pattern (thin controllers) | `Features/*/Handler.cs` | Controllers delegate to injected handlers, keeping controllers focused on HTTP concerns only |
| Global error handling middleware | `Middleware/ExceptionHandlingMiddleware.cs` | Centralized exception-to-ProblemDetails mapping with RFC 9110 type URIs |
| Cursor-based pagination | `MessageRepository`, `CursorDto`, `CursorPaginatedResponse` | Composite key `(created_at, id)` encoding for deterministic pagination |
| Connection factory pattern | `IDbConnectionFactory` / `NpgsqlConnectionFactory` | Abstraction over connection creation for testability and DI |
| Singleton-scoped connection factory | `ServiceExtensions.cs` | Single connection factory with scoped `IDbConnection` per request |
| Strategy-like DI via interfaces | `IJwtService`, `IChatService` | Services programmed to interfaces for testability |

## Strengths

- **Clean separation of concerns**: Three-project solution clearly separates API host, data access, and tests
- **Production-grade pagination**: Cursor-based pagination with composite key prevents missed/duplicate records even with identical timestamps; URL-safe Base64 encoding; `limit+1` fetch strategy avoids extra count queries
- **Resilient error handling**: Global middleware maps specific exception types to appropriate HTTP status codes with RFC 9110 ProblemDetails; stack traces stripped in production
- **Container-first development**: Docker Compose with PostgreSQL healthchecks, volume persistence, and environment variable configuration
- **Thin controllers**: Handlers encapsulate business logic, controllers only handle HTTP binding and routing
- **Comprehensive test suite**: 19 unit tests covering pagination edge cases, cursor encoding/decoding, authorization, and boundary conditions
- **Self-documenting API**: Swagger UI with JWT authentication support, XML documentation comments on all endpoints
- **Automated migrations**: FluentMigrator runs on startup in development, ensuring schema is always up to date

## Areas for Improvement

| Concern | Current State | Suggested Improvement |
|---------|--------------|----------------------|
| N+1 query problem in chat list | `ChatService.GetUserChatsAsync` iterates each chat to fetch last message, unread count, companion name, and sender name — potentially 1 + 4N queries | Use SQL window functions or a single query with JOINs and subqueries to batch-fetch all data |
| `IDbConnection` scoping | `IDbConnection` is registered as `Scoped` but opened eagerly in `NpgsqlConnectionFactory`; Dapper repositories receive an already-open connection via constructor injection | Keep connection factory as single responsibility; let repositories manage their own connection lifecycle or use `IAsyncRepository` patterns with `DbConnection` created per method |
| Mixed architecture styles | `ProductsController` uses in-memory static list with no persistence while Features use full DB-backed flow; `BasicApi.Models.User.cs` is a dead placeholder file | Remove dead code (`ProductsController`, `Models/User.cs`, `Models/Product.cs`) entirely or migrate Products to a proper Feature with storage |
| No explicit validation middleware | Validation relies on `[ApiController]` automatic 400 responses and `[Required]` attributes; custom validation errors in handlers return `ProblemDetails` manually | Implement a `FluentValidation` pipeline or custom `ValidationProblemDetails` middleware for consistent validation response format |
| Connection string exposed in code | `ServiceExtensions.cs` reads `ConnectionStrings:DefaultConnection` and throws `InvalidOperationException` if missing; multiple services read it | Use `IOptions<DatabaseOptions>` pattern with named configuration sections for stronger typing |
| `IsRead` always false | Both `GetChatMessagesCursorAsync` and `GetChatMessagesAsync` have `IsRead = false` with a TODO comment | Implement actual read-status resolution via the `chat_members.last_read_message_id` field already in the schema |
| No CI/CD pipeline | `.github/workflows/` directory exists but is empty | Add GitHub Actions workflow for build → test → docker build & push → deploy |
| No logging framework | No logging configuration visible; `FluentMigrator` logging is set to console | Integrate structured logging (Serilog) with request/response logging middleware |
| `DbConnection` not `IAsyncDisposable` | `NpgsqlConnectionFactory.CreateConnection()` opens connection synchronously and returns `IDbConnection` (not `DbDataSource`) | Use `NpgsqlDataSource` (which is `IDisposable` and connection-pool-aware) and the new `NpgsqlDataSource.Create()` API |
| Missing cancellation tokens | Repository methods in `IUserRepository` accept `CancellationToken` but `IChatRepository` and `IMessageRepository` do not | Add `CancellationToken` support to all async repository methods and pass it from controllers via `HttpContext.RequestAborted` |

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Application framework |
| ASP.NET Core | 10.0 | Web API host, middleware pipeline, SignalR |
| PostgreSQL | latest (Docker) | Primary database |
| Dapper | 2.1.72 | Micro-ORM for data access |
| Npgsql | 10.0.2 | PostgreSQL ADO.NET provider |
| FluentMigrator | 8.0.1 | Database migration management |
| BCrypt.Net-Next | 4.1.0 | Password hashing |
| JWT Bearer (ASP.NET) | 10.0.6 | Authentication / token validation |
| Swashbuckle / OpenAPI | 10.1.7 | API documentation / Swagger UI |
| SignalR | built-in | Real-time WebSocket messaging |
| xUnit | 2.9.3 | Unit testing framework |
| Moq | 4.20.72 | Mocking framework for tests |
| Docker | latest | Containerization and local development |
| Docker Compose | latest | Multi-container orchestration |
