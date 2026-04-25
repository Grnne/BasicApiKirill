# Project Analysis: BasicApi

> Real-time chat Web API with JWT authentication, SignalR, and PostgreSQL persistence

## Overview

BasicApi is a real-time chat application built on ASP.NET Core 10, using SignalR for WebSocket-based real-time messaging and PostgreSQL for persistent storage via Dapper (micro-ORM). The solution follows a two-project architecture: a Web API project (`BasicApi`) containing controllers, SignalR hubs, services, and DTOs; and a class library (`BasicApi.Storage`) handling all data access — entities, repositories, Dapper SQL queries, FluentMigrator migrations, and a connection factory.

The application implements JWT bearer token authentication for both REST endpoints and SignalR hub connections (tokens passed via query string for WebSocket). It supports user registration/login with BCrypt password hashing, private chat creation with participant tracking, message history retrieval with cursor-based pagination, and real-time messaging with typing indicators and online presence. The entire stack is Dockerized with docker-compose, running the API and a PostgreSQL  database in linked containers.

## Project Structure

```
BasicApi/
├── BasicApi.sln
├── docker-compose.yml
├── BasicApi/                          # Web API entry point
│   ├── BasicApi.csproj
│   ├── Program.cs
│   ├── Dockerfile
│   ├── Controllers/
│   │   └── ProductsController.cs
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
│   ├── Models/
│   │   ├── Product.cs
│   │   ├── User.cs
│   │   └── Dto/
│   │       ├── Auth/       (4 files)
│   │       ├── Chat/       (4 files)
│   │       └── Message/    (3 files)
│   ├── Services/
│   │   ├── IChatService.cs
│   │   ├── ChatService.cs
│   │   ├── IJwtService.cs
│   │   └── JwtService.cs
│   └── Extensions/
│       ├── ServiceExtensions.cs
│       ├── SwaggerExtensions.cs
│       └── ClaimsPrincipalExtensions.cs
├── BasicApi.Storage/                  # Data access library
│   ├── BasicApi.Storage.csproj
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
│   ├── Migrations/
│   │   └── InitialCreate.cs
│   └── Dto/
│       └── ChatParticipantDto.cs
└── test-client.html
```

## Architecture Breakdown

