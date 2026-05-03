# Project Analysis: BasicApi
> Real-time Chat API with JWT authentication, SignalR, and PostgreSQL with cursor-based pagination

## Overview

BasicApi is a production-oriented real-time chat API built on ASP.NET Core 10. It provides a complete backend for a messaging application — user authentication (JWT), private chat management, real-time messaging via SignalR WebSockets, and cursor-based pagination for message history. The project follows a clean layered architecture with clear separation between API (controllers/handlers), business logic (services), and data access (repositories/Dapper + PostgreSQL).

The solution comprises three projects: the main web application (`BasicApi`), a storage/infrastructure library (`BasicApi.Storage`), and a test project (`BasicApi.Tests`). The stack relies on **Dapper** for lightweight data access, **Npgsql** for PostgreSQL connectivity, **FluentMigrator** for database migrations, and **BCrypt** for password hashing. The API uses a structured exception hierarchy mapped to RFC 7807 Problem Details and features a validation error code system for programmatic client handling.

## Project Structure

```
BasicApi/
├── BasicApi.sln
├── readme.md
├── docker-compose.yml
├── .env (ignored — contains secrets)
├── docker-compose.dcproj
├── CursorPaginationChanges.md
├── launchSettings.json
├── BasicApi/                          # Main web application
│   ├── BasicApi.csproj               # net10.0, ASP.NET Core
│   ├── Program.cs                    # Entry point & middleware pipeline
│   ├── Dockerfile
│   ├── appsettings.json              # (secrets — not read)
│   ├── Extensions/
│   │   ├── ServiceExtensions.cs      # DI registration, JWT, CORS, validation
│   │   ├── SwaggerExtensions.cs      # Swagger/OpenAPI config
│   │   └── ClaimsPrincipalExtensions.cs
│   ├── Middleware/
│   │   ├── ExceptionHandlingMiddleware.cs
│   │   └── Exceptions/
│   │       ├── DomainException.cs
│   │       ├── BadRequestException.cs
│   │       ├── NotFoundException.cs
│   │       ├── ConflictException.cs
│   │       ├── ForbiddenException.cs
│   │       └── UnauthorizedException.cs
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
│   ├── Services/
│   │   ├── IChatService.cs
│   │   ├── ChatService.cs
│   │   ├── IJwtService.cs
│   │   └── JwtService.cs
│   ├── Models/Dto/
│   │   ├── Auth/ (LoginRequestDto, RegisterRequestDto, AuthResponseDto)
│   │   ├── Chat/ (ChatDetailDto, ChatListItemDto, ChatParticipantDto, ...)
│   │   ├── Message/ (MessageDto, CursorPaginatedResponse, SearchMessagesResponseDto, SendMessageDto, ...)
│   │   └── Users/ (UserIdResponseDto)
│   └── wwwroot/
│       └── test-client.html
├── BasicApi.Storage/                 # Data access & entities
│   ├── BasicApi.Storage.csproj       # net10.0, class library
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Chat.cs
│   │   ├── ChatMember.cs
│   │   └── Message.cs
│   ├── Interfaces/
│   │   ├── IDbConnectionFactory.cs
│   │   ├── IUserRepository.cs
│   │   ├── IChatRepository.cs
│   │   └── IMessageRepository.cs
│   ├── Repositories/
│   │   ├── UserRepository.cs
│   │   ├── ChatRepository.cs
│   │   └── MessageRepository.cs
│   ├── Services/
│   │   └── NpgsqlConnectionFactory.cs
│   ├── Dto/
│   │   ├── CursorDto.cs
│   │   ├── CursorResult.cs
│   │   ├── ChatListResult.cs
│   │   ├── ChatParticipantDto.cs
│   │   └── MessageWithSender.cs
│   └── Migrations/
│       ├── InitialCreate.cs
│       └── AddFullTextSearch.cs
└── BasicApi.Tests/                   # Unit tests
    ├── BasicApi.Tests.csproj         # xUnit + Moq
    ├── CursorDtoTests.cs
    ├── Features/
    │   ├── AuthHandlerTests.cs
    │   ├── ChatsHandlerTests.cs
    │   ├── ChatsHandlerCursorTests.cs
    │   └── UsersHandlerTests.cs
    ├── Middleware/
    │   └── ExceptionHandlingMiddlewareTests.cs
    └── Services/
        ├── ChatServiceChatDetailsTests.cs
        ├── ChatServiceCursorTests.cs
        ├── ChatServiceUserChatsTests.cs
        └── JwtServiceTests.cs
```

## Architecture Breakdown

