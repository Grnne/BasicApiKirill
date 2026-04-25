# Feature Checklist

Before writing any code, map out your feature.

## 1. Scope

**Where do changes go?**

- [ ] `BasicApi/Controllers/` — new endpoint
- [ ] `BasicApi/Features/{FeatureName}/` — Controller + Handler
- [ ] `BasicApi/Services/` — new/updated business logic
- `BasicApi/Models/Dto/{FeatureName}/` — new DTOs
- [ ] `BasicApi/Extensions/ServiceExtensions.cs` — register new DI services
- [ ] `BasicApi.Storage/Entities/` — new entity
- [ ] `BasicApi.Storage/Interfaces/` — new repository interface
- [ ] `BasicApi.Storage/Repositories/` — new repository
- [ ] `BasicApi.Storage/Migrations/` — new migration

## 2. Feature Structure (typical)

```
BasicApi/Features/{FeatureName}/
├── {FeatureName}Controller.cs    ← [ApiController], routes, XML docs
├── {FeatureName}Handler.cs       ← orchestrates services/repos, returns IActionResult

BasicApi/Services/
├── I{FeatureName}Service.cs      ← interface
├── {FeatureName}Service.cs       ← business logic

BasicApi/Models/Dto/{FeatureName}/
├── RequestDto.cs
└── ResponseDto.cs

BasicApi.Storage/Interfaces/
└── I{FeatureName}Repository.cs

BasicApi.Storage/Repositories/
└── {FeatureName}Repository.cs    ← Dapper SQL queries

BasicApi.Tests/
└── {FeatureName}Tests.cs         ← xUnit tests (Moq for mocks)
```

## 3. Files to touch

- `BasicApi/Extensions/ServiceExtensions.cs` — register new Handler, Service, Repository
