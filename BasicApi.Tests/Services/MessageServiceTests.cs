using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>Sending and "jump to date".</summary>
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
        _chatRepoMock.WithMembersFromIsMember();
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_userId, _otherId]);
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetAuthorsNewlyReachedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<ReceiptKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // The repository returns what was inserted, with the number and the sender name
        _msgRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Message>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, Guid? clientMessageId, CancellationToken _) => Stored(m.ChatId, m.Text, clientMessageId, m.Id));

        var membership = new MembershipService(_chatRepoMock.Object);
        _service = new MessageService(new FakeDbSession(), _msgRepoMock.Object, membership, new ChatPolicy(membership), _eventsMock.Object, Mock.Of<IDraftRepository>(), Mock.Of<IGroupRepository>(), Mock.Of<IAttachmentRepository>());
    }

    private MessageWithSender Stored(Guid chatId, string text, Guid? clientMessageId = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ChatId = chatId,
        SenderId = _userId,
        Text = text,
        CreatedAt = DateTime.UtcNow,
        Seq = 7,
        ClientMessageId = clientMessageId,
        SenderName = "Alice"
    };

    // ========== Send ==========

    [Fact]
    public async Task Send_ByMember_StoresMessage_AndAnnouncesItToAllMembers()
    {
        var result = await _service.SendAsync(_chatId, _userId, "hello");

        Assert.True(result.Created);
        Assert.Equal("hello", result.Message.Text);
        Assert.Equal("Alice", result.Message.SenderName);
        Assert.Equal(_chatId, result.Message.ChatId);
        Assert.Equal(7, result.Message.Seq);
        _msgRepoMock.Verify(r => r.CreateAsync(
            It.Is<Message>(m => m.ChatId == _chatId && m.SenderId == _userId && m.Text == "hello"),
            null, It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.MessageCreatedAsync(result.Message,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_userId) && ids.Contains(_otherId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_ByNonMember_IsRejected_NothingStoredOrSent()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.SendAsync(Guid.NewGuid(), _userId, "hello"));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
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
        _msgRepoMock.VerifyNoOtherCalls();
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

        var result = await _service.SendAsync(_chatId, _userId, "  " + text + "  ");

        Assert.Equal(text, result.Message.Text);
    }

    // ========== Idempotency ==========

    [Fact]
    public async Task Send_Retry_ReturnsTheFirstMessage_WithoutStoringOrAnnouncingAgain()
    {
        var clientMessageId = Guid.NewGuid();
        var first = Stored(_chatId, "hello", clientMessageId);
        _msgRepoMock
            .Setup(r => r.GetByClientMessageIdAsync(_userId, clientMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);

        var result = await _service.SendAsync(_chatId, _userId, "hello", clientMessageId);

        Assert.False(result.Created);
        Assert.Equal(first.Id, result.Message.Id);
        Assert.Equal(clientMessageId, result.Message.ClientMessageId);
        _msgRepoMock.Verify(r => r.CreateAsync(
            It.IsAny<Message>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Send_ConcurrentRetryWonTheRace_ReturnsTheWinner()
    {
        var clientMessageId = Guid.NewGuid();
        var winner = Stored(_chatId, "hello", clientMessageId);
        _msgRepoMock
            .SetupSequence(r => r.GetByClientMessageIdAsync(_userId, clientMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MessageWithSender?)null) // it does not exist before the insert
            .ReturnsAsync(winner);                   // after the conflict it does
        _msgRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Message>(), clientMessageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateKeyException("duplicate", new Exception()));

        var result = await _service.SendAsync(_chatId, _userId, "hello", clientMessageId);

        Assert.False(result.Created);
        Assert.Equal(winner.Id, result.Message.Id);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Send_ClientMessageIdUsedInAnotherChat_IsAConflict()
    {
        var clientMessageId = Guid.NewGuid();
        _msgRepoMock
            .Setup(r => r.GetByClientMessageIdAsync(_userId, clientMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stored(Guid.NewGuid(), "elsewhere", clientMessageId));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.SendAsync(_chatId, _userId, "hello", clientMessageId));

        Assert.Equal("CLIENT_MESSAGE_ID_CONFLICT", ex.ErrorCode);
    }

    // ========== Jump to date ==========

    [Fact]
    public async Task PageAt_PagesStrictlyBeforeTheFirstMessageAfterDate()
    {
        var date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        _msgRepoMock
            .Setup(r => r.GetFirstSeqAfterAsync(_chatId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(_chatId, _userId, It.IsAny<long?>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>());

        await _service.GetPageAtAsync(_chatId, _userId, date, 20);

        _msgRepoMock.Verify(r => r.GetMessagesWithSenderCursorAsync(_chatId, _userId, 7L, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PageAt_NotMember_Is403_BeforeTouchingMessages()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPageAtAsync(Guid.NewGuid(), _userId, DateTime.UtcNow, 20));

        _msgRepoMock.VerifyNoOtherCalls();
    }
}
