# Commands Reference

## Run Tests

```powershell
# All tests
dotnet test

# Specific test class
dotnet test --filter "MyFeatureTests"

# Specific test
dotnet test --filter "CreateChat_WhenUserNotFound_ReturnsBadRequest"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
# Report: ./TestResults/{guid}/coverage.cobertura.xml
```

## Build

```powershell
# Full solution
dotnet build

# Specific project
dotnet build BasicApi/BasicApi.csproj
dotnet build BasicApi.Tests/BasicApi.Tests.csproj
```

## Add Package

```powershell
dotnet add BasicApi.Tests/BasicApi.Tests.csproj package Moq
```

## Add Project Reference

```powershell
dotnet add BasicApi.Tests/BasicApi.Tests.csproj reference BasicApi/BasicApi.csproj
```

## Solution

```powershell
# Add project to solution
dotnet sln BasicApi.sln add BasicApi.Tests/BasicApi.Tests.csproj

# List projects
dotnet sln BasicApi.sln list
```

## Docker

```powershell
docker compose up -d
docker compose down
```

## New Migration

```powershell
# Manually create a new file in BasicApi.Storage/Migrations/
# Extend the Migration class with [Migration(next_number)]
```
