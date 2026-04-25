# GREEN — Write Minimal Code

Write just enough code to pass the test.

## Rules

1. **Minimum implementation** — only what's needed for the test
2. **Tests are READ-ONLY** — never change tests to make them pass
3. **Hardcoding is allowed** — refactor comes next
4. **All tests must pass** — run `dotnet test`

## Adding a New Feature

### 1. Entity (if new table needed)

```csharp
// BasicApi.Storage/Entities/MyEntity.cs
namespace BasicApi.Storage.Entities;

public class MyEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // ...
}
```

### 2. Repository Interface

```csharp
// BasicApi.Storage/Interfaces/IMyRepository.cs
public interface IMyRepository
{
    Task<MyEntity?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(MyEntity entity);
}
```

### 3. Repository Implementation (Dapper)

```csharp
// BasicApi.Storage/Repositories/MyRepository.cs
public class MyRepository(IDbConnection connection) : IMyRepository
{
    public async Task<MyEntity?> GetByIdAsync(Guid id)
    {
        const string sql = "SELECT id AS Id, name AS Name FROM my_entities WHERE id = @id";
        return await connection.QueryFirstOrDefaultAsync<MyEntity>(sql, new { id });
    }

    public async Task<Guid> CreateAsync(MyEntity entity)
    {
        const string sql = "INSERT INTO my_entities (id, name) VALUES (@Id, @Name) RETURNING id";
        return await connection.ExecuteScalarAsync<Guid>(sql, entity);
    }
}
```

### 4. Service (business logic)

```csharp
// BasicApi/Services/IMyService.cs
// BasicApi/Services/MyService.cs
```

### 5. Handler (orchestration)

```csharp
// BasicApi/Features/{FeatureName}/{FeatureName}Handler.cs
public class MyHandler(IMyRepository repo)
{
    public async Task<IActionResult> GetAsync(Guid id)
    {
        var entity = await repo.GetByIdAsync(id);
        if (entity == null)
            return new NotFoundResult();
        return new OkObjectResult(entity);
    }
}
```

### 6. Controller

```csharp
// BasicApi/Features/{FeatureName}/{FeatureName}Controller.cs
[ApiController]
[Route("api/[controller]")]
public class MyController(MyHandler handler) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id) => await handler.GetAsync(id);
}
```

### 7. Register in DI

In `BasicApi/Extensions/ServiceExtensions.cs`:

```csharp
services.AddScoped<IMyRepository, MyRepository>();
services.AddScoped<MyHandler>();
```

### 8. Migration (if new table)

```csharp
// BasicApi.Storage/Migrations/{Version}_{Description}.cs
[Migration(2)]
public class AddMyEntities : Migration
{
    public override void Up()
    {
        Create.Table("my_entities")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(200).NotNullable();
    }

    public override void Down() => Delete.Table("my_entities");
}
```

## Check before moving to REFACTOR

- [ ] `dotnet test` — all tests pass (old + new)?
- [ ] Implementation is minimal?
- [ ] No dead code?
- [ ] Tests unchanged since RED phase?
