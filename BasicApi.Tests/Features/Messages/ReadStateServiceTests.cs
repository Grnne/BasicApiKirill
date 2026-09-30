using BasicApi.Features.Messages;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Features.Messages;

/// <summary>Read pointer and "marked as unread" (plan 2, F2.1–F2.2).</summary>
public class ReadStateServiceTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly ReadStateService _service;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherId = Guid.NewGuid();

    public ReadStateServiceTests()
    {
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _chatRepoMock
            .Setup(r => r.GetChatListItemAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatListResult
            {
                ChatId = _chatId, LastReadSeq = 9, UnreadCount = 2, UnreadMentionCount = 1, MarkedUnread = false
            });
        _msgRepoMock
            .Setup(r => r.GetAuthorsNewlyReachedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<ReceiptKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var policy = new ChatPolicy(new MembershipService(_chatRepoMock.Object));
        _service = new ReadStateService(new FakeDbSession(), _msgRepoMock.Object, _chatRepoMock.Object, policy, _eventsMock.Object);
    }

    private void PointerMoves(ReadPointerMove move) =>
        _msgRepoMock
            .Setup(r => r.MarkReadAsync(_chatId, _userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(move);

    private void VerifyStatePublished(Times times) =>
        _eventsMock.Verify(e => e.ReadStateChangedAsync(
            It.Is<ReadStateDto>(s => s.ChatId == _chatId && s.LastReadSeq == 9 && s.UnreadCount == 2 &&
                                     s.UnreadMentionCount == 1 && !s.MarkedUnread),
            _userId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task MarkRead_MovesPointer_AndTellsTheUsersDevices()
    {
        var messageId = Guid.NewGuid();
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.Moved, 3, 9));

        await _service.MarkReadAsync(_chatId, _userId, messageId);

        _msgRepoMock.Verify(r => r.MarkReadAsync(_chatId, _userId, messageId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyStatePublished(Times.Once());
    }

    [Fact]
    public async Task MarkRead_TellsTheAuthorsWhoseMessagesFirstBecameRead()
    {
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.Moved, 3, 9));
        _msgRepoMock
            .Setup(r => r.GetAuthorsNewlyReachedAsync(_chatId, _userId, 3, 9, ReceiptKind.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_otherId]);

        await _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid());

        _eventsMock.Verify(e => e.MessagesReadAsync(
            It.Is<ReceiptDto>(r => r.ChatId == _chatId && r.UserId == _userId && r.Seq == 9),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { _otherId })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_WhenNobodyNewIsReached_TellsOnlyTheUsersDevices()
    {
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.Moved, 3, 9));

        await _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid());

        VerifyStatePublished(Times.Once());
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_OlderMessage_IsNotAnError_AndSendsNothing()
    {
        // The pointer does not move backwards, but for the client this is not an error: they have already read further.
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.NotMoved, 9, 9));

        await _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid());

        _msgRepoMock.Verify(r => r.GetAuthorsNewlyReachedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<ReceiptKind>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_WithNothingNew_StillClearsTheMark_AndTellsTheDevices()
    {
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.NotMoved, 9, 9, ClearedMark: true));

        await _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid());

        VerifyStatePublished(Times.Once());
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_MessageNotInChat_Is404()
    {
        PointerMoves(new ReadPointerMove(ReadPointerUpdate.MessageNotFound));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.MarkReadAsync(_chatId, _userId, Guid.NewGuid()));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_NotMember_Is403()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.MarkReadAsync(Guid.NewGuid(), _userId, Guid.NewGuid()));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetMarkedUnread_WhenItChanges_TellsTheUsersDevices()
    {
        _chatRepoMock
            .Setup(r => r.SetMarkedUnreadAsync(_chatId, _userId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _chatRepoMock
            .Setup(r => r.GetChatListItemAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatListResult { ChatId = _chatId, LastReadSeq = 9, MarkedUnread = true });

        await _service.SetMarkedUnreadAsync(_chatId, _userId, true);

        _eventsMock.Verify(e => e.ReadStateChangedAsync(
            It.Is<ReadStateDto>(s => s.ChatId == _chatId && s.MarkedUnread), _userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetMarkedUnread_AlreadySo_SendsNothing()
    {
        _chatRepoMock
            .Setup(r => r.SetMarkedUnreadAsync(_chatId, _userId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _service.SetMarkedUnreadAsync(_chatId, _userId, true);

        _eventsMock.VerifyNoOtherCalls();
    }
}