### 🔵 BasicApi — Web API entry point
- **Target:** .NET 10, ASP.NET Core (Microsoft.NET.Sdk.Web)
- **NuGet:**
  - `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.6 — JWT auth
  - `BCrypt.Net-Next` 4.1.0 — password hashing
  - `System.IdentityModel.Tokens.Jwt` 8.17.0 — JWT token handling
  - `Swashbuckle.AspNetCore` 10.1.7 — Swagger/OpenAPI
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1 — migrations
  - `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` 1.22.1 — Docker tooling
- **Key Flow:**
  ```
  Program.Main → AddApiServices() → Controllers/SignalR → Auth (JWT) → Repositories → PostgreSQL
   ├─ Swagger/OpenAPI docs
   ├─ SignalR ChatHub (real-time messaging)
   ├─ REST: Auth, Chats, Users, Products endpoints
   └─ FluentMigrator runs at startup
  ```
- **Key Files:**
  - `Program.cs` (~30 lines) — app bootstrap, middleware pipeline, migration runner at startup
  - `Extensions/ServiceExtensions.cs` (~85 lines) — DI container setup (all services, repos, JWT, CORS, FluentMigrator)
  - `Extensions/SwaggerExtensions.cs` (~65 lines) — Swagger config with JWT security definition using new Swashbuckle 10 / OpenApi v2 syntax
  - `Features/Auth/AuthHandler.cs` (~70 lines) — login/register logic
  - `Features/Chats/ChatsHandler.cs` (~80 lines) — chat CRUD orchestration
  - `Hubs/ChatHub.cs` (~100 lines) — SignalR hub (connect, disconnect, join/leave groups, send message, typing)
  - `Services/JwtService.cs` (~75 lines) — JWT token generation and validation
  - `Services/ChatService.cs` (~85 lines) — chat business logic (user chats, details, messages)

### 🟢 BasicApi.Storage — Data access / Infrastructure library
- **Target:** .NET 10 (Microsoft.NET.Sdk)
- **NuGet:**
  - `Dapper` 2.1.72 — micro-ORM for ADO.NET
  - `Npgsql` 10.0.2 — PostgreSQL provider
  - `FluentMigrator` / `FluentMigrator.Runner` / `FluentMigrator.Runner.Postgres` 8.0.1
- **Purpose:** Data persistence layer — entities, repositories, migrations, connection management
- **Key Files:**
  - `Entities/User.cs`, `Entities/Chat.cs`, `Entities/ChatMember.cs`, `Entities/Message.cs` — domain/entity models
  - `Interfaces/IUserRepository.cs`, `IChatRepository.cs`, `IMessageRepository.cs` — repository contracts
  - `Repositories/UserRepository.cs` (~50 lines) — user queries (by username/email, create, get ID)
  - `Repositories/ChatRepository.cs` (~120 lines) — chat queries (user chats, create with members, participants, companion name)
  - `Repositories/MessageRepository.cs` (~60 lines) — message queries (get with cursor pagination, create, mark read, unread count)
  - `Services/NpgsqlConnectionFactory.cs` (~15 lines) — creates and opens Npgsql connections
  - `Migrations/InitialCreate.cs` (~100 lines) — FluentMigrator migration that creates all tables, indexes, foreign keys

### 🟣 (No test project exists)
- **Framework:** N/A — there are no tests in this solution
- **Test coverage:** None

## Key Design Patterns Observed

| Pattern | Where | Description |
|---------|-------|-------------|
| Repository | `BasicApi.Storage/Repositories/*` | All data access encapsulated behind repository interfaces (`IUserRepository`, `IChatRepository`, `IMessageRepository`) |
| Handler / Mediator (lightweight) | `BasicApi/Features/*/Handlers.cs` | Controllers delegate to handler classes that orchestrate services/repos and return `IActionResult` — a simple mediator-like separation |
| Strategy (DI-based) | `BasicApi/Extensions/ServiceExtensions.cs` | All dependencies registered via DI (`AddScoped`, `AddSingleton`); `IDbConnectionFactory` allows swapping connection implementations |
| Singleton | `NpgsqlConnectionFactory` | Registered as singleton; creates a new `IDbConnection` per resolution (scoped `IDbConnection`) |
| Unit of Work (implicit) | `ChatRepository.CreateAsync()` | Uses explicit `BeginTransaction()` / `Commit()` / `Rollback()` for chat + member inserts |
| Fluent Builder (migrations) | `BasicApi.Storage/Migrations/InitialCreate.cs` | FluentMigrator's fluent API for table/foreign key/index definitions |

## Strengths

- **Clean separation of concerns** — Web API project is purely about HTTP/SignalR concerns; data access is fully isolated in a separate class library project with no framework dependencies
- **Real-time messaging with SignalR** — properly handles JWT token passing via query string for WebSocket connections, with group-based chat rooms and online presence tracking
- **Docker-first deployment** — full docker-compose setup with PostgreSQL health check, environment variable configuration, and proper dependency ordering
- **Swagger documentation** — comprehensive OpenAPI docs with JWT authentication support, XML comments, and request/response examples
- **Database migrations** — FluentMigrator with auto-run at startup ensures schema is always up to date
- **Password security** — BCrypt hashing for passwords, not plain text
- **Cursor-based pagination** — messages can be fetched "before" a timestamp, preventing common issues with offset-based pagination

## Areas for Improvement

| Concern | Current State | Suggested Improvement |
|---------|--------------|----------------------|
| **No tests** | Zero test projects exist | Add xUnit/NUnit test project(s) covering AuthHandler, ChatService, repositories with in-memory or testcontainers for PostgreSQL |
| **ProductsController is a demo stub** | Uses in-memory `static List<Product>` with no persistence | Either remove it or implement real storage through the Storage project |
| **Duplicate DTO models** | `ChatParticipantDto` exists both in `BasicApi.Models.Dto.Chat` and `BasicApi.Storage.Dto` with the same shape | Consolidate into a single shared DTO; having two identical models creates maintenance overhead |
| **Scoped IDbConnection** | `IDbConnection` is registered as scoped but `NpgsqlConnectionFactory.CreateConnection()` opens a new connection each time; repos receive the same scoped connection but can't participate in ambient transactions across repositories | Consider using a connection-owning `UnitOfWork` pattern or `DbConnection` that is opened once per scope and shared |
| **FluentMigrator runs on every startup** | `MigrateUp()` runs unconditionally in `Program.cs` | Add an environment check so it only auto-migrates in Development; use a dedicated migration tool/script for production |
| **SQL injection risk in MessageRepository** | Uses `sql +=` string concatenation for the optional `@before` parameter filter | The parameterized query is safe (Dapper handles the param), but the concatenation approach is fragile; use a single parameterized SQL string with coalescing logic instead |
| **Error handling** | No global exception middleware; `ChatService` throws raw `Exception("Chat not found")` and `UnauthorizedAccessException` | Add a global error handling middleware (or `UseExceptionHandler`) that maps exceptions to proper HTTP status codes consistently |
| **No logging/observability** | No structured logging (Serilog/NLog), no metrics, no tracing | Add structured logging and consider OpenTelemetry for distributed tracing |
| **No CI/CD pipeline** | `.github/workflows` directory exists but is empty | Add a GitHub Actions workflow for build, test, and deploy |
| **Unread count SQL bug** | `GetUnreadCountAsync` in `ChatRepository` has a malformed SQL query — it uses `FROM chat_members` within a subquery after a `FROM messages` | Fix the SQL syntax; the query currently won't compile/run correctly |
| **No HTTPS in Docker** | Docker container only listens on HTTP `:8080` | For production, add HTTPS support or terminate TLS at a reverse proxy (nginx, Traefik) |

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime framework |
| ASP.NET Core | 10.0 | Web API framework with minimal APIs and controllers |
| C# | 13 (default for .NET 10) | Programming language |
| SignalR | Built-in | Real-time WebSocket communication |
| PostgreSQL | latest (Docker image) | Relational database |
| Dapper | 2.1.72 | Micro-ORM for data access |
| Npgsql | 10.0.2 | PostgreSQL ADO.NET provider |
| FluentMigrator | 8.0.1 | Database migration management |
| BCrypt.Net-Next | 4.1.0 | Password hashing |
| JWT Bearer Auth | 10.0.6 | Token-based authentication |
| Swashbuckle.AspNetCore | 10.1.7 | Swagger/OpenAPI documentation |
| Docker / docker-compose | — | Containerization and orchestration |
