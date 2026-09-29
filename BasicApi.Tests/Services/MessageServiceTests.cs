using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>Отправка, прочитанность и «переход к дате».</summary>
public class MessageServiceTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly MessageService _service;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherId = Guid.NewGuid();

    public MessageServiceTests()
    {
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_userId, _otherId]);
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Репозиторий возвращает то, что вставили, с именем отправителя
        _msgRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => new MessageWithSender
            {
                Id = m.Id, ChatId = m.ChatId, SenderId = m.SenderId, Text = m.Text,
                CreatedAt = m.CreatedAt, SenderName = "Alice"
            });

        _service = new MessageService(_msgRepoMock.Object, new MembershipService(_chatRepoMock.Object), _eventsMock.Object);
    }

    // ========== Send ==========

    [Fact]
    public async Task Send_ByMember_StoresMessage_AndAnnouncesItToAllMembers()
    {
        var message = await _service.SendAsync(_chatId, _userId, "hello");

        Assert.Equal("hello", message.Text);
        Assert.Equal("Alice", message.SenderName);
        Assert.Equal(_chatId, message.ChatId);
        Assert.Equal(DateTimeKind.Utc, message.CreatedAt.Kind);
        _msgRepoMock.Verify(r => r.CreateAsync(
            It.Is<Message>(m => m.ChatId == _chatId && m.SenderId == _userId && m.Text == "hello"),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.MessageCreatedAsync(message,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_userId) && ids.Contains(_otherId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_ByNonMember_IsRejected_NothingStoredOrSent()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.SendAsync(Guid.NewGuid(), _userId, "hello"));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _msgRepoMock.Verify(r => r.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task Send_EmptyText_IsRejectedWithCode(string? text)
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => _service.SendAsync(_chatId, _userId, text));

        Assert.Equal(MessageText.EmptyCode, ex.ErrorCode);
        _msgRepoMock.Verify(r => r.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_TooLong_IsRejectedWithCode()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SendAsync(_chatId, _userId, new string('x', MessageText.MaxLength + 1)));

        Assert.Equal(MessageText.TooLongCode, ex.ErrorCode);
    }

    [Fact]
    public async Task Send_IsTrimmed_AndMaxLengthIsAccepted()
    {
        var text = new string('x', MessageText.MaxLength);

        var message = await _service.SendAsync(_chatId, _userId, "  " + text + "  ");

        Assert.Equal(text, message.Text);
    }

    // ========== MarkRead ==========

    [Fact]
    public async Task MarkRead_MovesPointer()
    {
        var messageId = Guid.NewGuid();
        _msgRepoMock
            .Setup(r => r.MarkReadAsync(_chatId, _userId, messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadPointerUpdate.Moved);

        await _service.MarkReadAsync(_chatId, _userId, messageId);

        _msgRepoMock.Verify(r => r.MarkReadAsync(_chatId, _userId, messageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_OlderMessage_IsNotAnError()
    {
        // Указатель назад не двигается, но для клиента это не ошибка: он уже прочитал дальше.
        _msgRepoMock
            .Setup(r => r.MarkReadAsync(_chatId, _userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadPointerUpdate.NotMoved);

        await _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid());
    }

    [Fact]
    public async Task MarkRead_MessageNotInChat_Is404()
    {
        _msgRepoMock
            .Setup(r => r.MarkReadAsync(_chatId, _userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadPointerUpdate.MessageNotFound);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid()));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task MarkRead_NotMember_Is403()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.MarkReadAsync(Guid.NewGuid(), _userId, Guid.NewGuid()));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _msgRepoMock.Verify(r => r.MarkReadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ========== Jump to date ==========

    [Fact]
    public async Task PageAt_BuildsExclusiveCursorFromFirstMessageAfterDate()
    {
        var date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var next = new Message { Id = Guid.NewGuid(), CreatedAt = new DateTime(2024, 6, 16, 0, 0, 0, DateTimeKind.Utc) };
        _msgRepoMock
            .Setup(r => r.GetFirstMessageAfterDateAsync(_chatId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);
        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(_chatId, It.IsAny<string?>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>());

        await _service.GetPageAtAsync(_chatId, _userId, date, 20);

        var expected = new CursorDto(next.CreatedAt, next.Id).Encode();
        _msgRepoMock.Verify(r => r.GetMessagesWithSenderCursorAsync(_chatId, expected, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PageAt_NotMember_Is403_BeforeTouchingMessages()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPageAtAsync(Guid.NewGuid(), _userId, DateTime.UtcNow, 20));

        _msgRepoMock.Verify(r => r.GetFirstMessageAfterDateAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
