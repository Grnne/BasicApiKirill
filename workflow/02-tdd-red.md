# RED — Write a Failing Test

Write test **before** implementation code.

## Rules

1. Test defines expected behavior — don't write tests for existing code
2. Test must fail with a meaningful message
3. One test = one assertion (or `Assert.Multiple` for related checks)
4. Tests **DO NOT** touch:
   - Real database (mock repositories with Moq)
   - Real file system
   - External services

## Test Template (Handler)

```csharp
namespace BasicApi.Tests;

public class MyFeatureTests
{
    private readonly Mock<IMyRepository> _repoMock;
    private readonly MyHandler _handler;

    public MyFeatureTests()
    {
        _repoMock = new Mock<IMyRepository>();
        _handler = new MyHandler(_repoMock.Object);
    }

    [Fact]
    public async Task MyFeature_WhenCondition_ExpectsResult()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new MyEntity { /* ... */ });

        // Act
        var result = await _handler.DoSomethingAsync(Guid.NewGuid());

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}
```

## Test Template (Service)

```csharp
[Fact]
public async Task GetData_WhenEntityExists_ReturnsDto()
{
    // Arrange
    _repoMock
        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
        .ReturnsAsync(new MyEntity { Id = Guid.NewGuid(), Name = "Test" });

    var service = new MyService(_repoMock.Object);

    // Act
    var dto = await service.GetDataAsync(Guid.NewGuid());

    // Assert
    Assert.NotNull(dto);
    Assert.Equal("Test", dto.Name);
}
```

## Naming Convention

```
{Feature}_{WhenCondition}_{ExpectsResult}
```

✅ `CreateChat_WhenUserNotFound_ReturnsBadRequest`
✅ `SendMessage_WhenNotMember_ThrowsUnauthorizedAccess`
❌ `Test1`, `CreateTest`

## Check before moving to GREEN

- [ ] Test fails with expected error?
- [ ] Error message is meaningful?
- [ ] No external dependencies (DB, network)?
- [ ] File is `BasicApi.Tests/{FeatureName}Tests.cs` (not `UnitTest1.cs`)