### 🔵 BasicApi — Main Web Application (Entry Point)
- **Target:** .NET 10.0 (ASP.NET Core), Linux Docker target
- **NuGet (key):**
  - `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.6
  - `BCrypt.Net-Next` 4.1.0
  - `System.IdentityModel.Tokens.Jwt` 8.17.0
  - `Swashbuckle.AspNetCore` 10.1.7
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1
- **Key Flow:**
```
Program.Main → AddApiServices() → AddControllers / Swagger / JWT / SignalR / Dapper repos / FluentMigrator
  → app.Build() → ExceptionHandlingMiddleware → FluentMigrateUp() → StaticFiles → CORS → SwaggerUI
  → HttpsRedirection → Authentication → Authorization → MapHub<ChatHub>("/hubs/chat") → MapControllers
```
- **Key Files:**
  - `Program.cs` (~55 lines) — entry point; middleware pipeline order is well-commented
  - `Extensions/ServiceExtensions.cs` (~165 lines) — all DI registration, includes custom `InvalidModelStateResponseFactory` with validation error codes, JWT events for SignalR token extraction, FluentMigrator setup
  - `Extensions/SwaggerExtensions.cs` (~70 lines) — configures Swagger with JWT security definition, XML docs, contact info from config
  - `Middleware/ExceptionHandlingMiddleware.cs` (~110 lines) — global error handling; maps 6 exception types to ProblemDetails with error codes; strips internals in production; sets `WWW-Authenticate` header for 401
  - `Features/Chats/ChatsController.cs` (~115 lines) — 7 endpoints including cursor-based pagination, "jump to date", and full-text search
  - `Hubs/ChatHub.cs` (~120 lines) — SignalR hub with online presence tracking (ConcurrentDictionary), Join/Leave/SendMessage/Typing
  - `Services/JwtService.cs` (~70 lines) — token generation/validation with configurable expiry

### 🟢 BasicApi.Storage — Database Access Library
- **Target:** .NET 10.0 (class library)
- **NuGet (key):**
  - `Dapper` 2.1.72
  - `Npgsql` 10.0.2
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1
- **Purpose:** Data access layer with Dapper micro-ORM, PostgreSQL via Npgsql, cursor-based pagination infrastructure
- **Key Files:**
  - `Repositories/ChatRepository.cs` (~230 lines) — 10 SQL queries including a sophisticated batched chat list query using LATERAL joins
  - `Repositories/MessageRepository.cs` (~300 lines) — cursor-based pagination with composite key `(created_at, id)`, `GetFirstMessageBeforeDateAsync` for "jump to date", `SearchMessagesCursorAsync` for full-text search using `to_tsvector`/`plainto_tsquery` with GIN index + `COUNT(*)` for total results
  - `Repositories/UserRepository.cs` (~65 lines) — basic CRUD with `CancellationToken` support
  - `Dto/CursorDto.cs` (~70 lines) — `readonly struct` encoding `(DateTime, Guid)` as URL-safe Base64 (32 bytes)
  - `Dto/CursorResult.cs` (~18 lines) — generic wrapper with `HasMore` detection via extra record
  - `Migrations/InitialCreate.cs` (~110 lines) — FluentMigrator migration creating 4 tables with indexes and foreign keys
  - `Services/NpgsqlConnectionFactory.cs` (~12 lines) — simple factory pattern for creating connections

### 🟣 BasicApi.Tests — Unit Tests
- **Framework:** xUnit 2.9.3, Moq 4.20.72, coverlet.collector 6.0.4
- **Infrastructure:**
  - `CursorDtoTests.cs` — roundtrip encode/decode test
  - `Middleware/ExceptionHandlingMiddlewareTests.cs` — 11 tests covering all exception types, environment-aware detail hiding, trace ID, WWW-Authenticate header
  - `Features/AuthHandlerTests.cs` — 6 tests: login success, invalid password, user not found, register success, duplicate username/email, empty display name fallback
  - `Features/ChatsHandlerTests.cs` — 6 tests: existing chat returns 200, self-chat blocked, get chat, mark read success, mark read not-a-member, jump-to-date cursor
  - `Features/ChatsHandlerCursorTests.cs` — 7 tests: paginated response, not-a-member authorization, search success, short query validation, search not-a-member, empty search results
  - `Features/UsersHandlerTests.cs` — 3 tests: found returns ID, not found, empty GUID
  - `Services/ChatServiceChatDetailsTests.cs` — 2 tests: success with participants, chat not found
  - `Services/ChatServiceCursorTests.cs` — 10 tests: authorization, mapped messages, has-more detection, single page, cursor passthrough, search authorization, short query validation, mapped search results, empty search results, search cursor passthrough
  - `Services/ChatServiceUserChatsTests.cs` — 2 tests: mapped chats with last message, no-last-message fallback
  - `Services/JwtServiceTests.cs` — 3 tests: token non-empty, validate valid token, expiry date range
- **Test coverage:** 56 tests total. Covers all handler methods, all middleware exception types, all chat service methods, auth flows, cursor pagination edge cases (HasMore, no-more-pages, cursor passthrough), user lookups, and full-text search (query validation, authorization, mapped results, cursor passthrough). No integration tests.

## Key Design Patterns Observed

| Pattern | Where | Description |
|---------|-------|-------------|
| **Handler Pattern** | `Features/Auth/AuthHandler.cs`, `Features/Chats/ChatsHandler.cs`, `Features/Users/UsersHandler.cs` | Business logic extracted from controllers into handler classes; controllers become thin delegates |
| **Domain Exception Hierarchy** | `Middleware/Exceptions/DomainException.cs` + 5 subclasses | Abstract base exception with `ErrorCode` property; each maps to a specific HTTP status via middleware |
| **Global Error Handling Middleware** | `Middleware/ExceptionHandlingMiddleware.cs` | Single middleware catches all exceptions, maps to structured ProblemDetails responses; dev/prod-aware |
| **Cursor-Based Pagination** | `Storage/Dto/CursorDto.cs`, `Storage/Dto/CursorResult.cs`, `Models/Dto/Message/CursorPaginatedResponse.cs` | Composite cursor `(created_at, id)` encoded as URL-safe Base64; fetch `limit+1` pattern for HasMore detection |
| **Repository Pattern** | `Storage/Repositories/*.cs` | Interfaces and implementations for data access using Dapper; repositories handle SQL and connection management |
| **Connection Factory** | `Storage/Services/NpgsqlConnectionFactory.cs`, `Storage/Interfaces/IDbConnectionFactory.cs` | Abstracted connection creation for testability |
| **Service Layer** | `Services/ChatService.cs` | Business logic layer between handlers and repositories; handles authorization checks and DTO mapping |
| **JWT Service** | `Services/JwtService.cs`, `Services/IJwtService.cs` | Token generation/validation encapsulated in a service with configurable expiry |
| **Feature Folders** | `Features/Auth/`, `Features/Chats/`, `Features/Users/` | Grouping by feature (controller + handler) rather than by layer type |
| **Readonly Struct Cursor** | `Storage/Dto/CursorDto.cs` | Immutable value type for cursor encoding/decoding with `IComparable<CursorDto>` |
| **Batched Query Pattern** | `ChatRepository.GetUserChatsBatchedAsync` | Single SQL with LATERAL JOINs replaces N+1 pattern for chat list with last message and unread count |
| **Migration Runner at Startup** | `Program.cs` | `MigrateUp()` runs automatically on app start (development convenience) |
| **Validation Error Code Mapping** | `ServiceExtensions.GetValidationErrorCode()` | Maps ASP.NET default validation messages to machine-readable codes (REQUIRED, MAX_LENGTH, etc.) |
| **Full-Text Search** | `MessageRepository.SearchMessagesCursorAsync`, `Migrations/AddFullTextSearch.cs` | PostgreSQL `tsvector`/`tsquery` full-text search with GIN inverted index; search within specific chat with cursor-based pagination; `COUNT(*)` only on first page for performance; validation (min 2 chars) in service layer |

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/auth/login` | Authenticate user, returns JWT |
| `POST` | `/api/auth/register` | Register new user, returns JWT |
| `GET` | `/api/users/GetUserId/{usernameOrEmail}` | Lookup user ID by username or email |
| `GET` | `/api/chats` | List user's chats with last message and unread count |
| `POST` | `/api/chats/private/{userId}` | Create or get existing private chat |
| `GET` | `/api/chats/{chatId}` | Get chat details with participants |
| `GET` | `/api/chats/{chatId}/messages/cursor` | Cursor-based paginated messages |
| `GET` | `/api/chats/{chatId}/messages/at` | Jump to messages around a date |
| `GET` | `/api/chats/{chatId}/messages/search` | **Full-text search** within chat messages |
| `POST` | `/api/chats/{chatId}/read` | Mark messages as read |
| `ws` | `/hubs/chat` | SignalR hub for real-time messaging |

## Strengths

- **Clean layered architecture** with clear separation of concerns: Controller → Handler → Service → Repository → Database
- **Structured error handling** with domain exception hierarchy, global middleware, and standardized ProblemDetails responses — no scattered try/catch
- **Production-grade cursor-based pagination** with composite key `(created_at, id)` ensuring deterministic ordering even under high concurrency; URL-safe Base64 encoding
- **Batched SQL queries** that avoid N+1 problems (chat list with last message, messages with sender name via JOIN)
- **Comprehensive test suite** (56 tests) covering happy paths, error paths, and edge cases for all layers including full-text search (query validation, authorization, mapped results, cursor passthrough)
- **SignalR integration** with real-time messaging, online presence tracking (ConcurrentDictionary), and typing indicators
- **FluentMigrator** for managed, versioned database schema migrations
- **Validation error code system** providing machine-readable error codes alongside human-readable messages
- **Docker Compose** setup with PostgreSQL health check and network isolation; configurable ports via environment variables
- **JWT token extraction from query string** for SignalR WebSocket connections
- **Swagger documentation** with JWT auth configuration, XML doc comments, contact info
- **Clear commit/docs** (`CursorPaginationChanges.md`) documenting the refactoring decisions

## Areas for Improvement

| Concern | Current State | Suggested Improvement |
|---------|--------------|----------------------|
| **No CI/CD pipeline** | `.github/workflows/` is empty; no pipeline configuration found | Add GitHub Actions workflow for `dotnet build`, `dotnet test`, Docker build & push, and deployment |
| **No integration tests** | All tests are unit tests with Moq; the database layer is untested | Add integration tests using Testcontainers for PostgreSQL to test actual SQL queries, migrations, and cursor pagination |
| **Missing Serilog/structured logging** | No logging framework configured; relies on default `AddConsole()` for FluentMigrator only | Add Serilog (or Microsoft.Extensions.Telemetry) with structured logging, correlation IDs, and request logging middleware |
| **No OpenTelemetry / observability** | No metrics, tracing, or health checks configured | Add OpenTelemetry for distributed tracing, Prometheus metrics, and health check endpoints (`/health`, `/ready`) |
| **ConcurrentDictionary for SignalR presence** | `ChatHub._onlineUsers` is static — does not scale horizontally across multiple instances | Replace with Redis backplane for SignalR scaleout; use Redis cache or SignalR Redis backplane for presence tracking |
| **No CancellationToken forwarding** | Repositories accept `CancellationToken` in interface but `ChatService` and `ChatsHandler` don't pass it — they use default | Propagate `CancellationToken` from HttpContext through services to repositories |
| **`IMessageRepository.CreateAsync` returns Guid but `ChatHub` ignores return** | Hub calls `await messageRepository.CreateAsync(message)` but doesn't use returned ID (already set client-side) | Either use server-generated ID or keep as-is (client generates GUID which is fine) |
| **No API versioning** | All endpoints under `/api/[controller]` with no versioning strategy | Add `Asp.Versioning.Mvc` for URL/header versioning (e.g., `/api/v1/chats`) |
| **No rate limiting** | No protection against brute-force login attempts or excessive API calls | Add `Microsoft.AspNetCore.RateLimiting` middleware for auth endpoints and general API |
| **No model validation on `SendMessageDto`** | `SendMessageDto` has no `[Required]` attributes | Add `[Required(ErrorMessage = "Message text is required")]` validation |
| **`IsRead` always false** | `MessageDto.IsRead` is hardcoded as `false` with `// TODO: resolve actual read status` | Join with `chat_members.last_read_message_id` to compute real read status per user |
| **`TotalCount` only on first page** | `SearchMessagesCursorAsync` executes `COUNT(*)` only when `cursor == null`; subsequent pages return `0` | For accurate total across pages, cache count client-side or add dedicated `/search/count` endpoint |
| **Raw SQL in repositories** | All queries are inline strings with Dapper | Consider using a SQL files approach or a lightweight query builder for maintainability; or at minimum use `Dapper.SqlBuilder` for dynamic queries |

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET / ASP.NET Core | 10.0 | Application framework |
| C# | 13 (implied) | Programming language |
| PostgreSQL | latest (Docker) | Primary database |
| Dapper | 2.1.72 | Micro-ORM for data access |
| Npgsql | 10.0.2 | PostgreSQL ADO.NET provider |
| FluentMigrator | 8.0.1 | Database migrations |
| BCrypt.Net-Next | 4.1.0 | Password hashing |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.6 | JWT authentication |
| System.IdentityModel.Tokens.Jwt | 8.17.0 | JWT token handling |
| SignalR | built-in (ASP.NET Core) | Real-time WebSocket communication |
| Swashbuckle.AspNetCore | 10.1.7 | Swagger/OpenAPI documentation |
| MS Visual Studio Azure Containers Tools | 1.22.1 | Docker container tooling |
| xUnit | 2.9.3 | Unit testing framework |
| Moq | 4.20.72 | Mocking framework |
| coverlet | 6.0.4 | Code coverage collection |
| Docker / Docker Compose | — | Containerization & local development |
| Microsoft.NET.Test.Sdk | 17.14.1 | Test SDK |
