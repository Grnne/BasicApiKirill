using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using BasicApi.Storage.Dto;
using Moq;

namespace BasicApi.Tests.Services;

public class ChatServiceChatDetailsTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatService _service;

    public ChatServiceChatDetailsTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _service = new ChatService(_chatRepoMock.Object, _msgRepoMock.Object);
    }

    [Fact]
    public async Task GetChatDetailsAsync_Success_ReturnsChatDetailWithParticipants()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var chat = new Chat
        {
            Id = chatId,
            Type = "group",
            Title = "Team Chat",
            CreatedAt = now
        };

        var participants = new List<BasicApi.Storage.Dto.ChatParticipantDto>
        {
            new(Guid.NewGuid(), "Alice", "alice"),
            new(Guid.NewGuid(), "Bob", "bob"),
        };

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetChatParticipantsAsync(chatId)).ReturnsAsync(participants);

        // Act
        var result = await _service.GetChatDetailsAsync(chatId, userId);

        // Assert
        Assert.Equal(chatId, result.ChatId);
        Assert.Equal("group", result.Type);
        Assert.Equal("Team Chat", result.Title);
        Assert.Equal(2, result.Participants.Count);
        Assert.Equal("Alice", result.Participants[0].DisplayName);
        Assert.Equal("Bob", result.Participants[1].DisplayName);
    }

    [Fact]
    public async Task GetChatDetailsAsync_ChatNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var chatId = Guid.NewGuid();

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((Chat?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetChatDetailsAsync(chatId, Guid.NewGuid()));
    }
}

