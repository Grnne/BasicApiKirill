# Test Patterns for BasicApi

## Pattern 1: Handler returns IActionResult

```csharp
[Fact]
public async Task GetChat_WhenChatExists_ReturnsOkWithChat()
{
    // Arrange
    var chatId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var chatDetail = new ChatDetailDto
    {
        ChatId = chatId,
        Type = "private",
        Participants = []
    };

    _chatServiceMock
        .Setup(s => s.GetChatDetailsAsync(chatId, userId))
        .ReturnsAsync(chatDetail);

    // Act
    var result = await _handler.GetChatAsync(chatId, userId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var dto = Assert.IsType<ChatDetailDto>(okResult.Value);
    Assert.Equal(chatId, dto.ChatId);
}
```

## Pattern 2: Handler returns error

```csharp
[Fact]
public async Task GetChat_WhenUserNotMember_ReturnsForbid()
{
    // Arrange
    var chatId = Guid.NewGuid();
    _repoMock
        .Setup(r => r.IsMemberAsync(chatId, It.IsAny<Guid>()))
        .ReturnsAsync(false);

    // Act
    var result = await _handler.MarkReadAsync(chatId, Guid.NewGuid(), Guid.NewGuid());

    // Assert
    Assert.IsType<ForbidResult>(result);
}
```

## Pattern 3: Service with business logic

```csharp
[Fact]
public async Task GetUserChatsAsync_WhenHasUnread_ReturnsCount()
{
    // Arrange
    var userId = Guid.NewGuid();
    var chats = new[] { new Chat { Id = Guid.NewGuid(), Type = "private" } };

    _chatRepoMock.Setup(r => r.GetUserChatsAsync(userId)).ReturnsAsync(chats);
    _msgRepoMock.Setup(r => r.GetUnreadCountAsync(It.IsAny<Guid>(), userId)).ReturnsAsync(5);

    var service = new ChatService(_chatRepoMock.Object, _msgRepoMock.Object);

    // Act
    var result = await service.GetUserChatsAsync(userId);

    // Assert
    var item = Assert.Single(result);
    Assert.Equal(5, item.UnreadCount);
}
```

## Pattern 4: Validation

```csharp
[Fact]
public void CreateProduct_WhenPriceZero_ReturnsBadRequest()
{
    // Arrange
    var controller = new ProductsController();
    var request = new CreateProductRequest { Name = "Test", Price = 0 };

    // Act
    var result = controller.Create(request);

    // Assert
    var badRequest = Assert.IsType<BadRequestObjectResult>(result);
    // or use FluentValidation / DataAnnotations in real scenarios
}
```

## Pattern 5: Repository (integration-style with mocked connection)

```csharp
[Fact]
public async Task UserRepository_GetByUsername_ReturnsUser()
{
    // This tests SQL logic — use a real DB or mock IDbConnection
    // For unit tests, mock IDbConnection + Dapper extensions
    // For integration, use Testcontainers (PostgreSQL)
}
```

## Pattern 6: Exception from Service

```csharp
[Fact]
public async Task GetChatMessages_WhenNotMember_ThrowsUnauthorized()
{
    // Arrange
    _repoMock
        .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
        .ReturnsAsync(false);

    var service = new ChatService(_repoMock.Object, _msgRepoMock.Object);

    // Act & Assert
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        service.GetChatMessagesAsync(Guid.NewGuid(), Guid.NewGuid(), null, 50));
}
```

## Naming Examples

| ✅ Good | ❌ Bad |
|---------|-------|
| `CreateChat_WhenUserNotFound_ReturnsBadRequest` | `TestChat1` |
| `SendMessage_WhenNotMember_ThrowsUnauthorizedAccess` | `SendTest` |
| `GetChats_WhenNoChats_ReturnsEmptyList` | `CheckGetChats` |
| `MarkRead_WhenNotMember_ReturnsForbid` | `HandlerTest` |
